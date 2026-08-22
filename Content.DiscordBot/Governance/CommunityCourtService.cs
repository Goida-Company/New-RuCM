using System.Data;
using System.Text.Json;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed class CommunityCourtService(
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory,
    CourtPolicy policy,
    CandidateSelectionService selection)
{
    public CourtPolicy Policy => policy;

    public async Task SyncLinkedAccountsAsync()
    {
        await using var game = gameFactory();
        var linked = await game.RMCLinkedAccounts
            .AsNoTracking()
            .Select(account => new LinkedGameAccount(account.PlayerId, account.DiscordId, account.Player.LastSeenUserName))
            .ToListAsync();

        await using var governance = governanceFactory();
        foreach (var account in linked)
            await UpsertGovernanceUserAsync(governance, account);
    }

    public async Task SyncLinkedAccountAsync(Guid playerId, ulong discordId)
    {
        await using var game = gameFactory();
        var account = await game.Player.AsNoTracking()
            .Where(player => player.UserId == playerId)
            .Select(player => new LinkedGameAccount(player.UserId, discordId, player.LastSeenUserName))
            .SingleAsync();
        await using var governance = governanceFactory();
        await UpsertGovernanceUserAsync(governance, account);
    }

    public async Task<GovernanceCourtCase> FileCaseAsync(
        ulong claimantDiscordId,
        ulong defendantDiscordId,
        int roundId,
        string summary,
        string evidenceReference)
    {
        summary = summary.Trim();
        evidenceReference = evidenceReference.Trim();
        if (summary.Length is < 20 or > 1500)
            throw new CourtRuleException("Описание жалобы должно содержать от 20 до 1500 символов.");
        if (string.IsNullOrWhiteSpace(evidenceReference))
            throw new CourtRuleException("Нужно приложить клип, файл или ссылку на реплей.");

        var claimant = await RequireLinkedAccountAsync(claimantDiscordId);
        var defendant = await RequireLinkedAccountAsync(defendantDiscordId);
        if (claimant.PlayerId == defendant.PlayerId)
            throw new CourtRuleException("Нельзя подать жалобу на самого себя.");

        await ValidateRoundAsync(roundId, claimant.PlayerId, defendant.PlayerId);
        var now = DateTime.UtcNow;
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var claimantUser = await EnsureGovernanceUserAsync(governance, claimant);
        var defendantUser = await EnsureGovernanceUserAsync(governance, defendant);
        var courtCase = governance.CourtCases.Add(new GovernanceCourtCase
        {
            ClaimantUserId = claimantUser.Id,
            DefendantUserId = defendantUser.Id,
            RoundId = roundId,
            Summary = summary,
            Status = CourtStatuses.Defense,
            FiledAt = now,
            DefenseDeadline = now + policy.DefensePeriod,
        }).Entity;
        await governance.SaveChangesAsync();
        governance.CourtStatements.Add(new GovernanceCourtStatement
        {
            CaseId = courtCase.Id,
            AuthorUserId = claimantUser.Id,
            Kind = "complaint",
            Body = summary,
            EvidenceReference = evidenceReference,
            CreatedAt = now,
        });
        governance.CourtParticipants.AddRange(
            new GovernanceCourtParticipant { CaseId = courtCase.Id, UserId = claimantUser.Id, Role = "claimant", AddedAt = now },
            new GovernanceCourtParticipant { CaseId = courtCase.Id, UserId = defendantUser.Id, Role = "defendant", AddedAt = now });
        AddAudit(governance, "court.case_filed", "discord_user", claimantDiscordId.ToString(), courtCase.Id,
            new { round_id = roundId, defendant_user_id = defendantUser.Id });
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        return courtCase;
    }

    // Kept for API compatibility. New slash filing uses CourtFilingService so the defendant does not
    // need Discord; this legacy overload still requires a Discord-backed defendant.
    public async Task<GovernanceCourtCase> FileCaseByGameNicknameAsync(
        ulong claimantDiscordId,
        string defendantGameNickname,
        int roundId,
        string summary,
        string evidenceReference)
    {
        var defendant = await RequireLinkedAccountByGameNicknameAsync(defendantGameNickname);
        if (defendant.DiscordId is not { } defendantDiscordId)
            throw new CourtRuleException("Для этого устаревшего пути подачи требуется Discord ответчика.");
        return await FileCaseAsync(claimantDiscordId, defendantDiscordId, roundId, summary, evidenceReference);
    }

    public static string NormalizeGameNickname(string nickname)
    {
        nickname = nickname.Trim();
        if (nickname.Length is < 1 or > 64)
            throw new CourtRuleException("Игровой никнейм ответчика должен содержать от 1 до 64 символов.");
        return nickname;
    }

    public async Task<GovernanceCourtStatement> SubmitDefenseAsync(
        long caseId,
        ulong discordId,
        string body,
        string? evidenceReference)
    {
        body = body.Trim();
        evidenceReference = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
        if (body.Length is < 20 or > 3000)
            throw new CourtRuleException("Текст защиты должен содержать от 20 до 3000 символов.");

        var account = await RequireLinkedAccountAsync(discordId);
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCaseAsync(governance, caseId);
        var user = await EnsureGovernanceUserAsync(governance, account);
        var courtCase = await governance.CourtCases.SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.DefendantUserId != user.Id)
            throw new CourtRuleException("Защиту может подать только ответчик.");
        if (courtCase.Status != CourtStatuses.Defense || courtCase.DefenseDeadline < DateTime.UtcNow)
            throw new CourtRuleException("Стадия защиты по этому делу завершена.");
        if (await governance.CourtStatements.AnyAsync(value => value.CaseId == caseId && value.Kind == "defense"))
            throw new CourtRuleException("Защита по этому делу уже подана.");

        var statement = governance.CourtStatements.Add(new GovernanceCourtStatement
        {
            CaseId = caseId,
            AuthorUserId = user.Id,
            Kind = "defense",
            Body = body,
            EvidenceReference = evidenceReference,
            CreatedAt = DateTime.UtcNow,
        }).Entity;
        AddAudit(governance, "court.defense_submitted", "discord_user", discordId.ToString(), caseId, new { });
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        return statement;
    }

    public async Task AddWitnessAsync(long caseId, ulong defendantDiscordId, ulong witnessDiscordId)
    {
        var defendant = await RequireLinkedAccountAsync(defendantDiscordId);
        var witness = await RequireLinkedAccountAsync(witnessDiscordId);
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCaseAsync(governance, caseId);
        var defendantUser = await EnsureGovernanceUserAsync(governance, defendant);
        var witnessUser = await EnsureGovernanceUserAsync(governance, witness);
        var courtCase = await governance.CourtCases.SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.DefendantUserId != defendantUser.Id)
            throw new CourtRuleException("Свидетелей может приглашать только ответчик.");
        if (courtCase.Status != CourtStatuses.Defense || courtCase.DefenseDeadline <= DateTime.UtcNow)
            throw new CourtRuleException("Список свидетелей уже закрыт.");
        if (witnessUser.Id == courtCase.ClaimantUserId || witnessUser.Id == courtCase.DefendantUserId)
            throw new CourtRuleException("Сторона дела не может быть добавлена как свидетель.");
        if (await governance.CourtParticipants.AnyAsync(value => value.CaseId == caseId && value.UserId == witnessUser.Id))
            return;
        governance.CourtParticipants.Add(new GovernanceCourtParticipant
        {
            CaseId = caseId,
            UserId = witnessUser.Id,
            Role = "witness",
            AddedAt = DateTime.UtcNow,
        });
        governance.Conflicts.Add(new GovernanceConflict
        {
            UserId = witnessUser.Id,
            EntityType = "court_case",
            EntityId = caseId.ToString(),
            Reason = "witness",
            StartsAt = DateTime.UtcNow,
            CreatedByType = "system",
        });
        AddAudit(governance, "court.witness_added", "discord_user", defendantDiscordId.ToString(), caseId,
            new { witness_user_id = witnessUser.Id });
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<GovernanceCourtStatement> SubmitWitnessStatementAsync(
        long caseId,
        ulong witnessDiscordId,
        string body,
        string? evidenceReference)
    {
        body = body.Trim();
        if (body.Length is < 20 or > 3000)
            throw new CourtRuleException("Показание должно содержать от 20 до 3000 символов.");
        var account = await RequireLinkedAccountAsync(witnessDiscordId);
        await using var governance = governanceFactory();
        var user = await EnsureGovernanceUserAsync(governance, account);
        var courtCase = await governance.CourtCases.SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.Status != CourtStatuses.Defense || courtCase.DefenseDeadline <= DateTime.UtcNow)
            throw new CourtRuleException("Приём свидетельских показаний завершён.");
        if (!await governance.CourtParticipants.AnyAsync(value => value.CaseId == caseId && value.UserId == user.Id && value.Role == "witness"))
            throw new CourtRuleException("Вы не зарегистрированы свидетелем по этому делу.");
        if (await governance.CourtStatements.AnyAsync(value => value.CaseId == caseId && value.AuthorUserId == user.Id && value.Kind == "witness"))
            throw new CourtRuleException("Ваше показание уже принято.");
        var statement = governance.CourtStatements.Add(new GovernanceCourtStatement
        {
            CaseId = caseId,
            AuthorUserId = user.Id,
            Kind = "witness",
            Body = body,
            EvidenceReference = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim(),
            CreatedAt = DateTime.UtcNow,
        }).Entity;
        AddAudit(governance, "court.witness_statement_submitted", "discord_user", witnessDiscordId.ToString(), caseId, new { });
        await governance.SaveChangesAsync();
        return statement;
    }

    public async Task<string> RespondToInvitationAsync(long caseId, ulong discordId, string response, string? recusalReason)
    {
        if (response is not (InvitationStates.Accepted or InvitationStates.Declined or InvitationStates.Recused))
            throw new CourtRuleException("Неизвестный ответ на приглашение.");
        if (response == InvitationStates.Recused && string.IsNullOrWhiteSpace(recusalReason))
            throw new CourtRuleException("Для самоотвода нужно кратко указать причину.");

        var account = await RequireLinkedAccountAsync(discordId);
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCaseAsync(governance, caseId);
        var user = await EnsureGovernanceUserAsync(governance, account);
        var juror = await governance.Jurors.SingleOrDefaultAsync(value => value.CaseId == caseId && value.UserId == user.Id)
            ?? throw new CourtRuleException("У вас нет приглашения по этому делу.");
        var invitation = await governance.Invitations.SingleAsync(value => value.Id == juror.InvitationId);
        if (invitation.State != InvitationStates.Pending)
        {
            if (invitation.State == response)
                return invitation.State;
            throw new CourtRuleException("Ответ на приглашение уже зафиксирован.");
        }

        var now = DateTime.UtcNow;
        if (invitation.ExpiresAt <= now)
        {
            invitation.State = InvitationStates.Expired;
            invitation.RespondedAt = now;
            invitation.Version++;
            await governance.SaveChangesAsync();
            await transaction.CommitAsync();
            throw new CourtRuleException("Срок ответа на приглашение истёк. Неответ на приглашение сам по себе не снижает репутацию.");
        }

        invitation.State = response;
        invitation.RespondedAt = now;
        invitation.RecusalReason = response == InvitationStates.Recused ? recusalReason!.Trim() : null;
        invitation.Version++;
        juror.Active = response == InvitationStates.Accepted;
        if (response == InvitationStates.Accepted &&
            !await governance.ServiceAssignments.AnyAsync(value =>
                value.UserId == user.Id && value.Track == "jury" &&
                value.EntityType == "court_case" && value.EntityId == caseId.ToString()))
        {
            governance.ServiceAssignments.Add(new GovernanceServiceAssignment
            {
                UserId = user.Id,
                Track = "jury",
                EntityType = "court_case",
                EntityId = caseId.ToString(),
                AssignedAt = now,
            });
        }
        AddAudit(governance, $"invitation.{response}", "discord_user", discordId.ToString(), caseId,
            new { invitation_id = invitation.Id, recusal_reason = invitation.RecusalReason, reputation_neutral = true });
        await governance.SaveChangesAsync();
        await StartOrResumeVotingAsync(governance, caseId, now);
        await transaction.CommitAsync();
        return response;
    }

    public async Task<CourtVoteOutcome> SubmitGuiltVoteAsync(long caseId, ulong discordId, string verdict, string reasoning)
    {
        if (verdict is not (CourtVerdicts.Guilty or CourtVerdicts.NotGuilty or CourtVerdicts.InsufficientEvidence))
            throw new CourtRuleException("Неизвестный вариант вердикта.");
        ValidateReasoning(reasoning);
        var account = await RequireLinkedAccountAsync(discordId);
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCaseAsync(governance, caseId);
        var user = await EnsureGovernanceUserAsync(governance, account);
        var courtCase = await RequireVotingCaseAsync(governance, caseId, CourtStatuses.Jury, "Голосование о виновности сейчас недоступно.");
        await RequireActiveJurorAsync(governance, caseId, user.Id);
        if (await governance.GuiltVotes.AnyAsync(value => value.CaseId == caseId && value.JurorUserId == user.Id))
            throw new CourtRuleException("Ваш голос по этому делу уже принят.");

        governance.GuiltVotes.Add(new GovernanceGuiltVote
        {
            CaseId = caseId,
            JurorUserId = user.Id,
            Verdict = verdict,
            Reasoning = reasoning.Trim(),
            SubmittedAt = DateTime.UtcNow,
            IdempotencyKey = $"court:{caseId}:guilt:{user.Id}",
        });
        AddAudit(governance, "court.guilt_vote_submitted", "discord_user", discordId.ToString(), caseId, new { juror_user_id = user.Id });
        await governance.SaveChangesAsync();
        var votes = await governance.GuiltVotes.Where(value => value.CaseId == caseId).Select(value => value.Verdict).ToListAsync();
        var resolved = CourtDecisionPolicy.ResolveGuilt(votes, policy.DecisionThreshold, policy.JurySize);
        if (resolved == CourtVerdicts.Guilty)
        {
            courtCase.Verdict = resolved;
            courtCase.Status = CourtStatuses.Sentencing;
            courtCase.SentencingStartedAt = DateTime.UtcNow;
            courtCase.SentencingDeadline = DateTime.UtcNow + policy.VotePeriod;
            courtCase.Version++;
            AddAudit(governance, "court.guilt_decided", "system", null, caseId, new { verdict = resolved });
        }
        else if (resolved != null)
        {
            courtCase.Verdict = resolved;
            courtCase.Status = CourtStatuses.Verdict;
            courtCase.Version++;
            await CompleteJurorAssignmentsAsync(governance, caseId, false);
            AddAudit(governance, "court.verdict_created", "system", null, caseId, new { verdict = resolved });
        }
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        return new CourtVoteOutcome(resolved);
    }

    public async Task<CourtVoteOutcome> SubmitSentencingVoteAsync(
        long caseId,
        ulong discordId,
        string sanctionType,
        short? days,
        string? role,
        string reasoning)
    {
        ValidateSentence(sanctionType, ref days, ref role);
        ValidateReasoning(reasoning);
        var account = await RequireLinkedAccountAsync(discordId);
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCaseAsync(governance, caseId);
        var user = await EnsureGovernanceUserAsync(governance, account);
        var courtCase = await RequireVotingCaseAsync(governance, caseId, CourtStatuses.Sentencing, "Голосование о наказании сейчас недоступно.");
        await RequireActiveJurorAsync(governance, caseId, user.Id);
        if (await governance.SentencingVotes.AnyAsync(value => value.CaseId == caseId && value.JurorUserId == user.Id))
            throw new CourtRuleException("Ваш голос о наказании уже принят.");

        governance.SentencingVotes.Add(new GovernanceSentencingVote
        {
            CaseId = caseId,
            JurorUserId = user.Id,
            SanctionType = sanctionType,
            SanctionDays = days,
            SanctionRole = role,
            Reasoning = reasoning.Trim(),
            SubmittedAt = DateTime.UtcNow,
            IdempotencyKey = $"court:{caseId}:sentence:{user.Id}",
        });
        AddAudit(governance, "court.sentencing_vote_submitted", "discord_user", discordId.ToString(), caseId,
            new { juror_user_id = user.Id });
        await governance.SaveChangesAsync();
        var votes = await governance.SentencingVotes.Where(value => value.CaseId == caseId)
            .Select(value => new { value.SanctionType, value.SanctionDays, value.SanctionRole })
            .ToListAsync();
        var resolved = CourtDecisionPolicy.ResolveSentence(
            votes.Select(value => (value.SanctionType, value.SanctionDays, value.SanctionRole)),
            policy.DecisionThreshold,
            policy.JurySize);
        if (resolved != null)
        {
            courtCase.SanctionType = resolved.Value.Type;
            courtCase.SanctionDays = resolved.Value.Days;
            courtCase.SanctionRole = resolved.Value.Role;
            courtCase.Status = CourtStatuses.Verdict;
            courtCase.Version++;
            await CompleteJurorAssignmentsAsync(governance, caseId, true);
            AddAudit(governance, "court.verdict_created", "system", null, caseId,
                new { verdict = courtCase.Verdict, sanction_type = resolved.Value.Type, sanction_days = resolved.Value.Days, sanction_role = resolved.Value.Role });
        }
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        return new CourtVoteOutcome(courtCase.Verdict, resolved?.Type, resolved?.Days, resolved?.Role);
    }

    public async Task ProcessDeadlinesAsync(IReadOnlySet<ulong>? availableDiscordIds)
    {
        await SyncLinkedAccountsAsync();
        var now = DateTime.UtcNow;
        await using (var governance = governanceFactory())
        await using (var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var defenses = await governance.CourtCases
                .Where(value => value.Status == CourtStatuses.Defense && value.DefenseDeadline <= now)
                .ToListAsync();
            foreach (var courtCase in defenses)
            {
                courtCase.Status = CourtStatuses.AwaitingJury;
                courtCase.Version++;
                AddAudit(governance, "court.defense_expired", "system", null, courtCase.Id, new { });
            }

            var assignments = await governance.Jurors.Join(governance.Invitations,
                    juror => juror.InvitationId,
                    invitation => invitation.Id,
                    (juror, invitation) => new { Juror = juror, Invitation = invitation })
                .Where(value => !value.Juror.Active && value.Invitation.State == InvitationStates.Accepted)
                .ToListAsync();
            foreach (var assignment in assignments)
                assignment.Juror.Active = true;

            var expired = await governance.Invitations
                .Where(value => value.State == InvitationStates.Pending && value.ExpiresAt <= now && value.Purpose == "jury")
                .ToListAsync();
            foreach (var invitation in expired)
            {
                invitation.State = InvitationStates.Expired;
                invitation.RespondedAt = now;
                invitation.Version++;
                AddAudit(governance, "invitation.expired", "system", null, long.Parse(invitation.EntityId),
                    new { invitation_id = invitation.Id, reputation_neutral = true });
            }

            var timedOut = await governance.CourtCases
                .Where(value =>
                    value.Status == CourtStatuses.Jury && value.GuiltDeadline <= now ||
                    value.Status == CourtStatuses.Sentencing && value.SentencingDeadline <= now)
                .ToListAsync();
            foreach (var courtCase in timedOut)
            {
                var voted = courtCase.Status == CourtStatuses.Jury
                    ? await governance.GuiltVotes.Where(value => value.CaseId == courtCase.Id).Select(value => value.JurorUserId).ToListAsync()
                    : await governance.SentencingVotes.Where(value => value.CaseId == courtCase.Id).Select(value => value.JurorUserId).ToListAsync();
                var missing = await governance.Jurors
                    .Where(value => value.CaseId == courtCase.Id && value.Active && !voted.Contains(value.UserId))
                    .ToListAsync();
                foreach (var juror in missing)
                {
                    juror.Active = false;
                    var assignment = await governance.ServiceAssignments.SingleOrDefaultAsync(value =>
                        value.UserId == juror.UserId && value.Track == "jury" && value.EntityType == "court_case" && value.EntityId == courtCase.Id.ToString());
                    if (assignment != null && assignment.CompletedAt == null && assignment.FailedAt == null)
                        assignment.FailedAt = now;
                }
                if (courtCase.Status == CourtStatuses.Jury)
                    courtCase.GuiltDeadline = null;
                else
                    courtCase.SentencingDeadline = null;
                courtCase.Version++;
                AddAudit(governance, "court.jurors_timed_out", "system", null, courtCase.Id,
                    new { juror_user_ids = missing.Select(value => value.UserId).ToArray() });
            }

            await governance.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var casesContext = governanceFactory();
        var caseIds = await casesContext.CourtCases
            .Where(value => value.Status == CourtStatuses.AwaitingJury || value.Status == CourtStatuses.Jury || value.Status == CourtStatuses.Sentencing)
            .Select(value => value.Id)
            .ToListAsync();
        foreach (var caseId in caseIds)
        {
            await SelectJurorsAsync(caseId, availableDiscordIds);
            await using var votingContext = governanceFactory();
            await StartOrResumeVotingAsync(votingContext, caseId, now);
        }
    }

    public async Task<IReadOnlyList<(GovernanceInvitation Invitation, GovernanceUser User)>> PendingNotificationsAsync()
    {
        await using var governance = governanceFactory();
        return await governance.Invitations.AsNoTracking()
            .Where(value => value.Purpose == "jury" && value.State == InvitationStates.Pending && value.DiscordNotifiedAt == null)
            .Join(governance.Users, invitation => invitation.UserId, user => user.Id, (invitation, user) => new { invitation, user })
            .Select(value => ValueTuple.Create(value.invitation, value.user))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ulong>> LinkedDiscordIdsAsync()
    {
        await using var governance = governanceFactory();
        return await governance.Users.AsNoTracking()
            .Where(value => value.DiscordUserId != null && value.DiscordUserId > 0)
            .Select(value => checked((ulong) value.DiscordUserId!.Value))
            .ToListAsync();
    }

    public async Task MarkInvitationNotifiedAsync(long invitationId)
    {
        await using var governance = governanceFactory();
        var invitation = await governance.Invitations.SingleAsync(value => value.Id == invitationId);
        invitation.DiscordNotifiedAt = DateTime.UtcNow;
        await governance.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<GovernanceCourtCase>> CasesWithoutThreadsAsync()
    {
        await using var governance = governanceFactory();
        return await governance.CourtCases.AsNoTracking()
            .Where(value => value.DiscordThreadId == null && value.Status != CourtStatuses.Overturned)
            .OrderBy(value => value.Id)
            .ToListAsync();
    }

    public async Task AttachThreadAsync(long caseId, ulong threadId)
    {
        await using var governance = governanceFactory();
        var courtCase = await governance.CourtCases.SingleAsync(value => value.Id == caseId);
        if (courtCase.DiscordThreadId != null && courtCase.DiscordThreadId != (long) threadId)
            throw new CourtRuleException("К делу уже привязан другой Discord-тред.");
        courtCase.DiscordThreadId = (long) threadId;
        courtCase.Version++;
        await governance.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<GovernanceCourtCase>> UnpublishedVerdictsAsync()
    {
        await using var governance = governanceFactory();
        return await governance.CourtCases.AsNoTracking()
            .Where(value => (value.Status == CourtStatuses.Verdict || value.Status == CourtStatuses.Executed) && value.PublishedAt == null)
            .OrderBy(value => value.Id)
            .ToListAsync();
    }

    public async Task MarkPublishedAsync(long caseId, ulong messageId)
    {
        await using var governance = governanceFactory();
        var courtCase = await governance.CourtCases.SingleAsync(value => value.Id == caseId);
        courtCase.VerdictMessageId = (long) messageId;
        courtCase.PublishedAt = DateTime.UtcNow;
        courtCase.Version++;
        await governance.SaveChangesAsync();
    }

    public async Task<GovernanceCourtCase> GetCaseAsync(long caseId)
    {
        await using var governance = governanceFactory();
        return await governance.CourtCases.AsNoTracking().SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
    }

    public async Task<IReadOnlyList<GovernanceCourtStatement>> GetStatementsAsync(long caseId)
    {
        await using var governance = governanceFactory();
        return await governance.CourtStatements.AsNoTracking().Where(value => value.CaseId == caseId)
            .OrderBy(value => value.CreatedAt).ToListAsync();
    }

    public async Task<bool> CanWriteThreadAsync(ulong threadId, ulong discordId)
    {
        await using var governance = governanceFactory();
        var userId = await governance.Users.AsNoTracking()
            .Where(value => value.DiscordUserId == checked((long) discordId))
            .Select(value => (Guid?) value.Id)
            .SingleOrDefaultAsync();
        if (userId == null)
            return false;
        var caseId = await governance.CourtCases.AsNoTracking()
            .Where(value => value.DiscordThreadId == checked((long) threadId))
            .Select(value => (long?) value.Id)
            .SingleOrDefaultAsync();
        return caseId != null && await governance.CourtParticipants.AsNoTracking()
            .AnyAsync(value => value.CaseId == caseId && value.UserId == userId);
    }

    public async Task<bool> IsCourtThreadAsync(ulong threadId)
    {
        await using var governance = governanceFactory();
        return await governance.CourtCases.AsNoTracking()
            .AnyAsync(value => value.DiscordThreadId == checked((long) threadId));
    }

    public async Task<bool> IsSentencingJurorAsync(long caseId, ulong discordId)
    {
        var account = await RequireLinkedAccountAsync(discordId);
        await using var governance = governanceFactory();
        var userId = await governance.Users.Where(value => value.Ss14UserId == account.PlayerId).Select(value => value.Id).SingleAsync();
        return await governance.CourtCases.AnyAsync(value => value.Id == caseId && value.Status == CourtStatuses.Sentencing) &&
               await governance.Jurors.AnyAsync(value => value.CaseId == caseId && value.UserId == userId && value.Active);
    }

    public async Task<LinkedGameAccount> GetAccountAsync(Guid governanceUserId)
    {
        await using var governance = governanceFactory();
        var user = await governance.Users.AsNoTracking().SingleAsync(value => value.Id == governanceUserId);
        await using var game = gameFactory();
        var name = await game.Player.AsNoTracking()
            .Where(value => value.UserId == user.Ss14UserId)
            .Select(value => value.LastSeenUserName)
            .SingleOrDefaultAsync() ?? user.Ss14UserId.ToString();
        var discordId = user.DiscordUserId is > 0 ? checked((ulong?) user.DiscordUserId.Value) : null;
        return new LinkedGameAccount(user.Ss14UserId, discordId, name);
    }

    private async Task SelectJurorsAsync(long caseId, IReadOnlySet<ulong>? availableDiscordIds)
    {
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockCaseAsync(governance, caseId);
        var courtCase = await governance.CourtCases.SingleAsync(value => value.Id == caseId);
        var pendingInvitationIds = await governance.Invitations
            .Where(value => value.State == InvitationStates.Pending && value.Purpose == "jury")
            .Select(value => value.Id).ToListAsync();
        var active = await governance.Jurors.CountAsync(value => value.CaseId == caseId && value.Active);
        var pending = await governance.Jurors.CountAsync(value => value.CaseId == caseId && pendingInvitationIds.Contains(value.InvitationId));
        var slots = policy.JurySize - active - pending;
        if (slots <= 0)
            return;

        var used = await governance.Jurors.Where(value => value.CaseId == caseId).Select(value => value.UserId).ToListAsync();
        var participants = await governance.CourtParticipants.Where(value => value.CaseId == caseId).Select(value => value.UserId).ToListAsync();
        var excluded = used.Concat(participants).Append(courtCase.ClaimantUserId).Append(courtCase.DefendantUserId).Distinct().ToArray();
        var candidates = await selection.SelectAsync("jury", 1, "court_case", caseId.ToString(), slots,
            excluded, availableDiscordIds, policy.SelectionCooldown);

        foreach (var candidate in candidates)
        {
            var invitation = governance.Invitations.Add(new GovernanceInvitation
            {
                UserId = candidate.Id,
                EntityType = "court_case",
                EntityId = caseId.ToString(),
                Purpose = "jury",
                State = InvitationStates.Pending,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow + policy.InvitationPeriod,
                IdempotencyKey = $"jury:{caseId}:{candidate.Id}",
            }).Entity;
            await governance.SaveChangesAsync();
            governance.Jurors.Add(new GovernanceJuror
            {
                CaseId = caseId,
                UserId = candidate.Id,
                InvitationId = invitation.Id,
                AssignedAt = DateTime.UtcNow,
            });
            AddAudit(governance, "court.juror_invited", "system", null, caseId,
                new { invitation_id = invitation.Id, juror_user_id = candidate.Id, obligation_created = false });
        }
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task StartOrResumeVotingAsync(GovernanceDbContext governance, long caseId, DateTime now)
    {
        var courtCase = await governance.CourtCases.SingleAsync(value => value.Id == caseId);
        var active = await governance.Jurors.CountAsync(value => value.CaseId == caseId && value.Active);
        if (active < policy.JurySize)
            return;
        if (courtCase.Status == CourtStatuses.AwaitingJury)
        {
            courtCase.Status = CourtStatuses.Jury;
            courtCase.GuiltStartedAt = now;
            courtCase.GuiltDeadline = now + policy.VotePeriod;
            courtCase.Version++;
            AddAudit(governance, "court.jury_ready", "system", null, caseId, new { });
        }
        else if (courtCase.Status == CourtStatuses.Jury && courtCase.GuiltDeadline == null)
        {
            courtCase.GuiltDeadline = now + policy.VotePeriod;
            courtCase.Version++;
        }
        else if (courtCase.Status == CourtStatuses.Sentencing && courtCase.SentencingDeadline == null)
        {
            courtCase.SentencingDeadline = now + policy.VotePeriod;
            courtCase.Version++;
        }
        await governance.SaveChangesAsync();
    }

    private async Task ValidateRoundAsync(int roundId, Guid claimant, Guid defendant)
    {
        await using var game = gameFactory();
        var round = await game.Round.AsNoTracking().Where(value => value.Id == roundId)
            .Select(value => new { value.ServerId, value.StartDate })
            .SingleOrDefaultAsync() ?? throw new CourtRuleException("Раунд не найден или ещё не завершён.");
        if (round.StartDate == null)
            throw new CourtRuleException("У раунда отсутствует время начала.");
        var endedAt = await game.Round.AsNoTracking()
            .Where(value => value.ServerId == round.ServerId && value.StartDate > round.StartDate)
            .MinAsync(value => value.StartDate);
        if (endedAt == null)
            throw new CourtRuleException("Раунд не найден или ещё не завершён.");
        if (DateTime.UtcNow - endedAt.Value.ToUniversalTime() > policy.ComplaintWindow)
            throw new CourtRuleException("После окончания раунда прошло больше допустимого срока.");
        var participants = await game.Round.AsNoTracking().Where(value => value.Id == roundId)
            .SelectMany(value => value.Players)
            .CountAsync(value => value.UserId == claimant || value.UserId == defendant);
        if (participants != 2)
            throw new CourtRuleException("Обе стороны должны быть участниками указанного раунда.");
    }

    private async Task<LinkedGameAccount> RequireLinkedAccountAsync(ulong discordId)
    {
        await using var game = gameFactory();
        return await game.RMCLinkedAccounts.AsNoTracking()
            .Where(value => value.DiscordId == discordId)
            .Select(value => new LinkedGameAccount(value.PlayerId, value.DiscordId, value.Player.LastSeenUserName))
            .SingleOrDefaultAsync() ?? throw new CourtRuleException("Discord-аккаунт не привязан к аккаунту SS14.");
    }

    private async Task<LinkedGameAccount> RequireLinkedAccountByGameNicknameAsync(string gameNickname)
    {
        gameNickname = NormalizeGameNickname(gameNickname);
        var normalized = gameNickname.ToLower();
        await using var game = gameFactory();
        var matches = await game.Player.AsNoTracking()
            .Where(value => value.LastSeenUserName.ToLower() == normalized)
            .Select(value => new
            {
                value.UserId,
                value.LastSeenUserName,
                DiscordId = value.LinkedAccount == null ? null : (ulong?) value.LinkedAccount.DiscordId,
            })
            .Take(3)
            .ToListAsync();

        if (matches.Count == 0)
            throw new CourtRuleException($"Игрок с никнеймом «{gameNickname}» не найден.");

        var exactMatches = matches.Where(value => value.LastSeenUserName == gameNickname).ToArray();
        var selected = exactMatches.Length == 1
            ? exactMatches[0]
            : matches.Count == 1
                ? matches[0]
                : throw new CourtRuleException("Найдено несколько игроков с таким никнеймом. Укажите его с точным регистром.");

        return new LinkedGameAccount(selected.UserId, selected.DiscordId, selected.LastSeenUserName);
    }

    private static async Task<GovernanceUser> EnsureGovernanceUserAsync(GovernanceDbContext governance, LinkedGameAccount account)
    {
        await UpsertGovernanceUserAsync(governance, account);
        return await governance.Users.SingleAsync(value => value.Ss14UserId == account.PlayerId);
    }

    private static async Task UpsertGovernanceUserAsync(GovernanceDbContext governance, LinkedGameAccount account)
    {
        long? discordId = account.DiscordId is { } value ? checked((long) value) : null;
        await governance.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO governance.users(ss14_user_id, discord_user_id, civic_rating_cache)
            VALUES ({account.PlayerId}, {discordId}, {ReputationPolicy.NeutralScore})
            ON CONFLICT (ss14_user_id) DO UPDATE
            SET discord_user_id = COALESCE(EXCLUDED.discord_user_id, governance.users.discord_user_id), updated_at = now()
            """);
        await governance.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO governance.qualifications(user_id, track, level)
            SELECT id, 'jury', 1 FROM governance.users WHERE ss14_user_id = {account.PlayerId}
            ON CONFLICT (user_id, track) DO NOTHING
            """);
    }

    private static async Task LockCaseAsync(GovernanceDbContext governance, long caseId)
    {
        await governance.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({caseId})");
    }

    private static async Task<GovernanceCourtCase> RequireVotingCaseAsync(
        GovernanceDbContext governance,
        long caseId,
        string status,
        string message)
    {
        var courtCase = await governance.CourtCases.SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.Status != status)
            throw new CourtRuleException(message);
        var deadline = status == CourtStatuses.Jury ? courtCase.GuiltDeadline : courtCase.SentencingDeadline;
        if (deadline == null || deadline <= DateTime.UtcNow)
            throw new CourtRuleException("Срок голосования истёк; ожидается замена неявившихся.");
        return courtCase;
    }

    private static async Task RequireActiveJurorAsync(GovernanceDbContext governance, long caseId, Guid userId)
    {
        if (!await governance.Jurors.AnyAsync(value => value.CaseId == caseId && value.UserId == userId && value.Active))
            throw new CourtRuleException("Голосовать может только действующий присяжный по этому делу.");
    }

    private static async Task CompleteJurorAssignmentsAsync(GovernanceDbContext governance, long caseId, bool sentencing)
    {
        var jurors = sentencing
            ? await governance.SentencingVotes.Where(value => value.CaseId == caseId).Select(value => value.JurorUserId).ToListAsync()
            : await governance.GuiltVotes.Where(value => value.CaseId == caseId).Select(value => value.JurorUserId).ToListAsync();
        foreach (var juror in jurors)
        {
            var assignment = await governance.ServiceAssignments.SingleOrDefaultAsync(value => value.UserId == juror &&
                value.Track == "jury" && value.EntityType == "court_case" && value.EntityId == caseId.ToString());
            if (assignment != null && assignment.CompletedAt == null && assignment.FailedAt == null)
                assignment.CompletedAt = DateTime.UtcNow;
        }
    }

    private static void AddAudit(
        GovernanceDbContext governance,
        string eventType,
        string actorType,
        string? actorId,
        long caseId,
        object payload)
    {
        governance.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = eventType,
            ActorType = actorType,
            ActorId = actorId,
            EntityType = "court_case",
            EntityId = caseId.ToString(),
            CreatedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        });
    }

    private static void ValidateReasoning(string reasoning)
    {
        if (reasoning.Trim().Length is < 20 or > 1500)
            throw new CourtRuleException("Обоснование должно содержать от 20 до 1500 символов.");
    }

    private static void ValidateSentence(string sanctionType, ref short? days, ref string? role)
    {
        if (sanctionType == CourtSanctions.Warning)
        {
            days = null;
            role = null;
            return;
        }
        if (sanctionType is not (CourtSanctions.GameBan or CourtSanctions.JobBan))
            throw new CourtRuleException("Неизвестный тип наказания.");
        if (days is < 1 or > 7)
            throw new CourtRuleException("Бан может длиться от одного до семи дней.");
        if (sanctionType == CourtSanctions.JobBan)
        {
            role = role?.Trim();
            if (string.IsNullOrWhiteSpace(role) || role.Length > 100)
                throw new CourtRuleException("Для джоббана нужен ID роли.");
        }
        else
        {
            role = null;
        }
    }
}
