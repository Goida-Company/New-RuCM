using System.Numerics;
using Content.Server.Hands.Systems;
using Content.Shared._CMU14.Yautja;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Client.ResourceManagement;
using Robust.Shared.Utility;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests._CMU14.Yautja;

[TestFixture]
[NonParallelizable]
public sealed class YautjaFullThrowRegressionTest
{
    [TestCase("CMUYautjaHunterSpear", "hunter_spear")]
    [TestCase("CMUYautjaCombistick", "combistick")]
    [TestCase("CMUYautjaWarGlaive", "war_glaive")]
    [TestCase("CMUYautjaCleavingGlaive", "cleaving_glaive")]
    [TestCase("CMUYautjaAncientWarGlaive", "ancient_war_glaive")]
    [TestCase("CMUYautjaLongaxe", "longaxe")]
    public async Task WieldAndUnwieldUseTheWeaponsOwnSprite(string prototype, string prefix)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            var weapon = em.SpawnEntity(prototype, map.GridCoords);
            var item = em.GetComponent<ItemComponent>(weapon);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(hunter, weapon), Is.True);
            Assert.That(item.HeldPrefix, Is.EqualTo(prefix));
            var wieldable = em.GetComponent<WieldableComponent>(weapon);
            Assert.That(em.System<SharedWieldableSystem>().TryWield(weapon, wieldable, hunter), Is.True);
            Assert.That(item.HeldPrefix, Is.EqualTo(prefix + "_wielded"));
            em.System<SharedHandsSystem>().TryDrop(hunter, weapon);
            Assert.That(item.HeldPrefix, Is.EqualTo(prefix), "Dropping must restore the one-hand sprite.");
        });
        await pair.Client.WaitAssertion(() =>
        {
            var rsi = pair.Client.ResolveDependency<IResourceCache>()
                .GetResource<RSIResource>(new ResPath("/Textures/_CMU14/Yautja/weapons.rsi")).RSI;
            foreach (var side in new[] { "left", "right" })
            {
                Assert.That(rsi.TryGetState($"{prefix}-inhand-{side}", out _), Is.True);
                Assert.That(rsi.TryGetState($"{prefix}_wielded-inhand-{side}", out _), Is.True);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("CMUYautjaCombistick")]
    [TestCase("CMUYautjaWarAxe")]
    public async Task HandThrowRequiresAndConsumesOneBloodCharge(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            var weapon = em.SpawnEntity(prototype, map.GridCoords);
            var hands = em.System<HandsSystem>();
            var chained = em.GetComponent<YautjaChainedWeaponComponent>(weapon);
            Assert.That(hands.TryPickupAnyHand(hunter, weapon), Is.True);
            Assert.That(hands.ThrowHeldItem(hunter, map.GridCoords.Offset(new Vector2(4, 0))), Is.False);
            Assert.That(hands.IsHolding(hunter, weapon), Is.True);
            chained.Charged = true;
            Assert.That(hands.ThrowHeldItem(hunter, map.GridCoords.Offset(new Vector2(4, 0))), Is.True);
            Assert.That(chained.Charged, Is.False);
            Assert.That(chained.LinkedTo, Is.EqualTo(hunter));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActualSpearThrowStopsOnFirstVictim()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid first = default, second = default, weapon = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            first = em.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            second = em.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(4, 0)));
            weapon = em.SpawnEntity("CMUYautjaHunterSpear", map.GridCoords);
            var hands = em.System<HandsSystem>();
            Assert.That(hands.TryPickupAnyHand(hunter, weapon), Is.True);
            Assert.That(hands.ThrowHeldItem(hunter, map.GridCoords.Offset(new Vector2(7, 0))), Is.True);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1));
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            Assert.That(em.GetComponent<DamageableComponent>(first).TotalDamage.Float(), Is.GreaterThan(0));
            Assert.That(em.GetComponent<DamageableComponent>(second).TotalDamage.Float(), Is.EqualTo(0));
            Assert.That(em.HasComponent<ThrownItemComponent>(weapon), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(10f, 16f)]
    [TestCase(20f, 24f)]
    public async Task ImpactUsesCollisionSpeedWithoutTechDamageBonus(float velocity, float expected)
    {
        // Harpoon throwforce 30: (1 + .02*30)*30*.05*source_speed.
        // Speed 10 tiles/s -> CM 6.6667; 20 tiles/s -> CM 10.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var weapon = em.SpawnEntity("CMUYautjaWarAxe", map.GridCoords);
            var target = em.SpawnEntity("CMMobHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            var physics = em.GetComponent<PhysicsComponent>(weapon);
            em.System<SharedPhysicsSystem>().SetLinearVelocity(weapon, new Vector2(velocity, 0), body: physics);
            // Compare the damage API's actual result against the source formula, not the melee value.
            var profile = em.GetComponent<YautjaThrowComponent>(weapon);
            var result = em.System<Content.Server._CMU14.Yautja.YautjaThrowSystem>().ApplyHit(
                (weapon, profile), target, em.GetComponent<Content.Shared.Damage.Components.DamageOtherOnHitComponent>(weapon),
                new ThrownItemComponent(), DamageImpact.ForThrown(new DamageSpecifier()));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.GetTotal().Float(), Is.EqualTo(expected).Within(0.1));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EligibleWeaponEmbedsInHumanLimbButNeverInYautja(bool yautja)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var weapon = em.SpawnEntity("CMUYautjaHunterSpear", map.GridCoords);
            var target = em.SpawnEntity(yautja ? "CMUMobYautja" : "CMMobHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            var profile = em.GetComponent<YautjaThrowComponent>(weapon);
            profile.Force = 60; // Exercises the source's guaranteed-embed damage threshold.
            em.System<SharedPhysicsSystem>().SetLinearVelocity(weapon, new Vector2(20, 0));
            em.System<Content.Server._CMU14.Yautja.YautjaThrowSystem>().ApplyHit(
                (weapon, profile), target, em.GetComponent<Content.Shared.Damage.Components.DamageOtherOnHitComponent>(weapon),
                new ThrownItemComponent(), DamageImpact.ForThrown(new DamageSpecifier()));
            if (yautja)
                Assert.That(em.HasComponent<EmbeddableProjectileComponent>(weapon), Is.False);
            else
            {
                var embedded = em.GetComponent<EmbeddableProjectileComponent>(weapon);
                Assert.That(embedded.EmbeddedIntoUid, Is.Not.Null);
                var part = em.GetComponent<Content.Shared.Body.Part.BodyPartComponent>(embedded.EmbeddedIntoUid.Value);
                Assert.That(part.Body, Is.EqualTo(target));
                em.System<SharedProjectileSystem>().EmbedDetach(weapon, embedded);
                Assert.That(embedded.EmbeddedIntoUid, Is.Null, "An embedded weapon must remain recoverable.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExtractingWeaponRequiresEmptyHandAndCompletesAfterDelay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid weapon = default, surgeon = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var victim = em.SpawnEntity("CMMobHuman", map.GridCoords);
            surgeon = em.SpawnEntity("CMUMobYautja", map.GridCoords.Offset(new Vector2(1, 0)));
            weapon = em.SpawnEntity("CMUYautjaHunterSpear", map.GridCoords);
            var profile = em.GetComponent<YautjaThrowComponent>(weapon);
            profile.Force = 60;
            em.System<SharedPhysicsSystem>().SetLinearVelocity(weapon, new Vector2(20, 0));
            var system = em.System<Content.Server._CMU14.Yautja.YautjaThrowSystem>();
            system.ApplyHit((weapon, profile), victim,
                em.GetComponent<Content.Shared.Damage.Components.DamageOtherOnHitComponent>(weapon),
                new ThrownItemComponent(), DamageImpact.ForThrown(new DamageSpecifier()));
            Assert.That(em.GetComponent<EmbeddableProjectileComponent>(weapon).EmbeddedIntoUid, Is.Not.Null);
            Assert.That(em.GetComponent<TransformComponent>(weapon).ParentUid, Is.EqualTo(victim));
            var hands = em.System<SharedHandsSystem>();
            var held = em.SpawnEntity("CMUYautjaCeremonialDagger", map.GridCoords);
            hands.TryPickup(surgeon, held, hands.GetActiveHand(surgeon)!);
            Assert.That(system.TryBeginExtraction(weapon, surgeon), Is.False);
            hands.TryDrop(surgeon, held);
            Assert.That(system.TryBeginExtraction(weapon, surgeon), Is.True);
            Assert.That(hands.IsHolding(surgeon, weapon), Is.False);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(10));
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            Assert.That(em.GetComponent<EmbeddableProjectileComponent>(weapon).EmbeddedIntoUid, Is.Null);
            Assert.That(em.System<SharedHandsSystem>().IsHolding(surgeon, weapon), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SmartDiscFinishesInitialThrowBeforeReturningAndCanBeCaught(bool continuousFloor)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid hunter = default, disc = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            // CreateTestMap only supplies one tile. Cover the complete flight path
            // to distinguish a normal ground throw from a throw into zero gravity.
            if (continuousFloor)
            {
                var maps = em.System<SharedMapSystem>();
                for (var x = -2; x <= 10; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, map.GridCoords.Offset(new Vector2(x, y)), map.Tile.Tile);
                em.System<Content.Server.Gravity.GravitySystem>().EnableGravity(map.Grid.Owner);
                em.GetComponent<Content.Shared.Gravity.GravityComponent>(map.Grid.Owner).Inherent = true;
            }
            hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            disc = em.SpawnEntity("CMUYautjaSmartDisc", map.GridCoords);
            Assert.That(em.System<Content.Shared.Gravity.SharedGravitySystem>().IsWeightless(disc), Is.EqualTo(!continuousFloor));
            var hands = em.System<HandsSystem>();
            Assert.That(hands.TryPickupAnyHand(hunter, disc), Is.True);
            Assert.That(hands.ThrowHeldItem(hunter, map.GridCoords.Offset(new Vector2(7, 0))), Is.True);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(0.1f));
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            Assert.That(em.System<SharedHandsSystem>().IsHolding(hunter, disc), Is.False);
            Assert.That(em.GetComponent<YautjaSmartDiscComponent>(disc).ReturningToOwner, Is.False);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var state = em.GetComponent<YautjaSmartDiscComponent>(disc);
            Assert.That(em.System<SharedHandsSystem>().IsHolding(hunter, disc), Is.True,
                $"disc={em.GetComponent<TransformComponent>(disc).Coordinates}, hunter={em.GetComponent<TransformComponent>(hunter).Coordinates}, " +
                $"velocity={em.GetComponent<PhysicsComponent>(disc).LinearVelocity}, returning={state.ReturningToOwner}, " +
                $"pending={state.PendingThrowActivator}, owner={state.YautjaOwner}, target={state.CurrentTarget}, thrown={em.HasComponent<ThrownItemComponent>(disc)}");
            Assert.That(em.HasComponent<ThrownItemComponent>(disc), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
