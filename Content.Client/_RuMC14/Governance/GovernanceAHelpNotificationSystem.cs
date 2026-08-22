using Content.Shared._RuMC14.Governance;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client._RuMC14.Governance;

public sealed class GovernanceAHelpNotificationSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GovernanceAHelpQueueChanged>(OnQueueChanged);
        SubscribeNetworkEvent<GovernanceAHelpResponderReplyReceived>(OnResponderReply);
    }

    private void OnQueueChanged(GovernanceAHelpQueueChanged message, EntitySessionEventArgs args)
    {
        Notify();
    }

    private void OnResponderReply(GovernanceAHelpResponderReplyReceived message, EntitySessionEventArgs args)
    {
        Notify();
    }

    private void Notify()
    {
        var sound = _configuration.GetCVar(CCVars.AHelpSound);
        if (!string.IsNullOrWhiteSpace(sound))
            _audio.PlayGlobal(new ResolvedPathSpecifier(sound), Filter.Local(), false);

        _clyde.RequestWindowAttention();
    }
}
