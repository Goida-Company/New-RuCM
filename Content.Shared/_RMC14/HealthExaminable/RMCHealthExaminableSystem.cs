using Content.Shared._CMU14.Medical.Foundation;
using Content.Shared._CMU14.Medical.Human.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.HealthExaminable;

public sealed partial class RMCHealthExaminableSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly ProtoId<DamageGroupPrototype> BruteGroup = "Brute";
    private static readonly ProtoId<DamageGroupPrototype> BurnGroup = "Burn";

    private static readonly FixedPoint2[] Thresholds = new FixedPoint2[]
    {
        FixedPoint2.New(25),
        FixedPoint2.New(50),
        FixedPoint2.New(75),
        FixedPoint2.New(100),
        FixedPoint2.New(200),
        FixedPoint2.New(300),
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<RMCHealthExaminableComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<RMCHealthExaminableComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.SpeciesType == null)
            return;

        if (_cfg.GetCVar(CMUMedicalCCVars.Enabled) &&
            HasComp<HumanMedicalComponent>(ent))
        {
            return;
        }

        if (!TryComp(ent, out DamageableComponent? damageable))
            return;

        using (args.PushGroup(nameof(RMCHealthExaminableSystem), -1))
        {
            foreach (var group in ent.Comp.Groups)
            {
                if (!damageable.DamagePerGroup.TryGetValue(group, out var groupDamage))
                    continue;

                for (var i = Thresholds.Length - 1; i >= 0; i--)
                {
                    var threshold = Thresholds[i];
                    if (groupDamage < threshold)
                        continue;

                    var id = $"rmc-health-examinable-{ent.Comp.SpeciesType}-{group}-{threshold.Int()}";
                    if (!Loc.TryGetString(id, out var msg, ("target", Identity.Entity(ent, EntityManager, args.Examiner))))
                        continue;

                    args.PushMarkup(msg);
                    break;
                }
            }
        }
    }
}
