using Content.DiscordBot.Governance;
using NUnit.Framework;

namespace Content.DiscordBot.Tests;

[TestFixture]
public sealed class FullGovernancePolicyTests
{
    [TestCase("freeze", 1)]
    [TestCase("request_explanation", 1)]
    [TestCase("view_logs", 1)]
    [TestCase("round_remove", 2)]
    public void ModerationQuorumMatchesRisk(string action, short expected)
    {
        Assert.That(ModerationQuorum.RequiredApprovals(action), Is.EqualTo(expected));
    }

    [Test]
    public void EventManifestRequiresScopedEventCapabilityAndBoundedLimit()
    {
        var result = EventGovernanceService.ParseManifest("event.spawn:CMXenoDrone:3, event.weather:ash:1");
        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(new EventManifestRequest("event.spawn", "CMXenoDrone", 3)));
        Assert.Throws<CourtRuleException>(() => EventGovernanceService.ParseManifest("admin.spawn:anything:1"));
        Assert.Throws<CourtRuleException>(() => EventGovernanceService.ParseManifest("event.spawn:anything:0"));
    }

    [Test]
    public void CourtDefendantNicknameIsTrimmedAndBounded()
    {
        Assert.That(CommunityCourtService.NormalizeGameNickname("  MarinePlayer  "), Is.EqualTo("MarinePlayer"));
        Assert.Throws<CourtRuleException>(() => CommunityCourtService.NormalizeGameNickname("   "));
        Assert.Throws<CourtRuleException>(() => CommunityCourtService.NormalizeGameNickname(new string('x', 65)));
    }

    [TestCase(ModerationReviewOutcomes.Correct, 100, 100)]
    [TestCase(ModerationReviewOutcomes.ReasonableButWrong, 85, 100)]
    [TestCase(ModerationReviewOutcomes.ProceduralError, 60, 35)]
    [TestCase(ModerationReviewOutcomes.Negligent, 25, 20)]
    [TestCase(ModerationReviewOutcomes.Abuse, 0, 0)]
    public void ModerationReviewOutcomesSeparateAccuracyFromProcedure(
        string outcome,
        int accuracy,
        int procedure)
    {
        Assert.That(ModerationReviewOutcomes.IsValid(outcome), Is.True);
        Assert.That(ModerationReviewOutcomes.AccuracyWeight(outcome), Is.EqualTo(accuracy));
        Assert.That(ModerationReviewOutcomes.ProcedureWeight(outcome), Is.EqualTo(procedure));
    }

    [Test]
    public void ReasonableButWrongDoesNotCountAsProceduralFailure()
    {
        Assert.That(
            ModerationReviewOutcomes.ProcedureWeight(ModerationReviewOutcomes.ReasonableButWrong),
            Is.EqualTo(100));
        Assert.That(
            ModerationReviewOutcomes.AccuracyWeight(ModerationReviewOutcomes.ReasonableButWrong),
            Is.LessThan(100));
    }

    [Test]
    public void RoundRemovalIsAlwaysSelectedForAudit()
    {
        Assert.That(ModerationTrustService.ShouldAudit(99, "round_remove", 0), Is.True);
        Assert.That(ModerationTrustService.ShouldAudit(24, "freeze", 25), Is.True);
        Assert.That(ModerationTrustService.ShouldAudit(25, "freeze", 25), Is.False);
    }

    [TestCase(4, 0, 750, 75, 100, 10, 0, 1)]
    [TestCase(5, 1, 700, 75, 80, 10, 0, 2)]
    [TestCase(15, 0, 810, 90, 90, 45, 5, 3)]
    [TestCase(30, 0, 900, 95, 95, 80, 12, 4)]
    public void ModerationQualificationUsesServiceHistoryAndTrust(
        int completed,
        int failed,
        int trust,
        int procedure,
        int reliability,
        int confidence,
        int reviewedActions,
        short expected)
    {
        var profile = new ModerationTrustProfile(
            Guid.NewGuid(),
            trust,
            trust / 10,
            procedure,
            reliability,
            confidence,
            reviewedActions,
            reviewedActions,
            completed,
            failed,
            0);
        Assert.That(ModerationQualificationPolicy.EligibleLevel(profile), Is.EqualTo(expected));
    }
}
