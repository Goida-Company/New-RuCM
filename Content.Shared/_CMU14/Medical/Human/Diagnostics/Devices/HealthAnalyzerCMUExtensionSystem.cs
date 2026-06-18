using Content.Shared._CMU14.Medical.Foundation;
using Content.Shared._CMU14.Medical.Human.Components;
using Content.Shared.MedicalScanner;
using Robust.Shared.Configuration;

namespace Content.Shared._CMU14.Medical.Human.Diagnostics;

public sealed partial class HealthAnalyzerCMUExtensionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanMedicalComponent, HealthAnalyzerBuildReadoutEvent>(OnBuildReadout);
    }

    private void OnBuildReadout(Entity<HumanMedicalComponent> ent, ref HealthAnalyzerBuildReadoutEvent args)
    {
        if (!_cfg.GetCVar(CMUMedicalCCVars.Enabled))
            return;
        if (!_cfg.GetCVar(CMUMedicalCCVars.DiagnosticsEnabled))
            return;

        HumanMedicalScannerBuiSystem.BuildHealthAnalyzerDamageReadout(ent.Comp, args.Damage);
    }
}
