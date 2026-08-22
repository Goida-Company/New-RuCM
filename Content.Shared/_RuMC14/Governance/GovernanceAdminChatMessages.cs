using Robust.Shared.Serialization;

namespace Content.Shared._RuMC14.Governance;

/// <summary>
/// Server-authoritative signal that exposes the admin-chat channel in the client UI for an active duty responder.
/// It does not grant any Robust admin flags or other administrative permissions.
/// </summary>
[Serializable, NetSerializable]
public sealed class GovernanceAdminChatAccessUpdated(bool active) : EntityEventArgs
{
    public readonly bool Active = active;
}
