using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Server._RMC14.Discord;

public sealed partial class RMCDiscordSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private RMCDiscordManager _discord = default!;
    [Dependency] private INetConfigurationManager _net = default!;

    private const int Cap = 10;

    public override void Shutdown()
    {
        base.Shutdown();
        _discord.Shutdown();
    }

    public override void Update(float frameTime)
    {
        var messages = _discord.GetDiscordMessages();
        if (messages.Count == 0)
            return;

        var admins = _admin.ActiveAdmins.Select(p => p.Channel).ToArray();
        var i = 0;
        while (messages.TryDequeue(out var msg))
        {
            i++;
            if (msg.Type == RMCDiscordMessageType.Admin)
            {
                var wrappedMessage = Loc.GetString("chat-manager-send-admin-chat-wrap-message",
                    ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                    ("playerName", msg.Author),
                    ("message", FormattedMessage.EscapeText(msg.Message)));

                foreach (var client in admins)
                {
                    _chat.ChatMessageToOne(
                        ChatChannel.AdminChat,
                        msg.Message,
                        wrappedMessage,
                        default,
                        false,
                        client,
                        audioPath: _net.GetClientCVar(client, CCVars.AdminChatSoundPath),
                        audioVolume: _net.GetClientCVar(client, CCVars.AdminChatSoundVolume)
                    );
                }
            }

            if (i >= Cap)
            {
                Log.Error("Max cap reached on discord messages.");
                break;
            }
        }
    }
}
