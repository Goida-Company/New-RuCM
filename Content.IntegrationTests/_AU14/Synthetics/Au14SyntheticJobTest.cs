using System.Collections.Generic;
using System.Linq;
using Content.Server.Jobs;
using Content.Shared._CMU14.Medical.Human.Components;
using Content.Shared._CMU14.Medical.Human.Organs.Heart;
using Content.Shared._CMU14.Medical.Human.Organs.Lungs;
using Content.Shared._RMC14.Synth;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests._AU14.Synthetics;

[TestFixture]
public sealed class Au14SyntheticJobTest
{
    private static readonly string[] SyntheticJobs =
    {
        "AU14JobGOVFORAuxSupportSynth",
        "AU14JobGOVFORAuxSupportSynthRMC",
        "AU14JobGOVFORAuxSupportSynthUPP",
        "AU14JobOPFORAuxSupportSynth",
        "AU14JobCivilianColonySynthetic",
    };

    [Test]
    public async Task SyntheticJobsApplySynthSpecial()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var missing = new List<string>();

            foreach (var jobId in SyntheticJobs)
            {
                var job = prototypes.Index<JobPrototype>(jobId);
                var hasSynth = GetAppliedAddComponentSpecials(prototypes, job)
                    .Any(special => special.Components.ContainsKey("Synth"));

                if (!hasSynth)
                    missing.Add(jobId);
            }

            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SyntheticJobSpecialsDoNotLeaveRemovedOrganFailureState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var spawned = new List<(string JobId, EntityUid Human)>();

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var prototypes = server.ResolveDependency<IPrototypeManager>();

            foreach (var jobId in SyntheticJobs)
            {
                var human = entMan.SpawnEntity("CMMobHuman", MapCoordinates.Nullspace);

                Assert.That(entMan.HasComponent<HumanMedicalComponent>(human), Is.True, jobId);

                var job = prototypes.Index<JobPrototype>(jobId);
                foreach (var special in GetAppliedAddComponentSpecials(prototypes, job))
                    special.AfterEquip(human);

                spawned.Add((jobId, human));
            }
        });

        await pair.RunTicksSync(pair.SecondsToTicks(3));

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            foreach (var (jobId, human) in spawned)
            {
                var mob = entMan.GetComponent<MobStateComponent>(human);
                var damageable = entMan.GetComponent<DamageableComponent>(human);

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.HasComponent<SynthComponent>(human), Is.True, jobId);
                    Assert.That(entMan.HasComponent<HumanMedicalComponent>(human), Is.False, jobId);
                    Assert.That(entMan.HasComponent<MissingHeartComponent>(human), Is.False, jobId);
                    Assert.That(entMan.HasComponent<MissingLungsComponent>(human), Is.False, jobId);
                    Assert.That(
                        damageable.Damage.DamageDict.GetValueOrDefault("Asphyxiation"),
                        Is.EqualTo(FixedPoint2.Zero),
                        jobId);
                    Assert.That(mob.CurrentState, Is.Not.EqualTo(MobState.Dead), jobId);
                });

                entMan.DeleteEntity(human);
            }
        });

        await pair.CleanReturnAsync();
    }

    private static List<AddComponentSpecial> GetAppliedAddComponentSpecials(IPrototypeManager prototypes, JobPrototype job)
    {
        if (!job.InheritAddComponentSpecials)
            return job.Special.OfType<AddComponentSpecial>().ToList();

        var results = new List<AddComponentSpecial>();
        AddInheritedAddComponentSpecials(prototypes, job, results);
        return results;
    }

    private static void AddInheritedAddComponentSpecials(
        IPrototypeManager prototypes,
        JobPrototype job,
        List<AddComponentSpecial> results)
    {
        if (job.Parents is { Length: > 0 })
        {
            foreach (var parentId in job.Parents)
            {
                if (prototypes.TryIndex<JobPrototype>(parentId, out var parent))
                    AddInheritedAddComponentSpecials(prototypes, parent, results);
            }
        }

        results.AddRange(job.Special.OfType<AddComponentSpecial>());
    }
}
