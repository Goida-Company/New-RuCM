using System;
using Content.Server._RuMC14.Governance;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests.Server._RuMC14.Governance;

[TestFixture]
public sealed class GovernancePolicyTest
{
    private readonly NetUserId _actor = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    private readonly NetUserId _target = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

    [Test]
    public void ValidObserverFreezeIsAllowed()
    {
        var denial = GovernancePolicy.ValidateFreeze(true, true, _actor, _target, 120, 120);
        Assert.That(denial, Is.EqualTo(GovernanceDenial.None));
    }

    [TestCase(0)]
    [TestCase(121)]
    public void DurationOutsideServerLimitIsDenied(int seconds)
    {
        var denial = GovernancePolicy.ValidateFreeze(true, true, _actor, _target, seconds, 120);
        Assert.That(denial, Is.EqualTo(GovernanceDenial.InvalidDuration));
    }

    [Test]
    public void NonObserverIsDenied()
    {
        var denial = GovernancePolicy.ValidateFreeze(true, false, _actor, _target, 30, 120);
        Assert.That(denial, Is.EqualTo(GovernanceDenial.NotObserver));
    }

    [Test]
    public void SelfTargetIsDenied()
    {
        var denial = GovernancePolicy.ValidateFreeze(true, true, _actor, _actor, 30, 120);
        Assert.That(denial, Is.EqualTo(GovernanceDenial.SelfTarget));
    }

    [Test]
    public void DisabledSystemIsDeniedBeforeOtherChecks()
    {
        var denial = GovernancePolicy.ValidateFreeze(false, false, _actor, _actor, 0, 120);
        Assert.That(denial, Is.EqualTo(GovernanceDenial.Disabled));
    }
}
