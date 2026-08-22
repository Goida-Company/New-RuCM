using Content.Shared._RuMC14.Governance;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client._RuMC14.Governance;

/// <summary>
/// Client entrypoint for the native Governance support center.
/// Keeps player/responder notifications out of the legacy Bwoink UI.
/// </summary>
public sealed class GovernanceAHelpClientSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GovernanceAHelpPlayerReplyReceived>(OnPlayerReply);
    }

    public void RequestOpen()
    {
        RaiseNetworkEvent(new GovernanceAHelpOpenRequest());
    }

    private void OnPlayerReply(GovernanceAHelpPlayerReplyReceived message, EntitySessionEventArgs args)
    {
        var sound = _configuration.GetCVar(CCVars.AHelpSound);
        if (!string.IsNullOrWhiteSpace(sound))
            _audio.PlayGlobal(new ResolvedPathSpecifier(sound), Filter.Local(), false);

        _clyde.RequestWindowAttention();
    }
}
