using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Content.DiscordBot.Governance;

public sealed class GovernanceDbContext(DbContextOptions<GovernanceDbContext> options) : DbContext(options)
{
    public DbSet<GovernanceUser> Users => Set<GovernanceUser>();
    public DbSet<GovernanceIdentityLink> IdentityLinks => Set<GovernanceIdentityLink>();
    public DbSet<GovernanceServicePath> ServicePaths => Set<GovernanceServicePath>();
    public DbSet<GovernanceQualification> Qualifications => Set<GovernanceQualification>();
    public DbSet<GovernanceRatingEntry> RatingEntries => Set<GovernanceRatingEntry>();
    public DbSet<GovernanceReputationObservation> ReputationObservations => Set<GovernanceReputationObservation>();
    public DbSet<GovernanceReputationSnapshot> ReputationSnapshots => Set<GovernanceReputationSnapshot>();
    public DbSet<GovernanceGameActivitySnapshot> GameActivitySnapshots => Set<GovernanceGameActivitySnapshot>();
    public DbSet<GovernanceContributionEvent> ContributionEvents => Set<GovernanceContributionEvent>();
    public DbSet<GovernanceConflict> Conflicts => Set<GovernanceConflict>();
    public DbSet<GovernanceInvitation> Invitations => Set<GovernanceInvitation>();
    public DbSet<GovernanceCourtCase> CourtCases => Set<GovernanceCourtCase>();
    public DbSet<GovernanceCourtStatement> CourtStatements => Set<GovernanceCourtStatement>();
    public DbSet<GovernanceJuror> Jurors => Set<GovernanceJuror>();
    public DbSet<GovernanceGuiltVote> GuiltVotes => Set<GovernanceGuiltVote>();
    public DbSet<GovernanceSentencingVote> SentencingVotes => Set<GovernanceSentencingVote>();
    public DbSet<GovernanceAuditEvent> AuditEvents => Set<GovernanceAuditEvent>();
    public DbSet<GovernanceCourtParticipant> CourtParticipants => Set<GovernanceCourtParticipant>();
    public DbSet<GovernanceFriendship> Friendships => Set<GovernanceFriendship>();
    public DbSet<GovernanceServiceAssignment> ServiceAssignments => Set<GovernanceServiceAssignment>();
    public DbSet<GovernanceDutySession> DutySessions => Set<GovernanceDutySession>();
    public DbSet<GovernanceCapabilityGrant> CapabilityGrants => Set<GovernanceCapabilityGrant>();
    public DbSet<GovernancePunishmentExecution> PunishmentExecutions => Set<GovernancePunishmentExecution>();
    public DbSet<GovernanceLeadershipOverride> LeadershipOverrides => Set<GovernanceLeadershipOverride>();
    public DbSet<GovernanceAHelpTicket> AHelpTickets => Set<GovernanceAHelpTicket>();
    public DbSet<GovernanceLiveIncident> LiveIncidents => Set<GovernanceLiveIncident>();
    public DbSet<GovernanceModerationAction> ModerationActions => Set<GovernanceModerationAction>();
    public DbSet<GovernanceModerationApproval> ModerationApprovals => Set<GovernanceModerationApproval>();
    public DbSet<GovernanceModerationReview> ModerationReviews => Set<GovernanceModerationReview>();
    public DbSet<GovernanceModerationAppeal> ModerationAppeals => Set<GovernanceModerationAppeal>();
    public DbSet<GovernanceEventProposal> EventProposals => Set<GovernanceEventProposal>();
    public DbSet<GovernanceEventReview> EventReviews => Set<GovernanceEventReview>();
    public DbSet<GovernanceEventSession> EventSessions => Set<GovernanceEventSession>();
    public DbSet<GovernanceEventManifestItem> EventManifestItems => Set<GovernanceEventManifestItem>();
    public DbSet<GovernanceEventAction> EventActions => Set<GovernanceEventAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure<GovernanceUser>(modelBuilder, "users");
        Configure<GovernanceIdentityLink>(modelBuilder, "identity_links");
        Configure<GovernanceRatingEntry>(modelBuilder, "rating_entries");
        Configure<GovernanceReputationObservation>(modelBuilder, "reputation_observations");
        Configure<GovernanceContributionEvent>(modelBuilder, "contribution_events");
        Configure<GovernanceConflict>(modelBuilder, "conflicts");
        Configure<GovernanceInvitation>(modelBuilder, "invitations");
        Configure<GovernanceCourtCase>(modelBuilder, "court_cases");
        Configure<GovernanceCourtStatement>(modelBuilder, "court_statements");
        Configure<GovernanceGuiltVote>(modelBuilder, "guilt_votes");
        Configure<GovernanceSentencingVote>(modelBuilder, "sentencing_votes");
        Configure<GovernanceAuditEvent>(modelBuilder, "audit_events");
        Configure<GovernanceFriendship>(modelBuilder, "friendships");
        Configure<GovernanceServiceAssignment>(modelBuilder, "service_assignments");
        Configure<GovernanceDutySession>(modelBuilder, "duty_sessions");
        Configure<GovernanceCapabilityGrant>(modelBuilder, "capability_grants");
        Configure<GovernancePunishmentExecution>(modelBuilder, "punishment_executions");
        Configure<GovernanceLeadershipOverride>(modelBuilder, "leadership_overrides");
        Configure<GovernanceAHelpTicket>(modelBuilder, "ahelp_tickets");
        Configure<GovernanceLiveIncident>(modelBuilder, "live_incidents");
        Configure<GovernanceModerationAction>(modelBuilder, "moderation_actions");
        Configure<GovernanceModerationReview>(modelBuilder, "moderation_reviews");
        Configure<GovernanceModerationAppeal>(modelBuilder, "moderation_appeals");
        Configure<GovernanceEventProposal>(modelBuilder, "event_proposals");
        Configure<GovernanceEventReview>(modelBuilder, "event_reviews");
        Configure<GovernanceEventSession>(modelBuilder, "event_sessions");
        Configure<GovernanceEventManifestItem>(modelBuilder, "event_manifest_items");
        Configure<GovernanceEventAction>(modelBuilder, "event_actions");

        var participant = modelBuilder.Entity<GovernanceCourtParticipant>();
        participant.ToTable("court_participants", "governance");
        participant.HasKey(value => new { value.CaseId, value.UserId });
        SnakeCaseProperties(participant);

        var approval = modelBuilder.Entity<GovernanceModerationApproval>();
        approval.ToTable("moderation_approvals", "governance");
        approval.HasKey(value => new { value.ActionId, value.ApproverUserId });
        SnakeCaseProperties(approval);

        var qualification = modelBuilder.Entity<GovernanceQualification>();
        qualification.ToTable("qualifications", "governance");
        qualification.HasKey(value => new { value.UserId, value.Track });
        SnakeCaseProperties(qualification);

        var juror = modelBuilder.Entity<GovernanceJuror>();
        juror.ToTable("jurors", "governance");
        juror.HasKey(value => new { value.CaseId, value.UserId });
        SnakeCaseProperties(juror);

        var servicePath = modelBuilder.Entity<GovernanceServicePath>();
        servicePath.ToTable("service_paths", "governance");
        servicePath.HasKey(value => new { value.UserId, value.Slot });
        servicePath.HasIndex(value => new { value.UserId, value.Track }).IsUnique();
        SnakeCaseProperties(servicePath);

        var reputationSnapshot = modelBuilder.Entity<GovernanceReputationSnapshot>();
        reputationSnapshot.ToTable("reputation_snapshots", "governance");
        reputationSnapshot.HasKey(value => new { value.UserId, value.Track });
        SnakeCaseProperties(reputationSnapshot);

        var activitySnapshot = modelBuilder.Entity<GovernanceGameActivitySnapshot>();
        activitySnapshot.ToTable("game_activity_snapshots", "governance");
        activitySnapshot.HasKey(value => value.UserId);
        SnakeCaseProperties(activitySnapshot);

        modelBuilder.Entity<GovernanceUser>().HasIndex(value => value.Ss14UserId).IsUnique();
        modelBuilder.Entity<GovernanceUser>().HasIndex(value => value.DiscordUserId).IsUnique().HasFilter("discord_user_id IS NOT NULL");
        modelBuilder.Entity<GovernanceIdentityLink>().HasIndex(value => value.DiscordUserId);
        modelBuilder.Entity<GovernanceInvitation>().HasIndex(value => value.IdempotencyKey).IsUnique();
        modelBuilder.Entity<GovernanceRatingEntry>().HasIndex(value => value.IdempotencyKey).IsUnique();
        modelBuilder.Entity<GovernanceReputationObservation>().HasIndex(value => value.IdempotencyKey).IsUnique();
        modelBuilder.Entity<GovernanceContributionEvent>().HasIndex(value => value.IdempotencyKey).IsUnique();
        modelBuilder.Entity<GovernanceGuiltVote>().HasIndex(value => new { value.CaseId, value.JurorUserId }).IsUnique();
        modelBuilder.Entity<GovernanceSentencingVote>().HasIndex(value => new { value.CaseId, value.JurorUserId }).IsUnique();
        modelBuilder.Entity<GovernanceFriendship>().HasIndex(value => new { value.UserId, value.FriendUserId }).IsUnique();
        modelBuilder.Entity<GovernancePunishmentExecution>().HasIndex(value => value.CaseId).IsUnique();
        modelBuilder.Entity<GovernanceModerationAction>().HasIndex(value => value.IdempotencyKey).IsUnique();
        modelBuilder.Entity<GovernanceModerationReview>().HasIndex(value => value.IdempotencyKey).IsUnique();
        modelBuilder.Entity<GovernanceModerationReview>().HasIndex(value => new { value.ActionId, value.ReviewerUserId }).IsUnique();
        modelBuilder.Entity<GovernanceModerationAppeal>().HasIndex(value => value.ActionId).IsUnique();
        modelBuilder.Entity<GovernanceDutySession>().HasIndex(value => value.IdempotencyKey).IsUnique();
        modelBuilder.Entity<GovernanceCapabilityGrant>().HasIndex(value => value.IdempotencyKey).IsUnique();
        modelBuilder.Entity<GovernanceEventReview>().HasIndex(value => new { value.ProposalId, value.ReviewerUserId }).IsUnique();
        modelBuilder.Entity<GovernanceCourtCase>().Property(value => value.Version).IsConcurrencyToken();
        modelBuilder.Entity<GovernanceInvitation>().Property(value => value.Version).IsConcurrencyToken();
        modelBuilder.Entity<GovernanceRatingEntry>().Property(value => value.Metadata).HasColumnType("jsonb");
        modelBuilder.Entity<GovernanceIdentityLink>().Property(value => value.Metadata).HasColumnType("jsonb");
        modelBuilder.Entity<GovernanceReputationObservation>().Property(value => value.Metadata).HasColumnType("jsonb");
        modelBuilder.Entity<GovernanceContributionEvent>().Property(value => value.Metadata).HasColumnType("jsonb");
        modelBuilder.Entity<GovernanceAuditEvent>().Property(value => value.Payload).HasColumnType("jsonb");
        modelBuilder.Entity<GovernanceEventAction>().Property(value => value.Payload).HasColumnType("jsonb");
        modelBuilder.Entity<GovernanceEventProposal>().Property(value => value.Manifest).HasColumnType("jsonb");
        modelBuilder.Entity<GovernanceCapabilityGrant>().Property(value => value.Scope).HasColumnType("jsonb");
    }

    private static void Configure<TEntity>(ModelBuilder modelBuilder, string table)
        where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.ToTable(table, "governance");
        entity.HasKey("Id");
        SnakeCaseProperties(entity);
    }

    private static void SnakeCaseProperties<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        foreach (var property in entity.Metadata.GetProperties())
            property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string value)
    {
        return string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));
    }
}

public sealed class GovernanceDesignTimeContextFactory : IDesignTimeDbContextFactory<GovernanceDbContext>
{
    public GovernanceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GovernanceDbContext>()
            .UseNpgsql("Host=localhost;Database=ss14;Username=postgres")
            .Options;
        return new GovernanceDbContext(options);
    }
}
