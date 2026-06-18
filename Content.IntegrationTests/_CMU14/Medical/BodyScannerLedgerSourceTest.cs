using System.Collections.Generic;
using System.Reflection;
using Content.Server._CMU14.Medical.Machines;
using Content.Server.Medical;
using Content.Server.Medical.SuitSensors;
using Content.Shared._CMU14.Medical.Human.Data;
using Content.Shared._CMU14.Medical.Human.Components;
using Content.Shared._CMU14.Medical.Human.Systems;
using Content.Shared._CMU14.Medical.Machines;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.SuitSensor;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests._CMU14.Medical;

[TestFixture]
public sealed class BodyScannerLedgerSourceTest
{
    [Test]
    public async Task HumanBodyScannerIgnoresLegacyDamageCache()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

            Assert.That(entMan.HasComponent<HumanMedicalComponent>(human), Is.True);

            SetLegacyDamageCache(entMan, human, FixedPoint2.New(150));
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var scanner = entMan.System<CMUBodyScannerSystem>();
            var lines = BuildScanLines(scanner, human);

            Assert.Multiple(() =>
            {
                Assert.That(lines, Has.None.Matches<CMUBodyScannerScanLine>(
                    line => line.Text.Contains("Damage:")));
                Assert.That(lines, Has.None.Matches<CMUBodyScannerScanLine>(
                    line => line.Text.Contains("brute 150")));
                Assert.That(lines, Has.Some.Matches<CMUBodyScannerScanLine>(
                    line => line.Category == CMUBodyScannerScanCategory.Vitals &&
                        line.Text.Contains("Ledger:")));
            });

            entMan.DeleteEntity(human);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HumanHealthAnalyzerMessageIgnoresLegacyDamageCache()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

            Assert.That(entMan.HasComponent<HumanMedicalComponent>(human), Is.True);

            SetLegacyDamageCache(entMan, human, FixedPoint2.New(150));
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var analyzer = entMan.System<HealthAnalyzerSystem>();
            var message = analyzer.BuildScannedUserMessage(human, human, true);

            Assert.Multiple(() =>
            {
                Assert.That(message.Damage, Is.Not.Null);
                Assert.That(message.Damage!.Total, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(message.Damage.DamagePerGroup.GetValueOrDefault("Brute"), Is.EqualTo(FixedPoint2.Zero));
            });

            entMan.DeleteEntity(human);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HumanSuitSensorIgnoresLegacyDamageCache()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid human = default;
        EntityUid sensorUid = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            human = entMan.SpawnEntity("CMMobHuman", map.GridCoords);
            sensorUid = entMan.SpawnEntity(null, map.GridCoords);

            Assert.That(entMan.HasComponent<HumanMedicalComponent>(human), Is.True);

            var sensor = entMan.EnsureComponent<SuitSensorComponent>(sensorUid);
            SetSuitSensorVitalsTarget(sensor, human);

            SetLegacyDamageCache(entMan, human, FixedPoint2.New(150));
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var suitSensors = entMan.System<SuitSensorSystem>();
            var status = suitSensors.GetSensorState(sensorUid);

            Assert.Multiple(() =>
            {
                Assert.That(status, Is.Not.Null);
                Assert.That(status!.TotalDamage, Is.EqualTo(0));
            });

            entMan.DeleteEntity(sensorUid);
            entMan.DeleteEntity(human);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HumanLedgerProjectsTraumaIntoLegacyDamageCache()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

            Assert.That(entMan.HasComponent<HumanMedicalComponent>(human), Is.True);

            var damageable = entMan.System<DamageableSystem>();
            var toxin = new DamageSpecifier();
            toxin.DamageDict["Poison"] = FixedPoint2.New(5);
            damageable.TryChangeDamage(
                human,
                toxin,
                ignoreResistances: true,
                interruptsDoAfters: false);

            var medical = entMan.GetComponent<HumanMedicalComponent>(human);
            var transaction = new MedicalTransaction(BodyRegion.LeftArm);
            transaction.Add(MedicalEffect.AddRegionDamage(
                BodyRegion.LeftArm,
                FixedPoint2.New(12),
                FixedPoint2.New(7)));

            var result = entMan.System<SharedHumanMedicalSystem>()
                .ApplyTransaction((human, medical), transaction);

            Assert.That(result.Applied, Is.True);
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var damageable = entMan.GetComponent<DamageableComponent>(human);

            Assert.Multiple(() =>
            {
                Assert.That(damageable.TotalDamage, Is.EqualTo(FixedPoint2.New(24)));
                Assert.That(damageable.DamagePerGroup.GetValueOrDefault("Brute"), Is.EqualTo(FixedPoint2.New(12)));
                Assert.That(damageable.DamagePerGroup.GetValueOrDefault("Burn"), Is.EqualTo(FixedPoint2.New(7)));
                Assert.That(damageable.DamagePerGroup.GetValueOrDefault("Toxin"), Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(damageable.Damage.DamageDict.GetValueOrDefault("Blunt"), Is.EqualTo(FixedPoint2.New(12)));
                Assert.That(damageable.Damage.DamageDict.GetValueOrDefault("Heat"), Is.EqualTo(FixedPoint2.New(7)));
                Assert.That(damageable.Damage.DamageDict.GetValueOrDefault("Poison"), Is.EqualTo(FixedPoint2.New(5)));
            });

            entMan.DeleteEntity(human);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HumanLedgerRejectsLegacyDamageableTraumaHealing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

            var medical = entMan.GetComponent<HumanMedicalComponent>(human);
            var transaction = new MedicalTransaction(BodyRegion.Chest);
            transaction.Add(MedicalEffect.AddRegionDamage(
                BodyRegion.Chest,
                FixedPoint2.New(50),
                FixedPoint2.New(25)));

            var result = entMan.System<SharedHumanMedicalSystem>()
                .ApplyTransaction((human, medical), transaction);

            Assert.That(result.Applied, Is.True);

            var legacyHeal = new DamageSpecifier();
            legacyHeal.DamageDict["Blunt"] = FixedPoint2.New(-50);
            legacyHeal.DamageDict["Heat"] = FixedPoint2.New(-25);

            entMan.System<DamageableSystem>().TryChangeDamage(
                human,
                legacyHeal,
                ignoreResistances: true,
                interruptsDoAfters: false);
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var damageable = entMan.GetComponent<DamageableComponent>(human);
            var medical = entMan.GetComponent<HumanMedicalComponent>(human);
            var chest = HumanMedicalLedger.GetRegion(medical, BodyRegion.Chest);

            Assert.Multiple(() =>
            {
                Assert.That(chest.BruteDamage, Is.EqualTo(FixedPoint2.New(50)));
                Assert.That(chest.BurnDamage, Is.EqualTo(FixedPoint2.New(25)));
                Assert.That(damageable.Damage.DamageDict.GetValueOrDefault("Blunt"), Is.EqualTo(FixedPoint2.New(50)));
                Assert.That(damageable.Damage.DamageDict.GetValueOrDefault("Heat"), Is.EqualTo(FixedPoint2.New(25)));
                Assert.That(damageable.TotalDamage, Is.EqualTo(FixedPoint2.New(75)));
            });

            entMan.DeleteEntity(human);
        });

        await pair.CleanReturnAsync();
    }

    private static List<CMUBodyScannerScanLine> BuildScanLines(
        CMUBodyScannerSystem scanner,
        EntityUid patient)
    {
        var method = typeof(CMUBodyScannerSystem).GetMethod(
            "BuildScanLines",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null);
        return (List<CMUBodyScannerScanLine>) method!.Invoke(scanner, new object[] { patient })!;
    }

    private static void SetLegacyDamageCache(
        IEntityManager entMan,
        EntityUid patient,
        FixedPoint2 brute)
    {
        var damageable = entMan.GetComponent<DamageableComponent>(patient);
        var totalDamage = typeof(DamageableComponent).GetField(nameof(DamageableComponent.TotalDamage));
        var damagePerGroup = typeof(DamageableComponent).GetField(nameof(DamageableComponent.DamagePerGroup));

        Assert.That(totalDamage, Is.Not.Null);
        Assert.That(damagePerGroup, Is.Not.Null);

        totalDamage!.SetValue(damageable, brute);
        var groups = (Dictionary<string, FixedPoint2>) damagePerGroup!.GetValue(damageable)!;
        groups["Brute"] = brute;
        entMan.Dirty(patient, damageable);
    }

    private static void SetSuitSensorVitalsTarget(SuitSensorComponent sensor, EntityUid user)
    {
        var mode = typeof(SuitSensorComponent).GetField(nameof(SuitSensorComponent.Mode));
        var sensorUser = typeof(SuitSensorComponent).GetField(nameof(SuitSensorComponent.User));

        Assert.That(mode, Is.Not.Null);
        Assert.That(sensorUser, Is.Not.Null);

        mode!.SetValue(sensor, SuitSensorMode.SensorVitals);
        sensorUser!.SetValue(sensor, user);
    }
}
