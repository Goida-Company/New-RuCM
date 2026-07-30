#nullable enable
using System.Linq;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._RuMC14;

[TestFixture]
public sealed class RuMCEntitySpawnPreviewTest
{
    [Test]
    public async Task RuSearchResultsCanBePreviewed()
    {
        const string search = "ru";

        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var prototypes = pair.Server.ProtoMan
            .EnumeratePrototypes<EntityPrototype>()
            .Where(proto =>
                !proto.Abstract &&
                !proto.HideSpawnMenu &&
                (proto.ID.Contains(search, StringComparison.InvariantCultureIgnoreCase) ||
                 (!string.IsNullOrEmpty(proto.EditorSuffix) &&
                  proto.EditorSuffix.Contains(search, StringComparison.InvariantCultureIgnoreCase)) ||
                 (!string.IsNullOrEmpty(proto.Name) &&
                  proto.Name.Contains(search, StringComparison.InvariantCultureIgnoreCase))))
            .OrderBy(proto => proto.Name, StringComparer.Ordinal)
            .ThenBy(proto => proto.ID, StringComparer.Ordinal)
            .ToArray();

        Assert.That(prototypes, Is.Not.Empty);

        await pair.Client.WaitPost(() =>
        {
            foreach (var prototype in prototypes)
            {
                TestContext.Out.WriteLine($"Preview: {prototype.ID} ({prototype.Name})");
                var entity = pair.Client.EntMan.Spawn(prototype.ID);
                pair.Client.EntMan.System<SpriteSystem>().ForceUpdate(entity);
                pair.Client.EntMan.DeleteEntity(entity);
            }
        });

        await pair.CleanReturnAsync();
    }
}
