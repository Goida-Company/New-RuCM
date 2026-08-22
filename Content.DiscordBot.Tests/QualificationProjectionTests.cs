using Content.DiscordBot.Governance;
using NUnit.Framework;

namespace Content.DiscordBot.Tests;

[TestFixture]
public sealed class QualificationProjectionTests
{
    [Test]
    public void ProjectionShowsCredibleBoundCanDominateRawEvidenceThreshold()
    {
        var additional = QualificationProjection.AdditionalPositiveEvidenceForLowerBound(
            score: 621,
            evidenceWeight: 1.92,
            targetLowerBound: 0.65);

        Assert.That(additional, Is.GreaterThan(6.0));
        Assert.That(additional, Is.LessThan(7.0));
    }

    [Test]
    public void ProjectionIsZeroWhenTargetAlreadyReached()
    {
        var additional = QualificationProjection.AdditionalPositiveEvidenceForLowerBound(
            score: 900,
            evidenceWeight: 20,
            targetLowerBound: 0.65);

        Assert.That(additional, Is.Zero.Within(1e-9));
    }
}
