using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._CMU14.ZLevels.Core;
using Content.Shared._CMU14.ZLevels.Core.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._CMU14.ZLevels;

[TestFixture]
public sealed class CMUDeployableZLevelLadderSystemTest
{
    private static readonly EntProtoId DeployableLadder = "CMUDeployableZLevelLadder";

    [Test]
    public async Task DeployBlockedByOpaqueUpperTile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var maps = await CreateLinkedMaps(pair, "Plating");

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var user = entMan.SpawnEntity(null, maps.Lower.GridCoords);
            var kit = entMan.SpawnEntity(DeployableLadder, maps.Lower.GridCoords);

            entMan.EventBus.RaiseLocalEvent(kit, new UseInHandEvent(user));

            Assert.That(CountLadders(entMan), Is.EqualTo(0));
            Assert.That(entMan.Deleted(kit), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeployThroughTransparentOpeningCreatesPackablePair()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var maps = await CreateLinkedMaps(pair, "Lattice");

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var user = entMan.SpawnEntity(null, maps.Lower.GridCoords);
            var kit = entMan.SpawnEntity(DeployableLadder, maps.Lower.GridCoords);

            entMan.EventBus.RaiseLocalEvent(kit, new UseInHandEvent(user));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var ladders = GetPackableLadders(entMan).ToArray();

            Assert.That(ladders, Has.Length.EqualTo(2));
            Assert.That(ladders.Select(ladder => ladder.Ladder.Offset), Is.EquivalentTo(new[] { -1, 1 }));
            Assert.That(ladders[0].Packable.Partner, Is.EqualTo(ladders[1].Uid));
            Assert.That(ladders[1].Packable.Partner, Is.EqualTo(ladders[0].Uid));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PackDeployedLadderDeletesPairAndReturnsKit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var maps = await CreateLinkedMaps(pair, "Lattice");

        EntityUid user = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            user = entMan.SpawnEntity("CMMobHuman", maps.Lower.GridCoords);
            var kit = entMan.SpawnEntity(DeployableLadder, maps.Lower.GridCoords);

            entMan.EventBus.RaiseLocalEvent(kit, new UseInHandEvent(user));
        });

        await server.WaitRunTicks(1);

        EntityUid lowerLadder = default;
        EntityUid upperLadder = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var ladders = GetPackableLadders(entMan).ToArray();
            Assert.That(ladders, Has.Length.EqualTo(2));

            var lower = ladders.Single(ladder => ladder.Ladder.Offset > 0);
            lowerLadder = lower.Uid;
            upperLadder = lower.Packable.Partner!.Value;

            var deploy = server.System<CMUDeployableZLevelLadderSystem>();
            Assert.That(deploy.TryPack((lower.Uid, lower.Packable), user), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var hands = server.System<SharedHandsSystem>();
            var heldDeployable = hands.EnumerateHeld(user)
                .Any(held => entMan.HasComponent<CMUDeployableZLevelLadderComponent>(held));

            Assert.That(entMan.Deleted(lowerLadder), Is.True);
            Assert.That(entMan.Deleted(upperLadder), Is.True);
            Assert.That(CountLadders(entMan), Is.EqualTo(0));
            Assert.That(heldDeployable, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    private static async Task<(TestMapData Lower, TestMapData Upper)> CreateLinkedMaps(
        TestPair pair,
        string upperTile)
    {
        var lower = await pair.CreateTestMap(tile: "Plating");
        var upper = await pair.CreateTestMap(tile: upperTile);

        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.EntMan;
            var lowerZ = entMan.EnsureComponent<CMUZLevelMapComponent>(lower.MapUid);
            lowerZ.MapAbove = upper.MapUid;
            lowerZ.MapBelow = null;
            lowerZ.Depth = 0;
            lowerZ.NetworkUid = EntityUid.Invalid;
            entMan.Dirty(lower.MapUid, lowerZ);

            var upperZ = entMan.EnsureComponent<CMUZLevelMapComponent>(upper.MapUid);
            upperZ.MapAbove = null;
            upperZ.MapBelow = lower.MapUid;
            upperZ.Depth = 1;
            upperZ.NetworkUid = EntityUid.Invalid;
            entMan.Dirty(upper.MapUid, upperZ);
        });

        return (lower, upper);
    }

    private static int CountLadders(IEntityManager entMan)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<CMUZLevelLadderComponent>();
        while (query.MoveNext(out _, out _))
        {
            count++;
        }

        return count;
    }

    private static IEnumerable<(EntityUid Uid, CMUZLevelLadderComponent Ladder, CMUPackableZLevelLadderComponent Packable)> GetPackableLadders(
        IEntityManager entMan)
    {
        var query = entMan.EntityQueryEnumerator<CMUZLevelLadderComponent, CMUPackableZLevelLadderComponent>();
        while (query.MoveNext(out var uid, out var ladder, out var packable))
        {
            yield return (uid, ladder, packable);
        }
    }
}
