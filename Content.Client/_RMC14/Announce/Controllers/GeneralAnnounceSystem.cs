using Content.Shared._RMC14.Announce;
using Content.Shared._RMC14.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using System.Collections.Generic;

namespace Content.Client._RMC14.Announce;

public sealed partial class GeneralAnnounceSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private AnnouncementDisplayPreference _preference;
    private Dictionary<string, AnnouncementDisplayPreference> _overrides = new();
    private bool _playerAttached;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(RMCCVars.RMCAnnouncementStyle, OnPreferenceChanged, true);
        _cfg.OnValueChanged(RMCCVars.RMCAnnouncementStyleOverrides, OnOverridesChanged, true);
        SubscribeNetworkEvent<AnnouncementNetMessage>(OnAnnouncementMessage);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        _playerAttached = true;
        SendPreferenceUpdate();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent ev)
    {
        _playerAttached = false;
    }

    private void OnAnnouncementMessage(AnnouncementNetMessage msg, EntitySessionEventArgs args)
    {
        if (_preference == AnnouncementDisplayPreference.Disabled)
            return;

        if (_uiManager.GetUIController<GeneralAnnounceUIController>() is { } controller)
        {
            controller.ShowAnnouncement(msg.Data);
        }
    }

    private void OnPreferenceChanged(AnnouncementDisplayPreference preference)
    {
        _preference = preference;
        SendPreferenceUpdate();
    }

    private void OnOverridesChanged(string serializedOverrides)
    {
        _overrides = AnnouncementPreferenceOverrides.Parse(serializedOverrides);
        SendPreferenceUpdate();
    }

    private void SendPreferenceUpdate()
    {
        // CVar callbacks are invoked immediately during EntitySystem initialization, before the
        // client's network session is ready. Cache those values locally and synchronize once the
        // local player is attached instead of attempting to send during startup.
        if (!_playerAttached)
            return;

        RaiseNetworkEvent(new AnnouncementPreferenceNetMessage(
            _preference,
            new Dictionary<string, AnnouncementDisplayPreference>(_overrides)));
    }
}
