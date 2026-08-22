using System.Data;
using System.Text.Json;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed class CourtFilingService(
    GovernanceIdentityService identities,
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory,
    CourtPolicy policy)
{
    public async Task<GovernanceCourtCase> FileByGameNicknameAsync(
        ulong claimantDiscordId,
        string defendantGameNickname,
        int roundId,
        string summary,
        string evidenceReference)
    {
        defendantGameNickname = CommunityCourtService.NormalizeGameNickname(defendantGameNickname);
        summary = summary.Trim();
        evidenceReference = evidenceReference.Trim();
        if (summary.Length is < 20 or > 1500)
            throw new CourtRuleException("Описание жалобы должно содержать от 20 до 1500 символов.");
        if (string.IsNullOrWhiteSpace(evidenceReference))
            throw new CourtRuleException("Нужно приложить клип, файл или ссылку на реплей.");

        var claimant = await identities.RequireDiscordUserAsync(claimantDiscordId);
        var defendant = await identities.RequireSs14UserByNicknameAsync(defendantGameNickname);
        if (claimant.Id == defendant.Id)
            throw new CourtRuleException("Нельзя подать жалобу на самого себя.");

        await ValidateRoundAsync(roundId, claimant.Ss14UserId, defendant.Ss14UserId);
        var now = DateTime.UtcNow;
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var courtCase = governance.CourtCases.Add(new GovernanceCourtCase
        {
            ClaimantUserId = claimant.Id,
            DefendantUserId = defendant.Id,
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
            AuthorUserId = claimant.Id,
            Kind = "complaint",
            Body = summary,
            EvidenceReference = evidenceReference,
            CreatedAt = now,
        });
        governance.CourtParticipants.AddRange(
            new GovernanceCourtParticipant
            {
                CaseId = courtCase.Id,
                UserId = claimant.Id,
                Role = "claimant",
                AddedAt = now,
            },
            new GovernanceCourtParticipant
            {
                CaseId = courtCase.Id,
                UserId = defendant.Id,
                Role = "defendant",
                AddedAt = now,
            });
        governance.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = "court.case_filed",
            ActorType = "discord_user",
            ActorId = claimantDiscordId.ToString(),
            TargetType = "ss14_user",
            TargetId = defendant.Ss14UserId.ToString(),
            EntityType = "court_case",
            EntityId = courtCase.Id.ToString(),
            CreatedAt = now,
            Payload = JsonSerializer.Serialize(new
            {
                round_id = roundId,
                defendant_user_id = defendant.Id,
                defendant_discord_linked = defendant.DiscordUserId is > 0,
            }),
        });
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        return courtCase;
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
}
