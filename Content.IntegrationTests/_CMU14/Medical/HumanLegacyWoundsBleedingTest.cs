using System.Collections.Generic;
using Content.Shared._CMU14.Medical.Human.Components;
using Content.Shared._RMC14.Medical.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests._CMU14.Medical;

[TestFixture]
public sealed class HumanLegacyWoundsBleedingTest
{
    [Test]
    public async Task LegacyWoundedComponentDoesNotDriveBleedingForCmuHumans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

            Assert.That(entMan.HasComponent<HumanMedicalComponent>(human), Is.True);

            var wounded = entMan.EnsureComponent<WoundedComponent>(human);
            GetLegacyWounds(wounded).Add(new Wound(
                FixedPoint2.New(20),
                FixedPoint2.Zero,
                4f,
                null,
                WoundType.Brute,
                false));
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var bloodstream = entMan.GetComponent<BloodstreamComponent>(human);

            Assert.Multiple(() =>
            {
                Assert.That(bloodstream.BleedAmount, Is.Zero);
                Assert.That(entMan.HasComponent<WoundedComponent>(human), Is.False);
            });

            entMan.DeleteEntity(human);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LegacyBloodstreamBleedAmountDoesNotDriveBleedingForCmuHumans()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

            Assert.That(entMan.HasComponent<HumanMedicalComponent>(human), Is.True);

            var bloodstream = entMan.GetComponent<BloodstreamComponent>(human);
            var bloodstreamSystem = entMan.System<SharedBloodstreamSystem>();
            var applied = bloodstreamSystem.TryModifyBleedAmount((human, bloodstream), 4f);

            Assert.That(applied, Is.False);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var bloodstream = entMan.GetComponent<BloodstreamComponent>(human);

            Assert.That(bloodstream.BleedAmount, Is.Zero);

            entMan.DeleteEntity(human);
        });

        await pair.CleanReturnAsync();
    }

    private static List<Wound> GetLegacyWounds(WoundedComponent component)
    {
        var field = typeof(WoundedComponent).GetField(nameof(WoundedComponent.Wounds));
        Assert.That(field, Is.Not.Null);

        return (List<Wound>) field!.GetValue(component)!;
    }
}
