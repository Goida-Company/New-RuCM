using Content.Server.Body.Systems;
using Content.Shared._CMU14.Medical.Human.Components;
using Content.Shared._CMU14.Medical.Human.Data;
using Content.Shared._CMU14.Medical.Human.Systems;
using Content.Shared.Body.Components;
using Content.Shared.FixedPoint;

namespace Content.Server._CMU14.Medical.Human.Systems;

public sealed partial class HumanBleedingBloodstreamSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanBleedingTickEvent>(OnBleedingTick);
        SubscribeLocalEvent<HumanMedicalLedgerChangedEvent>(OnLedgerChanged);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<HumanMedicalComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var medical, out _))
        {
            var tick = HumanBleedingSystem.CalculateBleedingTick(medical);
            ReconcileBleedAmount(uid, tick.TotalRate);
        }
    }

    private void OnBleedingTick(ref HumanBleedingTickEvent args)
    {
        ReconcileBleedAmount(args.Body, args.TotalRate);
    }

    private void OnLedgerChanged(ref HumanMedicalLedgerChangedEvent args)
    {
        if (!args.Result.DirtyFlags.HasFlag(MedicalDirtyFlags.Bleeding) ||
            !TryComp<HumanMedicalComponent>(args.Body, out var medical))
        {
            return;
        }

        var tick = HumanBleedingSystem.CalculateBleedingTick(medical);
        ReconcileBleedAmount(args.Body, tick.TotalRate);
    }

    private void ReconcileBleedAmount(EntityUid body, FixedPoint2 ledgerRate)
    {
        if (!TryComp<BloodstreamComponent>(body, out var bloodstream))
            return;

        var desired = GetDesiredBleedAmount(ledgerRate, bloodstream.MaxBleedAmount);
        if (MathF.Abs(desired - bloodstream.BleedAmount) < 0.001f)
            return;

        _bloodstream.TrySetBleedAmount((body, bloodstream), desired);
    }

    public static float GetDesiredBleedAmount(FixedPoint2 ledgerRate, float maxBleedAmount)
    {
        return Math.Clamp(ledgerRate.Float(), 0f, maxBleedAmount);
    }
}
