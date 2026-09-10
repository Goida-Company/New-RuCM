using System.Linq;
using System.Reflection;
using Content.Client._CMU14.Yautja;
using Robust.Client.UserInterface.Controls;
using Content.Server.CharacterInfo;
using Content.Server.GameTicking;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Destructible;
using Content.Server._CMU14.Yautja;
using Content.Shared._CMU14.Yautja;
using Content.Shared._RMC14.Language.Prototypes;
using Content.Shared._RMC14.Language.Components;
using Content.Shared._RMC14.Stack;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared._RMC14.UniformAccessories;
using Content.Shared.Clothing;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared._RMC14.Stealth;
using Robust.Client.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._CMU14.Yautja;

[TestFixture]
public sealed class YautjaLatestFeedbackTest
{
    [Test]
    public async Task MarkWindowRequiresAndSendsThrallReason()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var window = new YautjaMarkWindow();
            var type = typeof(YautjaMarkWindow);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            window.UpdateState(new YautjaMarkPanelState([new YautjaMarkPanelEntry(new NetEntity(1), "Prey", false, [])]));
            type.GetField("_selectedIndex", flags)!.SetValue(window, 0);
            var option = (OptionButton) type.GetField("_markKindOption", flags)!.GetValue(window)!;
            var reason = (LineEdit) type.GetField("_reason", flags)!.GetValue(window)!;
            var apply = (Button) type.GetField("_markButton", flags)!.GetValue(window)!;
            option.SelectId((int) YautjaMarkKind.Thrall);
            type.GetMethod("RefreshSelectionState", flags)!.Invoke(window, null);
            Assert.That(apply.Disabled, Is.True);
            string sent = null;
            window.OnMark += (_, _, text) => sent = text;
            reason.Text = "  Survived the hunt  ";
            type.GetMethod("RefreshSelectionState", flags)!.Invoke(window, null);
            Assert.That(apply.Disabled, Is.False);
            type.GetMethod("SendMark", flags)!.Invoke(window, [false]);
            Assert.That(sent, Is.EqualTo("Survived the hunt"));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReduxGarrisonLoadsThreeGroundDeploymentChoices()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            Assert.That(em.System<MapLoaderSystem>().TryLoadMap(
                new ResPath("/Maps/_AU14/StableGarrisonMultiZ/StableGarrisonMultiZ0.yml"),
                out var map, out _, DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
            var markers = em.EntityQuery<YautjaRelayDestinationComponent, TransformComponent>()
                .Where(entry => entry.Item1.Kind == YautjaRelayDestinationKind.Ground && entry.Item2.MapUid == map!.Value.Owner).ToList();
            Assert.That(markers, Has.Count.EqualTo(3));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("CMXenoWarrior")]
    [TestCase("CMXenoLurker")]
    [TestCase("RMCXenoPraetorianDancer")]
    public async Task FourOrdinaryXenoHitsDamageButDoNotKillHealthyArmoredHunter(string attackerPrototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            var armor = em.SpawnEntity("CMUYautjaClanArmor", map.GridCoords);
            Assert.That(em.System<InventorySystem>().TryEquip(hunter, armor, "outerClothing", silent: true), Is.True);
            var xeno = em.SpawnEntity(attackerPrototype, map.GridCoords.Offset(new(1, 0)));
            var weapon = em.GetComponent<MeleeWeaponComponent>(xeno);
            for (var i = 0; i < 4; i++)
                em.System<DamageableSystem>().TryChangeDamage(hunter, new DamageSpecifier(weapon.Damage), origin: xeno, tool: xeno);
            Assert.That(em.GetComponent<DamageableComponent>(hunter).TotalDamage, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(em.System<MobStateSystem>().IsAlive(hunter), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BracerCloakReachesClientAndInstallsInvisibilityShader()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid hunter = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            Assert.That(em.System<InventorySystem>().TryGetSlotEntity(hunter, "gloves", out var bracer), Is.True);
            Assert.That(em.System<YautjaCloakSystem>().TryToggleCloakForced(
                (bracer!.Value, em.GetComponent<YautjaBracerComponent>(bracer.Value)), hunter, FixedPoint2.Zero), Is.True);
        });
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.EntMan;
            var clientHunter = em.GetEntity(pair.Server.EntMan.GetNetEntity(hunter));
            Assert.That(em.GetComponent<EntityActiveInvisibleComponent>(clientHunter).Opacity, Is.LessThan(0.5f));
            Assert.That(em.System<SpriteSystem>().TryGetPostShader(em.GetComponent<SpriteComponent>(clientHunter), "RMCInvisible", out _), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeniorRankReservationDoesNotConsumeOrdinarySlotsAndUnusedReservationsAreReleased()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var station = em.SpawnEntity(null, map.GridCoords);
            em.EnsureComponent<StationDataComponent>(station);
            em.System<MetaDataSystem>().SetEntityName(station,
                pair.Server.ResolveDependency<IPrototypeManager>().Index<GameMapPrototype>("CMUYautjaHunterShip").MapName);
            var jobs = new StationJobsComponent { SetupAvailableJobs = new() };
            em.AddComponent(station, jobs);
            var slots = em.System<StationJobsSystem>();
            slots.TrySetJobSlot(station, "CMUYautjaHunter", 4, true);
            var rule = new YautjaPredatorRoundComponent();
            var hunts = em.System<YautjaPredatorRoundSystem>();
            for (var i = 0; i < 3; i++)
                hunts.EnsureRankBypassSlot(rule);
            Assert.That(jobs.JobList["CMUYautjaHunter"], Is.EqualTo(5));
            Assert.That(rule.RankBypassSlotsRemaining, Is.EqualTo(1));
            hunts.ReleaseUnusedRankReservations(rule);
            Assert.That(jobs.JobList["CMUYautjaHunter"], Is.EqualTo(4));
            Assert.That(rule.RankBypassSlotsRemaining, Is.Zero);
            slots.TrySetJobSlot(station, "CMUYautjaHunter", 0);
            hunts.EnsureRankBypassSlot(rule);
            Assert.That(jobs.JobList["CMUYautjaHunter"], Is.EqualTo(1));
            Assert.That(YautjaPredatorRoundSystem.ShouldExcludeOrdinaryRankFromHunterCandidates(YautjaRank.Blooded, 1, 1), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TrophiesAttachToWornArmorAndOnlySkullAddsAnExistingClientLayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid armor = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            armor = em.SpawnEntity("CMUYautjaClanArmor", map.GridCoords);
            Assert.That(em.System<InventorySystem>().TryEquip(hunter, armor, "outerClothing", silent: true), Is.True);
            foreach (var proto in new[] { "CMUYautjaHumanLeftArmBoneTrophy", "CMUYautjaHumanSkullTrophy" })
            {
                var trophy = em.SpawnEntity(proto, map.GridCoords);
                Assert.That(em.System<SharedUniformAccessorySystem>().TryInsertUniformAccessory(trophy, armor, hunter), Is.True, proto);
            }
        });
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.EntMan;
            var clientArmor = em.GetEntity(pair.Server.EntMan.GetNetEntity(armor));
            var visuals = new GetEquipmentVisualsEvent(clientArmor, "outerClothing");
            em.EventBus.RaiseLocalEvent(clientArmor, visuals);
            var trophies = visuals.Layers.Where(layer => layer.Item2.RsiPath?.Contains("Yautja/trophy") == true).ToList();
            Assert.That(trophies, Has.Count.EqualTo(1));
            Assert.That(trophies.Single().Item2.State, Is.EqualTo("skull"));
            Assert.That(visuals.Layers.Any(layer => layer.Item2.State == "equipped"), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("desert_moon")]
    [TestCase("desert_moon_caves")]
    public async Task LoadedDesertBoundaryRocksCannotBreakWhileInteriorRocksCan(string file)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            Assert.That(em.System<MapLoaderSystem>().TryLoadMap(new ResPath($"/Maps/_CMU14/HuntingGrounds/{file}.yml"),
                out var map, out _, DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
            var rocks = em.EntityQuery<DestructibleComponent, MetaDataComponent, TransformComponent>()
                .Where(entry => entry.Item2.EntityPrototype?.ID == "WallRock" && entry.Item3.MapUid == map!.Value.Owner).ToList();
            Assert.That(rocks.Count(entry => entry.Item1.Thresholds.Count == 0), Is.EqualTo(416));
            Assert.That(rocks.Count(entry => entry.Item1.Thresholds.Count > 0), Is.GreaterThan(0));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HumanSedativeDoesNotMetabolizeInYautjaButAlienMedicineDoes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        EntityUid hunter = default;
        EntityUid human = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var metabolism = em.System<MetabolizerSystem>();
            var solutions = em.System<SharedSolutionContainerSystem>();
            hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            human = em.SpawnEntity("CMMobHuman", map.GridCoords);
            foreach (var reagent in new[] { "CMUSoporific", "CMUFentanyl", "CMBicaridine" })
            {
                Assert.That(metabolism.CanMetabolizeReagent(hunter, prototypes.Index<ReagentPrototype>(reagent)), Is.False, reagent);
                Assert.That(metabolism.CanMetabolizeReagent(human, prototypes.Index<ReagentPrototype>(reagent)), Is.True, reagent);
            }
            Assert.That(metabolism.CanMetabolizeReagent(hunter, prototypes.Index<ReagentPrototype>("thwei")), Is.True);
            Assert.That(metabolism.CanMetabolizeReagent(human, prototypes.Index<ReagentPrototype>("thwei")), Is.False);
            Assert.That(metabolism.CanMetabolizeReagent(human, prototypes.Index<ReagentPrototype>("dathwei")), Is.True);

            solutions.EnsureSolution(hunter, "chemicals", out var hunterSolution);
            solutions.EnsureSolution(human, "chemicals", out var humanSolution);
            hunterSolution.MaxVolume = 100;
            humanSolution.MaxVolume = 100;
            hunterSolution.AddReagent("CMUSoporific", FixedPoint2.New(10));
            hunterSolution.AddReagent("Nutriment", FixedPoint2.New(10));
            humanSolution.AddReagent("CMUSoporific", FixedPoint2.New(10));
        });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await pair.Server.WaitAssertion(() =>
        {
            var solutions = pair.Server.EntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(hunter, "chemicals", out _, out var hunterSolution), Is.True);
            Assert.That(solutions.TryGetSolution(human, "chemicals", out _, out var humanSolution), Is.True);
            Assert.That(hunterSolution.GetTotalPrototypeQuantity("CMUSoporific"), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(hunterSolution.GetTotalPrototypeQuantity("Nutriment"), Is.LessThan(FixedPoint2.New(10)));
            Assert.That(humanSolution.GetTotalPrototypeQuantity("CMUSoporific"), Is.LessThan(FixedPoint2.New(10)));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FactionPrimerAndJobTraitsAreIndependentOfHumanRoundSides()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var info = em.System<CharacterInfoSystem>();
            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            foreach (var preset in new[] { "ForceOnForce", "DistressSignal", "Insurgency" })
            {
                em.System<GameTicker>().SetGamePreset(preset);
                var lines = info.GetLorePrimerLines(hunter, "CMUYautjaHunter", false);
                Assert.That(lines, Has.Count.EqualTo(1));
                Assert.That(lines.Single(), Is.EqualTo(pair.Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>()
                    .GetString("cmu-yautja-character-primer")));
            }
            foreach (var job in prototypes.EnumeratePrototypes<JobPrototype>().Where(j => j.ID.StartsWith("CMUYautja")))
                Assert.That(job.ApplyTraits, Is.False, job.ID);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SplitMappedHerbsKeepAlienMedicineAndCounts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            foreach (var (mapped, expected) in new[]
                     {
                         ("CMUHunterShipPlacedCMUYautjaAdvancedBruisePackBruteHerbsSouthOffset4x9", "CMUYautjaAdvancedBruisePack"),
                         ("CMUHunterShipPlacedCMUYautjaAdvancedOintmentBurnHerbsSouthOffset1x1", "CMUYautjaAdvancedOintment"),
                     })
            {
                var herbs = em.SpawnEntity(mapped, map.GridCoords);
                var stack = em.GetComponent<StackComponent>(herbs);
                var split = em.System<SharedRMCStackSystem>().Split((herbs, stack), 1, map.GridCoords);
                Assert.That(split, Is.Not.Null);
                Assert.That(em.GetComponent<MetaDataComponent>(split!.Value).EntityPrototype!.ID, Is.EqualTo(expected));
                Assert.That(em.HasComponent<YautjaMedicalItemComponent>(split.Value), Is.True);
                Assert.That(stack.Count, Is.EqualTo(9));
                Assert.That(em.GetComponent<StackComponent>(split.Value).Count, Is.EqualTo(1));
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MappedCrystalsThrallBoxesHellhoundAndBackWeaponsWorkAfterMapInit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var solutions = em.System<SharedSolutionContainerSystem>();
            foreach (var id in new[] { "CMUHunterShipPlacedCMUYautjaAutoInjectorCrystalSouthOffset9x0", "CMUYautjaThrallAutoInjector" })
            {
                var crystal = em.SpawnEntity(id, map.GridCoords);
                Assert.That(solutions.TryGetSolution(crystal, "pen", out _, out var solution), Is.True);
                Assert.That(solution!.Volume.Float(), Is.EqualTo(30), id);
            }
            var box = em.SpawnEntity("CMUYautjaThrallGearBox", map.GridCoords);
            Assert.That(em.GetComponent<StorageComponent>(box).Container.ContainedEntities.Any(uid =>
                em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CMUYautjaThrallBracer"), Is.True);

            var hound = em.SpawnEntity("CMUMobYautjaHellhound", map.GridCoords);
            Assert.That(em.GetComponent<LanguageComponent>(hound).SpokenLanguages,
                Does.Contain((ProtoId<LanguagePrototype>) "Yautja"));

            var hunter = em.SpawnEntity("CMUMobYautja", map.GridCoords);
            var inventory = em.System<InventorySystem>();
            foreach (var weapon in new[] { "CMUYautjaClanSword", "CMUYautjaHunterSpear", "CMUYautjaWarGlaive", "CMUYautjaCombistick", "CMUYautjaWarAxe", "CMUYautjaLongaxe", "CMUYautjaHuntingBow" })
            {
                var item = em.SpawnEntity(weapon, map.GridCoords);
                Assert.That(inventory.TryEquip(hunter, item, "back", silent: true), Is.True, weapon);
                Assert.That(inventory.TryUnequip(hunter, "back", silent: true), Is.True, weapon);
                em.DeleteEntity(item);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task YoungbloodCallChoosesShipBedroomsEvenWhenPreserveMarkerWasSpawnedFirst()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var preserveMap = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            em.SpawnEntity("CMUYautjaYoungbloodSpawn", preserveMap.GridCoords);
            em.SpawnEntity("CMUYautjaYoungbloodDestinationJungleMoon", preserveMap.GridCoords);
            var bedroom = em.SpawnEntity("CMUHunterShipMarkerPredatorSpawn", map.GridCoords);
            var console = em.SpawnEntity("CMUHunterShipBloodingConsole", map.GridCoords);
            var comp = em.GetComponent<YautjaHuntConsoleComponent>(console);
            var option = comp.BloodingCallOptions.Single(o => o.Id == "youngblood_pack");
            Assert.That(em.System<YautjaHuntConsoleSystem>().TryCreateYoungbloodCall((console, comp), console, option, true), Is.True);
            var hunters = em.EntityQuery<YautjaYoungbloodGhostRoleComponent>().ToList();
            Assert.That(hunters.Count, Is.InRange(4, 6));
            foreach (var hunter in hunters)
                Assert.That(em.GetComponent<TransformComponent>(hunter.Owner).MapUid,
                    Is.EqualTo(em.GetComponent<TransformComponent>(bedroom).MapUid));
        });
        await pair.CleanReturnAsync();
    }
}
