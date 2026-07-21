using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Medical.CPR;

[Serializable, NetSerializable]
public sealed partial class CPRDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Raised after a CPR do-after resolves so other systems can observe its result
/// without subscribing to the directed MarineComponent event a second time.
/// </summary>
[ByRefEvent]
public readonly record struct CPRAttemptFinishedEvent(EntityUid Performer, EntityUid Target, bool Success);
