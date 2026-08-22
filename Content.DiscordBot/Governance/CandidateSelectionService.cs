using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record CandidateEligibilityDiagnostic(
    Guid UserId,
    string Track,
    short QualificationLevel,
    short RequiredQualification,
    bool PathSelected,
    bool PathRequirementBypassed,
    bool Suspended,
    bool DiscordRequired,
    bool DiscordLinked,
    bool HasActiveBan,
    bool Eligible);

public sealed record CandidateSimulationEntry(
    Guid UserId,
    Guid Ss14UserId,
    long? DiscordUserId,
    short QualificationLevel,
    int TrackScore,
    double TrackLowerBound,
    double TrackEvidenceWeight,
    int GeneralScore,
    int Wins);

public sealed record CandidateSimulationResult(
    string Track,
    short MinimumQualification,
    int Iterations,
    int Seed,
    int PoolSize,
    IReadOnlyList<CandidateSimulationEntry> Entries);

public sealed record QualificationProgressDiagnostic(
    string Track,
    short CurrentLevel,
    short EligibleLevel,
    int Score,
    double LowerBound,
    double EvidenceWeight,
    int CompletedAssignments,
    short? NextLevel,
    double? RequiredLowerBound,
    double? RequiredEvidenceWeight,
    int? RequiredCompletedAssignments);

public static class CandidateSelectionPolicy
{
    public static double SamplePriority(
        double alpha,
        double beta,
        int generalScore,
        short qualificationLevel,
        Random? random = null)
    {
        if (alpha <= 0 || beta <= 0)
            throw new ArgumentOutOfRangeException(nameof(alpha), "Параметры Beta-распределения должны быть положительными.");

        var thompson = ReputationMath.SampleBeta(alpha, beta, random);
        var normalizedGeneral = Math.Clamp(generalScore, 0, 1000) / 1000.0;
        var generalFactor = 0.85 + 0.30 * normalizedGeneral;
        var qualificationFactor = 1.0 + 0.03 * Math.Max(0, qualificationLevel - 1);
        return thompson * generalFactor * qualificationFactor;
    }
}

public sealed class CandidateSelectionService(
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory,
    ReputationService? reputation = null,
    Config? config = null)
{
    public async Task<CandidateEligibilityDiagnostic> DiagnoseBaseEligibilityAsync(
        Guid userId,
        string track,
        short minimumQualification)
    {
        if (!ReputationTracks.IsPath(track))
            throw new CourtRuleException("Неизвестное направление отбора.");
        if (minimumQualification is < 1 or > 4)
            throw new CourtRuleException("Минимальная квалификация должна быть от I до IV.");

        await using var governance = governanceFactory();
        var user = await governance.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == userId)
            ?? throw new CourtRuleException("Профиль Governance не найден.");

        var effectiveMinimumQualification = config?.CourtTestMode == true && track == ReputationTracks.Event
            ? Math.Max(minimumQualification, (short) 4)
            : minimumQualification;
        var qualification = await governance.Qualifications.AsNoTracking()
            .Where(value => value.UserId == userId && value.Track == track)
            .Select(value => (short?) value.Level)
            .SingleOrDefaultAsync() ?? 0;
        var pathSelected = await governance.ServicePaths.AsNoTracking()
            .AnyAsync(value => value.UserId == userId && value.Track == track);
        var pathRequirementBypassed = config?.CourtTestMode == true && track is ReputationTracks.Jury or ReputationTracks.Event;
        var discordRequired = track is ReputationTracks.Jury or ReputationTracks.Event or ReputationTracks.Moderation;
        var discordLinked = user.DiscordUserId is > 0;

        await using var game = gameFactory();
        var now = DateTime.UtcNow;
        var hasGameBan = await game.Ban.AsNoTracking().AnyAsync(value =>
            value.PlayerUserId == user.Ss14UserId && !value.Hidden && value.Unban == null &&
            (value.ExpirationTime == null || value.ExpirationTime > now));
        var hasRoleBan = await game.RoleBan.AsNoTracking().AnyAsync(value =>
            value.PlayerUserId == user.Ss14UserId && !value.Hidden && value.Unban == null &&
            (value.ExpirationTime == null || value.ExpirationTime > now));
        var hasActiveBan = hasGameBan || hasRoleBan;

        var eligible = !user.IsGovernanceSuspended &&
                       qualification >= effectiveMinimumQualification &&
                       (pathRequirementBypassed || pathSelected) &&
                       (!discordRequired || discordLinked) &&
                       !hasActiveBan;

        return new CandidateEligibilityDiagnostic(
            user.Id,
            track,
            qualification,
            effectiveMinimumQualification,
            pathSelected,
            pathRequirementBypassed,
            user.IsGovernanceSuspended,
            discordRequired,
            discordLinked,
            hasActiveBan,
            eligible);
    }

    public async Task<IReadOnlyList<QualificationProgressDiagnostic>> QualificationProgressAsync(Guid userId)
    {
        if (reputation != null)
            await reputation.RefreshUserAsync(userId);

        await using var governance = governanceFactory();
        if (!await governance.Users.AsNoTracking().AnyAsync(value => value.Id == userId))
            throw new CourtRuleException("Профиль Governance не найден.");

        var paths = await governance.ServicePaths.AsNoTracking()
            .Where(value => value.UserId == userId)
            .OrderBy(value => value.Slot)
            .ToListAsync();
        if (paths.Count == 0)
            return [];

        var tracks = paths.Select(value => value.Track).ToArray();
        var qualifications = await governance.Qualifications.AsNoTracking()
            .Where(value => value.UserId == userId && tracks.Contains(value.Track))
            .ToDictionaryAsync(value => value.Track, value => value.Level);
        var snapshots = await governance.ReputationSnapshots.AsNoTracking()
            .Where(value => value.UserId == userId && tracks.Contains(value.Track))
            .ToDictionaryAsync(value => value.Track);

        var result = new List<QualificationProgressDiagnostic>(paths.Count);
        foreach (var path in paths)
        {
            snapshots.TryGetValue(path.Track, out var snapshot);
            var posterior = snapshot == null
                ? ReputationMath.Posterior(path.Track, [], DateTime.UtcNow)
                : new ReputationPosterior(
                    snapshot.Track,
                    snapshot.Alpha,
                    snapshot.Beta,
                    snapshot.Mean,
                    snapshot.LowerBound,
                    snapshot.EvidenceWeight,
                    snapshot.Score);
            var current = qualifications.GetValueOrDefault(path.Track, (short) 1);
            var completed = await CountCompletedAssignmentsAsync(governance, userId, path.Track);
            var eligible = ReputationPolicy.EligibleQualificationLevel(path.Track, posterior, completed);
            var nextLevel = current >= 4 ? null : (short?) (current + 1);
            (double? Lower, double? Evidence, int? Completed) requirements = nextLevel switch
            {
                2 => (0.65, 4.0, 4),
                3 => (0.75, 10.0, 10),
                4 => (0.85, 20.0, 20),
                _ => (null, null, null),
            };

            result.Add(new QualificationProgressDiagnostic(
                path.Track,
                current,
                eligible,
                posterior.Score,
                posterior.LowerBound,
                posterior.EvidenceWeight,
                completed,
                nextLevel,
                requirements.Lower,
                requirements.Evidence,
                requirements.Completed));
        }

        return result;
    }

    public async Task<CandidateSimulationResult> SimulateAsync(
        string track,
        short minimumQualification,
        int iterations,
        IReadOnlySet<ulong>? availableDiscordIds,
        TimeSpan cooldown)
    {
        if (!ReputationTracks.IsPath(track))
            throw new CourtRuleException("Неизвестное направление отбора.");
        if (minimumQualification is < 1 or > 4)
            throw new CourtRuleException("Минимальная квалификация должна быть от I до IV.");
        if (iterations is < 50 or > 5000)
            throw new CourtRuleException("Для симуляции укажите от 50 до 5000 итераций.");
        if (cooldown < TimeSpan.Zero || cooldown > TimeSpan.FromDays(30))
            throw new CourtRuleException("Cooldown симуляции должен быть от 0 до 30 дней.");

        var pool = await SelectAsync(
            track,
            minimumQualification,
            "selection_simulation",
            Guid.NewGuid().ToString("N"),
            int.MaxValue,
            [],
            availableDiscordIds,
            cooldown);

        var seed = Random.Shared.Next();
        if (pool.Count == 0)
            return new CandidateSimulationResult(track, minimumQualification, iterations, seed, 0, []);

        await using var governance = governanceFactory();
        var ids = pool.Select(value => value.Id).ToArray();
        var qualificationLevels = await governance.Qualifications.AsNoTracking()
            .Where(value => ids.Contains(value.UserId) && value.Track == track)
            .ToDictionaryAsync(value => value.UserId, value => value.Level);
        var snapshots = await governance.ReputationSnapshots.AsNoTracking()
            .Where(value => ids.Contains(value.UserId) &&
                            (value.Track == track || value.Track == ReputationTracks.General))
            .ToListAsync();
        var byUser = snapshots.GroupBy(value => value.UserId)
            .ToDictionary(group => group.Key, group => group.ToDictionary(value => value.Track, StringComparer.Ordinal));

        var wins = pool.ToDictionary(value => value.Id, _ => 0);
        var random = new Random(seed);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            GovernanceUser? winner = null;
            var bestPriority = double.NegativeInfinity;
            foreach (var user in pool)
            {
                byUser.TryGetValue(user.Id, out var userSnapshots);
                GovernanceReputationSnapshot? trackSnapshot = null;
                GovernanceReputationSnapshot? generalSnapshot = null;
                if (userSnapshots != null)
                {
                    userSnapshots.TryGetValue(track, out trackSnapshot);
                    userSnapshots.TryGetValue(ReputationTracks.General, out generalSnapshot);
                }

                var priority = CandidateSelectionPolicy.SamplePriority(
                    trackSnapshot?.Alpha ?? ReputationPolicy.TrackPriorStrength * 0.5,
                    trackSnapshot?.Beta ?? ReputationPolicy.TrackPriorStrength * 0.5,
                    generalSnapshot?.Score ?? ReputationPolicy.NeutralScore,
                    qualificationLevels.GetValueOrDefault(user.Id, (short) 1),
                    random);
                if (priority <= bestPriority)
                    continue;
                bestPriority = priority;
                winner = user;
            }

            if (winner != null)
                wins[winner.Id]++;
        }

        var entries = pool.Select(user =>
        {
            byUser.TryGetValue(user.Id, out var userSnapshots);
            GovernanceReputationSnapshot? trackSnapshot = null;
            GovernanceReputationSnapshot? generalSnapshot = null;
            if (userSnapshots != null)
            {
                userSnapshots.TryGetValue(track, out trackSnapshot);
                userSnapshots.TryGetValue(ReputationTracks.General, out generalSnapshot);
            }

            return new CandidateSimulationEntry(
                user.Id,
                user.Ss14UserId,
                user.DiscordUserId,
                qualificationLevels.GetValueOrDefault(user.Id, (short) 1),
                trackSnapshot?.Score ?? ReputationPolicy.NeutralScore,
                trackSnapshot?.LowerBound ?? 0,
                trackSnapshot?.EvidenceWeight ?? 0,
                generalSnapshot?.Score ?? ReputationPolicy.NeutralScore,
                wins[user.Id]);
        })
        .OrderByDescending(value => value.Wins)
        .ThenByDescending(value => value.TrackScore)
        .ToArray();

        return new CandidateSimulationResult(track, minimumQualification, iterations, seed, pool.Count, entries);
    }

    public async Task<IReadOnlyList<GovernanceUser>> SelectAsync(
        string track,
        short minimumQualification,
        string entityType,
        string entityId,
        int count,
        IReadOnlyCollection<Guid> excludedUsers,
        IReadOnlySet<ulong>? availableDiscordIds,
        TimeSpan cooldown,
        bool aboveAverage = true)
    {
        if (count <= 0)
            return [];

        await using var governance = governanceFactory();
        var now = DateTime.UtcNow;
        var effectiveMinimumQualification = config?.CourtTestMode == true && track == ReputationTracks.Event
            ? Math.Max(minimumQualification, (short) 4)
            : minimumQualification;

        var qualified = governance.Users.AsNoTracking()
            .Join(governance.Qualifications.AsNoTracking()
                    .Where(value => value.Track == track && value.Level >= effectiveMinimumQualification),
                user => user.Id,
                qualification => qualification.UserId,
                (user, qualification) => new { User = user, qualification.Level })
            .Where(row => !row.User.IsGovernanceSuspended && !excludedUsers.Contains(row.User.Id));

        var bypassPathForLocalTest = config?.CourtTestMode == true && track is ReputationTracks.Jury or ReputationTracks.Event;
        if (!bypassPathForLocalTest)
        {
            qualified = qualified.Join(governance.ServicePaths.AsNoTracking().Where(value => value.Track == track),
                row => row.User.Id,
                path => path.UserId,
                (row, _) => row);
        }

        if (track is ReputationTracks.Jury or ReputationTracks.Event or ReputationTracks.Moderation)
            qualified = qualified.Where(row => row.User.DiscordUserId != null && row.User.DiscordUserId > 0);

        var qualifiedRows = await qualified.ToListAsync();
        var candidates = qualifiedRows.Select(value => value.User).ToList();
        var qualificationLevels = qualifiedRows
            .GroupBy(value => value.User.Id)
            .ToDictionary(group => group.Key, group => group.Max(value => value.Level));

        if (availableDiscordIds != null)
        {
            candidates = candidates
                .Where(user => user.DiscordUserId is > 0 && availableDiscordIds.Contains(checked((ulong) user.DiscordUserId.Value)))
                .ToList();
        }

        if (candidates.Count == 0)
            return [];

        var candidateIds = candidates.Select(value => value.Id).ToArray();
        var conflicts = await governance.Conflicts.AsNoTracking()
            .Where(value => value.EndsAt == null || value.EndsAt > now)
            .Where(value => candidateIds.Contains(value.UserId))
            .Where(value =>
                value.EntityType == entityType && value.EntityId == entityId ||
                value.RelatedUserId != null && excludedUsers.Contains(value.RelatedUserId.Value))
            .Select(value => value.UserId)
            .ToListAsync();

        var friendships = await governance.Friendships.AsNoTracking()
            .Where(value => value.ConfirmedAt != null)
            .Where(value => candidateIds.Contains(value.UserId) && excludedUsers.Contains(value.FriendUserId) ||
                            candidateIds.Contains(value.FriendUserId) && excludedUsers.Contains(value.UserId))
            .Select(value => candidateIds.Contains(value.UserId) ? value.UserId : value.FriendUserId)
            .ToListAsync();

        var recent = await governance.ServiceAssignments.AsNoTracking()
            .Where(value => candidateIds.Contains(value.UserId) && value.Track == track && value.AssignedAt > now - cooldown)
            .Select(value => value.UserId)
            .ToListAsync();
        var pending = await governance.Invitations.AsNoTracking()
            .Where(value => candidateIds.Contains(value.UserId) && value.State == InvitationStates.Pending)
            .Select(value => value.UserId)
            .ToListAsync();
        var activeDuty = await governance.Database.SqlQuery<Guid>($"""
                SELECT user_id AS "Value" FROM governance.duty_sessions
                WHERE status = 'active' AND expires_at > now()
                UNION
                SELECT director_user_id AS "Value" FROM governance.event_sessions
                WHERE status = 'active' AND expires_at > now()
                """).ToListAsync();

        var unavailable = conflicts.Concat(friendships).Concat(recent).Concat(pending).Concat(activeDuty).ToHashSet();
        candidates = candidates.Where(value => !unavailable.Contains(value.Id)).ToList();
        if (candidates.Count == 0)
            return [];

        var playerIds = candidates.Select(value => value.Ss14UserId).ToArray();
        await using var game = gameFactory();
        var banned = await game.Ban.AsNoTracking()
            .Where(value => value.PlayerUserId != null && playerIds.Contains(value.PlayerUserId.Value))
            .Where(value => !value.Hidden && value.Unban == null && (value.ExpirationTime == null || value.ExpirationTime > now))
            .Select(value => value.PlayerUserId!.Value)
            .Concat(game.RoleBan.AsNoTracking()
                .Where(value => value.PlayerUserId != null && playerIds.Contains(value.PlayerUserId.Value))
                .Where(value => !value.Hidden && value.Unban == null && (value.ExpirationTime == null || value.ExpirationTime > now))
                .Select(value => value.PlayerUserId!.Value))
            .Distinct()
            .ToListAsync();
        candidates = candidates.Where(value => !banned.Contains(value.Ss14UserId)).ToList();
        if (candidates.Count == 0)
            return [];

        if (reputation == null)
            return candidates.OrderBy(_ => Guid.NewGuid()).Take(count).ToArray();

        await reputation.RefreshUsersAsync(candidates.Select(value => value.Id));
        var remainingIds = candidates.Select(value => value.Id).ToArray();
        var snapshots = await governance.ReputationSnapshots.AsNoTracking()
            .Where(value => remainingIds.Contains(value.UserId) &&
                            (value.Track == track || value.Track == ReputationTracks.General))
            .ToListAsync();

        var byUser = snapshots.GroupBy(value => value.UserId)
            .ToDictionary(group => group.Key, group => group.ToDictionary(value => value.Track, StringComparer.Ordinal));

        return candidates
            .Select(user =>
            {
                byUser.TryGetValue(user.Id, out var userSnapshots);
                GovernanceReputationSnapshot? trackSnapshot = null;
                GovernanceReputationSnapshot? generalSnapshot = null;
                if (userSnapshots != null)
                {
                    userSnapshots.TryGetValue(track, out trackSnapshot);
                    userSnapshots.TryGetValue(ReputationTracks.General, out generalSnapshot);
                }

                var alpha = trackSnapshot?.Alpha ?? ReputationPolicy.TrackPriorStrength * 0.5;
                var beta = trackSnapshot?.Beta ?? ReputationPolicy.TrackPriorStrength * 0.5;
                var priority = CandidateSelectionPolicy.SamplePriority(
                    alpha,
                    beta,
                    generalSnapshot?.Score ?? ReputationPolicy.NeutralScore,
                    qualificationLevels.GetValueOrDefault(user.Id, (short) 1));
                return (User: user, Priority: priority);
            })
            .OrderByDescending(value => value.Priority)
            .Take(count)
            .Select(value => value.User)
            .ToArray();
    }

    private static async Task<int> CountCompletedAssignmentsAsync(
        GovernanceDbContext governance,
        Guid userId,
        string track)
    {
        var completed = await governance.ServiceAssignments.AsNoTracking().CountAsync(value =>
            value.UserId == userId && value.Track == track && value.CompletedAt != null);
        if (track == ReputationTracks.Moderation)
        {
            completed += await governance.AHelpTickets.AsNoTracking().CountAsync(value =>
                value.ClaimedByUserId == userId && value.Status == "resolved");
            completed += await governance.DutySessions.AsNoTracking().CountAsync(value =>
                value.UserId == userId && (value.Status == "completed" || value.Status == "round_ended"));
        }
        if (track == ReputationTracks.Contributor)
            completed += await governance.ContributionEvents.AsNoTracking().CountAsync(value => value.UserId == userId);
        return completed;
    }
}
