using Content.IntegrationTests.Pair;
using Content.Server._CMU14.Acquaintance;
using Content.Server.Mind;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests._CMU14.Acquaintance;

[TestFixture]
public sealed class AcquaintanceTest
{
    [Test]
    public async Task IntroductionSeparatelyTeachesFaceAndVoice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = false,
            Dirty = true,
            DummyTicker = false,
        });

        var server = pair.Server;
        var entMan = server.EntMan;
        var acquaintance = entMan.System<AcquaintanceSystem>();
        var minds = entMan.System<MindSystem>();

        await server.WaitAssertion(() =>
        {
            var speaker = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);
            var listener = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

            minds.TransferTo(minds.CreateMind(null, "Speaker"), speaker);
            minds.TransferTo(minds.CreateMind(null, "Listener"), listener);

            var claimedName = Identity.Name(speaker, entMan, listener).Name;
            var unknownFace = acquaintance.GetPerceivedFaceName(listener, speaker);
            var unknownVoice = acquaintance.GetPerceivedVoiceName(listener, speaker, entMan.GetComponent<MetaDataComponent>(speaker).EntityName);

            Assert.That(unknownFace, Is.Not.EqualTo(claimedName));
            Assert.That(unknownVoice, Is.Not.EqualTo(claimedName));

            acquaintance.Introduce(speaker, listener);

            Assert.That(acquaintance.GetPerceivedFaceName(listener, speaker), Is.EqualTo(claimedName));
            Assert.That(
                acquaintance.GetPerceivedVoiceName(listener, speaker, entMan.GetComponent<MetaDataComponent>(speaker).EntityName),
                Is.EqualTo(claimedName));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoveredFaceAndChangedVoiceAreNotRecognized()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = false,
            Dirty = true,
            DummyTicker = false,
        });

        var server = pair.Server;
        var entMan = server.EntMan;
        var acquaintance = entMan.System<AcquaintanceSystem>();
        var minds = entMan.System<MindSystem>();

        await server.WaitAssertion(() =>
        {
            var speaker = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);
            var listener = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

            minds.TransferTo(minds.CreateMind(null, "Speaker"), speaker);
            minds.TransferTo(minds.CreateMind(null, "Listener"), listener);

            var claimedName = Identity.Name(speaker, entMan, listener).Name;
            var normalVoice = entMan.GetComponent<MetaDataComponent>(speaker).EntityName;
            acquaintance.Introduce(speaker, listener);

            var blocker = entMan.EnsureComponent<IdentityBlockerComponent>(speaker);
            blocker.Enabled = true;
            blocker.Coverage = IdentityBlockerCoverage.FULL;

            Assert.That(acquaintance.GetPerceivedFaceName(listener, speaker), Is.Not.EqualTo(claimedName));
            Assert.That(acquaintance.GetPerceivedVoiceName(listener, speaker, normalVoice), Is.EqualTo(claimedName));
            Assert.That(acquaintance.GetPerceivedVoiceName(listener, speaker, "Changed Voice"), Is.Not.EqualTo(claimedName));
        });

        await pair.CleanReturnAsync();
    }
}
