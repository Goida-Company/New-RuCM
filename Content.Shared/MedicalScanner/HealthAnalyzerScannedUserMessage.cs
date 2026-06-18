using System.Collections.Generic;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.MedicalScanner;

[Serializable, NetSerializable]
public sealed class HealthAnalyzerDamageReadout
{
    public FixedPoint2 Total;
    public Dictionary<string, FixedPoint2> DamagePerGroup = new();
    public Dictionary<string, FixedPoint2> DamagePerType = new();
}

[ByRefEvent]
public record struct HealthAnalyzerBuildReadoutEvent(
    EntityUid Analyzer,
    EntityUid Target,
    HealthAnalyzerDamageReadout Damage);

/// <summary>
///     On interacting with an entity retrieves the entity UID and the server-side medical readout for the mob.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerScannedUserMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity? TargetEntity;
    public HealthAnalyzerDamageReadout? Damage;
    public float Temperature;
    public float BloodLevel;
    public bool? ScanMode;
    public bool? Bleeding;
    public bool? Unrevivable;

    public HealthAnalyzerScannedUserMessage(
        NetEntity? targetEntity,
        float temperature,
        float bloodLevel,
        bool? scanMode,
        bool? bleeding,
        bool? unrevivable,
        HealthAnalyzerDamageReadout? damage = null)
    {
        TargetEntity = targetEntity;
        Damage = damage;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        ScanMode = scanMode;
        Bleeding = bleeding;
        Unrevivable = unrevivable;
    }
}
