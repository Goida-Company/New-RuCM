using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests._CMU14.Medical;

[TestFixture]
public sealed class OrthopedicEquipmentExamineTest
{
    [Test]
    public async Task SplintExamineShowsUsesRemaining()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var examiner = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var splint = entMan.SpawnEntity("CMUSplintItem", MapCoordinates.Nullspace);

            try
            {
                var message = new FormattedMessage();
                var ev = new ExaminedEvent(message, splint, examiner, isInDetailsRange: true, hasDescription: false);

                entMan.EventBus.RaiseLocalEvent(splint, ev);

                Assert.That(ev.GetTotalMessage().ToString(), Does.Contain("4 uses"));
            }
            finally
            {
                entMan.DeleteEntity(splint);
                entMan.DeleteEntity(examiner);
            }
        });

        await pair.CleanReturnAsync();
    }
}
