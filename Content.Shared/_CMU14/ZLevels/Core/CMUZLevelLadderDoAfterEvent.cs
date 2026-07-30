using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._CMU14.ZLevels.Core;

[Serializable, NetSerializable]
public sealed partial class CMUZLevelLadderDoAfterEvent : SimpleDoAfterEvent
{
    public readonly int Offset;

    public CMUZLevelLadderDoAfterEvent(int offset)
    {
        Offset = offset;
    }

    public override DoAfterEvent Clone()
    {
        return new CMUZLevelLadderDoAfterEvent(Offset);
    }

    public override bool IsDuplicate(DoAfterEvent other)
    {
        return other is CMUZLevelLadderDoAfterEvent ladder && ladder.Offset == Offset;
    }
}
