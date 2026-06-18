using System.Collections.Generic;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._AU14.WorkingJoe;

[TestFixture]
public sealed class WorkingJoePrototypeTest
{
    private const string WorkingJoeSpecies = "WorkingJoe";

    private static readonly Dictionary<string, string> WorkingJoeJobEntities = new()
    {
        ["AU14JobColonyWorkingJoe"] = "AU14MobWorkingJoeColony",
        ["AU14JobGOVFORWorkingJoe"] = "AU14MobWorkingJoeGOVFOR",
        ["AU14JobOPFORWorkingJoe"] = "AU14MobWorkingJoeOPFOR",
    };

    [Test]
    public async Task WorkingJoeSpeciesAndJobsUseSpawnableMobPrototypes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();

            Assert.That(prototypes.TryIndex<SpeciesPrototype>(WorkingJoeSpecies, out var species), Is.True);
            AssertSpawnableEntity(prototypes, species!.Prototype, WorkingJoeSpecies);

            foreach (var (jobId, expectedEntity) in WorkingJoeJobEntities)
            {
                Assert.That(prototypes.TryIndex<JobPrototype>(jobId, out var job), Is.True, jobId);
                Assert.That(job!.JobEntity, Is.EqualTo(expectedEntity), jobId);
                AssertSpawnableEntity(prototypes, expectedEntity, jobId);
            }
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertSpawnableEntity(IPrototypeManager prototypes, string entityId, string context)
    {
        Assert.That(prototypes.TryIndex<EntityPrototype>(entityId, out var entity), Is.True, context);
        Assert.That(entity!.Abstract, Is.False, $"{context} uses abstract entity {entityId}");
    }
}
