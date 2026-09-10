using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._CMU14.Yautja;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._CMU14.Yautja;
using Content.Shared._RMC14.Dialog;
using Content.Shared._RMC14.Medical.Surgery;
using Content.Shared._RMC14.Medical.Surgery.Steps;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Throwing;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests._CMU14.Yautja;

[TestFixture]
public sealed class YautjaSeptemberFeedbackRegressionTest
{
    [TestCase("CMUYautjaBracerShieldAttachment", YautjaGearKind.Shield)]
    [TestCase("CMUYautjaWristBladesAttachment", YautjaGearKind.WristBlades)]
    [TestCase("CMUYautjaScimitarAttachment", YautjaGearKind.Scimitar)]
    public async Task InstalledModuleCanRetractRedeployAndRecoverAfterWeaponDeletion(string prototype, YautjaGearKind kind)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hands = em.System<SharedHandsSystem>();
            var attachments = em.System<YautjaAttachmentSystem>();
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            Assert.That(em.System<InventorySystem>().TryGetSlotEntity(hunter, "gloves", out var wornBracer), Is.True);
            var bracer = wornBracer!.Value;
            var container = em.GetComponent<YautjaGearContainerComponent>(bracer);
            foreach (var old in container.InstalledGear.ToArray())
                em.DeleteEntity(old);
            container.Gear.Clear();
            container.SecondaryGear.Clear();
            container.InstalledGear.Clear();
            var module = em.SpawnEntity(prototype, map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(hunter, module), Is.True);
            var install = new YautjaBracerAttachmentSlotSelectedEvent(em.GetNetEntity(hunter), em.GetNetEntity(module), kind, false);
            em.EventBus.RaiseLocalEvent(bracer, install);
            Assert.That(container.InstalledGear, Does.Contain(module));
            var stored = em.GetComponent<YautjaStoredGearComponent>(module);

            for (var cycle = 0; cycle < 3; cycle++)
            {
                Assert.That(attachments.TryToggleBracerAttachments((bracer, container), hunter), Is.True);
                Assert.That(stored.Deployed, Is.True, $"cycle {cycle}");
                var weapon = stored.AttachedWeapon!.Value;
                Assert.That(hands.IsHolding(hunter, weapon), Is.True);
                var use = new UseInHandEvent(hunter);
                em.EventBus.RaiseLocalEvent(weapon, use);
                Assert.That(use.Handled, Is.True);
                Assert.That(stored.Deployed, Is.False, $"cycle {cycle}: holder must retract too");
                Assert.That(em.GetComponent<YautjaStoredGearComponent>(weapon).Deployed, Is.False);
                Assert.That(hands.IsHolding(hunter, weapon), Is.False);
            }

            attachments.TryToggleBracerAttachments((bracer, container), hunter);
            em.DeleteEntity(stored.AttachedWeapon!.Value);
            Assert.That(stored.Deployed, Is.False);
            Assert.That(stored.AttachedWeapon, Is.Null);
            attachments.TryToggleBracerAttachments((bracer, container), hunter);
            Assert.That(stored.Deployed, Is.True);
            if (kind == YautjaGearKind.Shield)
                Assert.That(em.HasComponent<DamageableComponent>(stored.AttachedWeapon!.Value), Is.False);
            attachments.TryToggleBracerAttachments((bracer, container), hunter);
            Assert.That(attachments.TryRemoveBracerAttachments((bracer, container), hunter), Is.True);
            Assert.That(container.InstalledGear, Does.Not.Contain(module));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HunterSpearThrowHitsAndDamagesTarget(bool wielded)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid target = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            target = em.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            var spear = em.SpawnEntity("CMUYautjaHunterSpear", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(hunter, spear), Is.True);
            if (wielded)
                Assert.That(em.System<SharedWieldableSystem>().TryWield(spear, em.GetComponent<WieldableComponent>(spear), hunter), Is.True);
            Assert.That(em.System<Content.Server.Hands.Systems.HandsSystem>()
                .ThrowHeldItem(hunter, map.GridCoords.Offset(new Vector2(3, 0))), Is.True);
            Assert.That(em.HasComponent<ThrownItemComponent>(spear), Is.True);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1));
        await pair.Server.WaitAssertion(() =>
            Assert.That(pair.Server.EntMan.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.GreaterThan(0),
                "An actual thrown spear must hit the human, not just drop or fly harmlessly through it."));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HumanMustBeOwnThrallBeforeBloodingAndCannotBeYoungbloodStudent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            var target = em.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(1, 0)));
            Assert.That(em.System<InventorySystem>().TryGetSlotEntity(hunter, "gloves", out var wornBracer), Is.True);
            var bracer = wornBracer!.Value;
            var comp = em.GetComponent<YautjaBracerComponent>(bracer);
            var marks = em.System<YautjaMarkSystem>();
            Assert.That(marks.TryMark((bracer, comp), hunter, target, YautjaMarkKind.Student, null), Is.False);
            Assert.That(marks.TryMark((bracer, comp), hunter, target, YautjaMarkKind.Blooded, "earned honor"), Is.False);
            Assert.That(marks.TryMark((bracer, comp), hunter, target, YautjaMarkKind.Thrall, "proven worthy"), Is.True);
            Assert.That(marks.TryMark((bracer, comp), hunter, target, YautjaMarkKind.Blooded, "earned honor"), Is.True);
            Assert.That(em.GetComponent<YautjaThrallComponent>(target).Blooded, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HunterDoesNotRotAndMedicompSelfSurgeryPassesArmorCheck()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hunter = em.SpawnEntity("CMUMobYautja", MapCoordinates.Nullspace);
            Assert.That(em.HasComponent<PerishableComponent>(hunter), Is.False);
            Assert.That(em.HasComponent<RottingComponent>(hunter), Is.False);
            var armor = em.SpawnEntity("CMUYautjaClanArmor", MapCoordinates.Nullspace);
            Assert.That(em.System<InventorySystem>().TryEquip(hunter, armor, "outerClothing", silent: true, force: true), Is.True);
            foreach (var (stepId, toolId) in new[]
            {
                ("CMUSurgeryStepMcompStabilizeWounds", "CMUYautjaStabilizerGel"),
                ("CMUSurgeryStepMcompTendWounds", "CMUYautjaHealingGun"),
                ("CMUSurgeryStepMcompClampWound", "CMUYautjaWoundClamp"),
            })
            {
                var step = em.SpawnEntity(stepId, MapCoordinates.Nullspace);
                var tool = em.SpawnEntity(toolId, MapCoordinates.Nullspace);
                Assert.That(em.System<SharedCMSurgerySystem>().CanPerformStep(hunter, hunter, BodyPartType.Torso,
                    step, false, tool, out _, out var reason, out _), Is.True, $"{stepId}: {reason}");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BigSelfDestructReachesTwelveTilesInOpenSpace()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var bracer = new YautjaBracerComponent { SelfDestructExplosionType = YautjaSelfDestructExplosionType.Big };
            var radius = pair.Server.EntMan.System<ExplosionSystem>().IntensityToRadius(
                YautjaSelfDestructSystem.SelfDestructTotalIntensity(bracer),
                YautjaSelfDestructSystem.SelfDestructIntensitySlope(bracer),
                YautjaSelfDestructSystem.SelfDestructMaxIntensity(bracer));
            Assert.That(radius, Is.EqualTo(12).Within(0.001));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipPlatformOffersColonyChoicesAndRejectsSelectionAfterWalkingAway()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var transforms = em.System<SharedTransformSystem>();
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            var platform = em.SpawnEntity(null, map.GridCoords);
            em.AddComponent<YautjaHuntTeleporterComponent>(platform).Kind = YautjaHuntTeleporterKind.Ship;
            var destinations = new List<EntityUid>();
            for (var i = 0; i < 2; i++)
            {
                var destination = em.SpawnEntity(null, map.GridCoords.Offset(new Vector2(5 + i, 0)));
                var point = em.AddComponent<YautjaRelayDestinationComponent>(destination);
                point.Kind = YautjaRelayDestinationKind.Ground;
                point.Id = $"feedback-{i}";
                point.DisplayName = $"Colony {i}";
                destinations.Add(destination);
            }
            var step = new StepTriggeredOnEvent(platform, hunter);
            em.EventBus.RaiseLocalEvent(platform, ref step);
            var dialog = em.GetComponent<DialogComponent>(platform);
            Assert.That(dialog.Options.Count, Is.EqualTo(2));
            var selection = new YautjaColonyDeploySelectedEvent(em.GetNetEntity(hunter), "feedback-1");
            transforms.SetCoordinates(hunter, map.GridCoords.Offset(new Vector2(-4, 0)));
            em.EventBus.RaiseLocalEvent(platform, selection);
            Assert.That(transforms.GetMapCoordinates(hunter).Position,
                Is.Not.EqualTo(transforms.GetMapCoordinates(destinations[1]).Position));
            transforms.SetCoordinates(hunter, map.GridCoords);
            em.EventBus.RaiseLocalEvent(platform, selection);
            Assert.That(transforms.GetMapCoordinates(hunter), Is.EqualTo(transforms.GetMapCoordinates(destinations[1])));

            var relay = em.SpawnEntity("CMUYautjaRelayBeacon", map.GridCoords);
            var relayComp = em.GetComponent<YautjaRelayBeaconComponent>(relay);
            Assert.That(relayComp.AllowedDestinations, Is.EquivalentTo(new[]
            {
                YautjaRelayDestinationKind.YautjaShip, YautjaRelayDestinationKind.HumanShip,
            }));
            Assert.That(relayComp.AllowCustomDestinations, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DreadColorSurvivesProfileApplicationAndSubsequentStatsTicks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        EntityUid hunter = default;
        var profile = YautjaCharacterProfile.Default.WithDreadColor(YautjaDreadColor.Bone);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            hunter = em.SpawnEntity("CMUMobYautja", MapCoordinates.Nullspace);
            em.System<YautjaProfileApplySystem>().ApplyProfile(hunter, profile, equipProfileGear: false);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            var humanoid = pair.Server.EntMan.GetComponent<HumanoidAppearanceComponent>(hunter);
            var markings = humanoid.MarkingSet.Markings.Values.SelectMany(markings => markings).ToArray();
            Assert.That(markings, Is.Not.Empty);
            Assert.That(markings.SelectMany(marking => marking.MarkingColors), Does.Contain(profile.Appearance.HairColor));
        });
        await pair.CleanReturnAsync();
    }
}
