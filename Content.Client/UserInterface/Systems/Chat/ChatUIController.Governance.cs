using Content.Shared.Chat;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    private bool _governanceAdminChatAccess;
    private bool _governancePermissionHooksInitialized;
    private bool _applyingGovernancePermissions;

    public void SetGovernanceAdminChatAccess(bool active)
    {
        EnsureGovernancePermissionHooks();

        if (_governanceAdminChatAccess == active)
        {
            if (active)
                ApplyGovernanceAdminChatAccess();
            return;
        }

        _governanceAdminChatAccess = active;
        if (active)
            ApplyGovernanceAdminChatAccess();
        else
            UpdateChannelPermissions();
    }

    private void EnsureGovernancePermissionHooks()
    {
        if (_governancePermissionHooksInitialized)
            return;

        _governancePermissionHooksInitialized = true;
        CanSendChannelsChanged += _ => ApplyGovernanceAdminChatAccess();
        FilterableChannelsChanged += _ => ApplyGovernanceAdminChatAccess();
        SelectableChannelsChanged += _ => ApplyGovernanceAdminChatAccess();
    }

    private void ApplyGovernanceAdminChatAccess()
    {
        if (!_governanceAdminChatAccess || _applyingGovernancePermissions)
            return;

        var canSend = CanSendChannels | ChatSelectChannel.Admin;
        var filterable = FilterableChannels | ChatChannel.Admin | ChatChannel.AdminAlert | ChatChannel.AdminChat;
        var selectable = SelectableChannels | ChatSelectChannel.Admin;

        var canSendChanged = canSend != CanSendChannels;
        var filterableChanged = filterable != FilterableChannels;
        var selectableChanged = selectable != SelectableChannels;
        if (!canSendChanged && !filterableChanged && !selectableChanged)
            return;

        _applyingGovernancePermissions = true;
        try
        {
            CanSendChannels = canSend;
            FilterableChannels = filterable;
            SelectableChannels = selectable;

            if (canSendChanged)
                CanSendChannelsChanged?.Invoke(CanSendChannels);
            if (filterableChanged)
                FilterableChannelsChanged?.Invoke(FilterableChannels);
            if (selectableChanged)
                SelectableChannelsChanged?.Invoke(SelectableChannels);
        }
        finally
        {
            _applyingGovernancePermissions = false;
        }
    }
}
