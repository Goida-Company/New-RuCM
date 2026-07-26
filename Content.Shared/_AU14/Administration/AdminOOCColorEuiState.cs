using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._AU14.Administration;

[Serializable, NetSerializable]
public sealed class AdminOOCColorEuiState : EuiStateBase
{
    public bool IsLoading;
    public List<AdminOOCColorRank> Ranks = new();
}

[Serializable, NetSerializable]
public sealed class AdminOOCColorRank
{
    public int Id;
    public string Name = string.Empty;
    public string? Color;
}

[Serializable, NetSerializable]
public sealed class SetAdminRankOOCColor : EuiMessageBase
{
    public int RankId;
    public string? Color;
}
