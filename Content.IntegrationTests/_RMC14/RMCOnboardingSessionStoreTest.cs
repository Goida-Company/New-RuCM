using Content.Server._RMC14.Onboarding;
using Robust.Shared.Network;

namespace Content.IntegrationTests._RMC14;

[TestFixture]
public sealed class RMCOnboardingSessionStoreTest
{
    [Test]
    public void RejectsDuplicateAndCapacityOverflow()
    {
        var store = new RMCOnboardingSessionStore<object>();
        var first = new NetUserId(Guid.NewGuid());
        var second = new NetUserId(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(store.TryAdd(first, new object(), 1), Is.EqualTo(RMCOnboardingStartResult.Added));
            Assert.That(store.TryAdd(first, new object(), 1), Is.EqualTo(RMCOnboardingStartResult.AlreadyActive));
            Assert.That(store.TryAdd(second, new object(), 1), Is.EqualTo(RMCOnboardingStartResult.CapacityReached));
            Assert.That(store.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void CleanupIsIdempotent()
    {
        var store = new RMCOnboardingSessionStore<object>();
        var user = new NetUserId(Guid.NewGuid());
        store.TryAdd(user, new object(), 1);

        Assert.Multiple(() =>
        {
            Assert.That(store.TryRemove(user, out _), Is.True);
            Assert.That(store.TryRemove(user, out _), Is.False);
            Assert.That(store.Count, Is.Zero);
        });
    }
}
