using System.Numerics;
using System.Reflection;
using Content.Server._AU14.ZLevelBuilding;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Shared._CMU14.ZLevels.Core.Components;
using Robust.Server;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests._CMU14.HunterShip;

[TestFixture]
public sealed class HunterShipZLevelIsolationTest
{
    private const string HunterShipMap = "CMUYautjaHunterShip";
    private const string ReinforcedHull = "CMUHunterShipWallTurfClosedWallHuntershipReinforcedHunterBase";
    private static readonly Vector2 MainDeckOnlyWallPosition = new(57.5f, 38.5f);

    [Test]
    public async Task AuthoredLowerDeckDoesNotMirrorMainDeckWalls()
    {
        var options = new RobustIntegrationTest.ServerIntegrationOptions
        {
            ContentStart = true,
            FailureLogLevel = LogLevel.Fatal,
            Options = new ServerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = true,
            },
            ContentAssemblies =
            [
                typeof(Shared.Entry.EntryPoint).Assembly,
                typeof(Server.Entry.EntryPoint).Assembly,
            ],
        };

        using var server = new RobustIntegrationTest.ServerIntegrationInstance(options);
        await server.WaitIdleAsync();

        EntityUid actor = default;
        EntityUid lowerMap = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var ticker = entMan.System<GameTicker>();
            var mapPrototype = prototypes.Index<GameMapPrototype>(HunterShipMap);
            var options = DeserializationOptions.Default with { InitializeMaps = true };

            ticker.LoadGameMap(mapPrototype, out var mainMapId, options);

            EntityUid mainMap = default;
            var mapQuery = entMan.EntityQueryEnumerator<MapComponent>();
            while (mapQuery.MoveNext(out var mapUid, out var map))
            {
                if (map.MapId == mainMapId)
                {
                    mainMap = mapUid;
                    break;
                }
            }

            Assert.That(mainMap, Is.Not.EqualTo(EntityUid.Invalid));
            var zLevel = entMan.GetComponent<CMUZLevelMapComponent>(mainMap);
            Assert.That(zLevel.MapBelow, Is.Not.Null);
            lowerMap = zLevel.MapBelow!.Value;

            Assert.That(CountPrototypeAt(entMan, mainMap, ReinforcedHull, MainDeckOnlyWallPosition), Is.EqualTo(1),
                "The regression coordinate must contain the source wall on the main deck.");
            Assert.That(CountPrototypeAt(entMan, lowerMap, ReinforcedHull, MainDeckOnlyWallPosition), Is.Zero,
                "The authored lower deck must start without the main-deck-only wall.");

            var lowerMapComponent = entMan.GetComponent<MapComponent>(lowerMap);
            actor = entMan.SpawnEntity(null, new MapCoordinates(MainDeckOnlyWallPosition, lowerMapComponent.MapId));
            entMan.EnsureComponent<ActorComponent>(actor);

            var building = entMan.System<ZLevelBuildingSystem>();
            var nextStream = typeof(ZLevelBuildingSystem).GetField("_nextStream", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(nextStream, Is.Not.Null);
            nextStream!.SetValue(building, TimeSpan.Zero);
            building.Update(0);

            Assert.That(
                CountPrototypeAt(entMan, lowerMap, ReinforcedHull, MainDeckOnlyWallPosition),
                Is.Zero,
                "Entering the authored lower deck must not copy an invincible wall down from the main deck.");
        });

        await server.WaitPost(() =>
        {
            if (actor != default && !server.EntMan.Deleted(actor))
                server.EntMan.DeleteEntity(actor);
        });
    }

    [Test]
    public async Task HunterShipStairsKeepAdjacentWestAndNorthOffsetsAligned()
    {
        var options = new RobustIntegrationTest.ServerIntegrationOptions
        {
            ContentStart = true,
            FailureLogLevel = LogLevel.Fatal,
            Options = new ServerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = true,
            },
            ContentAssemblies =
            [
                typeof(Shared.Entry.EntryPoint).Assembly,
                typeof(Server.Entry.EntryPoint).Assembly,
            ],
        };

        using var server = new RobustIntegrationTest.ServerIntegrationInstance(options);
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var ticker = entMan.System<GameTicker>();
            var mapPrototype = prototypes.Index<GameMapPrototype>(HunterShipMap);
            var loadOptions = DeserializationOptions.Default with { InitializeMaps = true };

            ticker.LoadGameMap(mapPrototype, out var mainMapId, loadOptions);

            EntityUid mainMap = default;
            var mapQuery = entMan.EntityQueryEnumerator<MapComponent>();
            while (mapQuery.MoveNext(out var mapUid, out var map))
            {
                if (map.MapId == mainMapId)
                {
                    mainMap = mapUid;
                    break;
                }
            }

            Assert.That(mainMap, Is.Not.EqualTo(EntityUid.Invalid));
            var mainZLevel = entMan.GetComponent<CMUZLevelMapComponent>(mainMap);
            Assert.Multiple(() =>
            {
                Assert.That(mainZLevel.MapBelow, Is.Not.Null);
                Assert.That(mainZLevel.MapAbove, Is.Not.Null);
            });

            var lowerMap = mainZLevel.MapBelow!.Value;
            var upperMap = mainZLevel.MapAbove!.Value;

            AssertStair(entMan, lowerMap, "CMUHunterShipObjStructureStairsMultizUpRampbottomWest",
                new Vector2(28.5f, 52.5f), Direction.West, 1);
            AssertStair(entMan, mainMap, "CMUHunterShipObjStructureStairsMultizDownRampbottomWest",
                new Vector2(27.5f, 52.5f), Direction.West, -1);
            AssertStair(entMan, mainMap, "CMUHunterShipObjStructureStairsMultizUpRampbottomNorth",
                new Vector2(44.5f, 60.5f), Direction.North, 1);
            AssertStair(entMan, upperMap, "CMUHunterShipObjStructureStairsMultizDownRampbottomNorth",
                new Vector2(44.5f, 61.5f), Direction.North, -1);
        });
    }

    private static void AssertStair(
        IEntityManager entMan,
        EntityUid mapUid,
        string prototype,
        Vector2 worldPosition,
        Direction direction,
        int offset)
    {
        var stair = FindStairAt(entMan, mapUid, prototype, worldPosition);
        Assert.That(stair, Is.Not.Null, $"Expected {prototype} at {worldPosition} on map {mapUid}.");
        Assert.Multiple(() =>
        {
            Assert.That(stair!.Direction, Is.EqualTo(direction), prototype);
            Assert.That(stair.Offset, Is.EqualTo(offset), prototype);
        });
    }

    private static CMUZLevelStairsComponent? FindStairAt(
        IEntityManager entMan,
        EntityUid mapUid,
        string prototype,
        Vector2 worldPosition)
    {
        var transform = entMan.System<SharedTransformSystem>();
        var query = entMan.EntityQueryEnumerator<CMUZLevelStairsComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out var stair, out var metadata, out var xform))
        {
            if (xform.MapUid == mapUid &&
                metadata.EntityPrototype?.ID == prototype &&
                Vector2.DistanceSquared(transform.GetWorldPosition(xform), worldPosition) <= 0.001f)
            {
                return stair;
            }
        }

        return null;
    }

    private static int CountPrototypeAt(
        IEntityManager entMan,
        EntityUid mapUid,
        string prototype,
        Vector2 worldPosition)
    {
        var transform = entMan.System<SharedTransformSystem>();
        var count = 0;
        var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();

        while (query.MoveNext(out _, out var metadata, out var xform))
        {
            if (xform.MapUid != mapUid ||
                metadata.EntityPrototype?.ID != prototype ||
                Vector2.DistanceSquared(transform.GetWorldPosition(xform), worldPosition) > 0.001f)
            {
                continue;
            }

            count++;
        }

        return count;
    }
}
