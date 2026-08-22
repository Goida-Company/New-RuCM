using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._RuMC14.Governance;
using Robust.Client.UserInterface;

namespace Content.Client._RuMC14.Governance;

public sealed class GovernanceAdminChatSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GovernanceAdminChatAccessUpdated>(OnAccessUpdated);
    }

    private void OnAccessUpdated(GovernanceAdminChatAccessUpdated message)
    {
        _ui.GetUIController<ChatUIController>().SetGovernanceAdminChatAccess(message.Active);
    }
}
