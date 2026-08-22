using Content.DiscordBot.Governance;
using NUnit.Framework;

namespace Content.DiscordBot.Tests;

[TestFixture]
public sealed class CourtDecisionPolicyTests
{
    [Test]
    public void GuiltDecisionRequiresThreshold()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CourtDecisionPolicy.ResolveGuilt(
                [CourtVerdicts.Guilty], 2, 3), Is.Null);
            Assert.That(CourtDecisionPolicy.ResolveGuilt(
                [CourtVerdicts.Guilty, CourtVerdicts.Guilty], 2, 3), Is.EqualTo(CourtVerdicts.Guilty));
        });
    }

    [Test]
    public void SplitFullJuryFallsBackToInsufficientEvidence()
    {
        var result = CourtDecisionPolicy.ResolveGuilt(
            [CourtVerdicts.Guilty, CourtVerdicts.NotGuilty, CourtVerdicts.InsufficientEvidence],
            2,
            3);

        Assert.That(result, Is.EqualTo(CourtVerdicts.InsufficientEvidence));
    }

    [Test]
    public void SingleSentencingVoteDoesNotResolve()
    {
        var result = CourtDecisionPolicy.ResolveSentence(
            [
                (CourtSanctions.Warning, (short?) null, null),
            ],
            2,
            3);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SentencingMajorityWins()
    {
        var result = CourtDecisionPolicy.ResolveSentence(
            [
                (CourtSanctions.GameBan, (short?) 3, null),
                (CourtSanctions.GameBan, (short?) 3, null),
            ],
            2,
            3);

        Assert.Multiple(() =>
        {
            Assert.That(result?.Type, Is.EqualTo(CourtSanctions.GameBan));
            Assert.That(result?.Days, Is.EqualTo(3));
            Assert.That(result?.Role, Is.Null);
        });
    }

    [Test]
    public void SplitSentenceUsesLeastSevereOption()
    {
        var result = CourtDecisionPolicy.ResolveSentence(
            [
                (CourtSanctions.GameBan, (short?) 7, null),
                (CourtSanctions.JobBan, (short?) 3, "CMO"),
                (CourtSanctions.Warning, null, null),
            ],
            2,
            3);

        Assert.Multiple(() =>
        {
            Assert.That(result?.Type, Is.EqualTo(CourtSanctions.Warning));
            Assert.That(result?.Days, Is.Null);
            Assert.That(result?.Role, Is.Null);
        });
    }

    [Test]
    public void InvalidPolicyIsRejected()
    {
        var config = new Config
        {
            CourtJurySize = 3,
            CourtDecisionThreshold = 4,
        };

        Assert.That(() => CourtPolicy.FromConfig(config), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
