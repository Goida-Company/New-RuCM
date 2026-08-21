using System;
using System.Collections.Generic;
using Content.Server._RMC14.Discord;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Discord.DiscordLink;
using Content.Server.GameTicking;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Players.RateLimiting;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._RuMC14.Governance;

/// <summary>
/// Extends admin chat to active Governance duty responders without promoting them to administrators.
/// Authorization is derived only from an active current-round duty session while the responder is an observer.
/// </summary>
public sealed class GovernanceAdminChatSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly DiscordChatLink _discordLink = default!;
    [Dependency] private readonly RMCDiscordManager _discord = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GovernanceManager _governance = default!;
    [Dependency] private readonly INetConfigurationManager _netConfig = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private readonly HashSet<NetUserId> _dutyChatAccess = new();
    private float _accessElapsed = float.MaxValue;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accessElapsed += frameTime;
        if (_accessElapsed < 1f)
            return;
        _accessElapsed = 0f;

        var connected = new HashSet<NetUserId>();
        foreach (var session in _players.Sessions)
        {
            if (session.Status is not (SessionStatus.Connected or SessionStatus.InGame))
                continue;

            connected.Add(session.UserId);
            var active = IsDutyResponder(session);
            if (active && _dutyChatAccess.Add(session.UserId))
                RaiseNetworkEvent(new GovernanceAdminChatAccessUpdated(true), session);
            else if (!active && _dutyChatAccess.Remove(session.UserId))
                RaiseNetworkEvent(new GovernanceAdminChatAccessUpdated(false), session);
        }

        // A disconnected client cannot receive a revocation event. Drop it from the cache so a reconnect
        // always receives a fresh authoritative access signal.
        _dutyChatAccess.RemoveWhere(userId => !connected.Contains(userId));
    }

    public void TrySendAdminChat(ICommonSession player, string message)
    {
        if (_chat.HandleRateLimit(player) != RateLimitStatus.Allowed)
            return;

        var maxLength = _config.GetCVar(CCVars.ChatMaxMessageLength);
        if (message.Length > maxLength)
        {
            _chat.DispatchServerMessage(
                player,
                Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", maxLength)));
            return;
        }

        if (!CanUseAdminChat(player))
        {
            _adminLogger.Add(
                LogType.Chat,
                LogImpact.Extreme,
                $"{player:Player} attempted to send admin chat without Adminchat permission or active Governance duty");
            return;
        }

        var wrappedMessage = Loc.GetString(
            "chat-manager-send-admin-chat-wrap-message",
            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
            ("playerName", player.Name),
            ("message", FormattedMessage.EscapeText(message)));

        _discord.SendDiscordAdminMessage(player.Name, message);

        foreach (var recipient in GetRecipients())
        {
            var playSound = recipient.UserId != player.UserId;
            _chat.ChatMessageToOne(
                ChatChannel.AdminChat,
                message,
                wrappedMessage,
                default,
                false,
                recipient.Channel,
                audioPath: playSound ? _netConfig.GetClientCVar(recipient.Channel, CCVars.AdminChatSoundPath) : default,
                audioVolume: playSound ? _netConfig.GetClientCVar(recipient.Channel, CCVars.AdminChatSoundVolume) : default,
                author: player.UserId);
        }

        _discordLink.SendMessage(message, player.Name, ChatChannel.AdminChat);
        _adminLogger.Add(LogType.Chat, $"Admin chat from {player:Player}: {message}");
    }

    public void SendHookAdmin(string sender, string message)
    {
        var wrappedMessage = Loc.GetString(
            "chat-manager-send-hook-admin-wrap-message",
            ("senderName", sender),
            ("message", FormattedMessage.EscapeText(message)));

        foreach (var recipient in GetRecipients())
        {
            _chat.ChatMessageToOne(
                ChatChannel.AdminChat,
                message,
                wrappedMessage,
                default,
                false,
                recipient.Channel,
                recordReplay: false,
                audioPath: _netConfig.GetClientCVar(recipient.Channel, CCVars.AdminChatSoundPath),
                audioVolume: _netConfig.GetClientCVar(recipient.Channel, CCVars.AdminChatSoundVolume));
        }

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Hook admin from {sender}: {message}");
    }

    public bool CanUseAdminChat(ICommonSession player)
    {
        if (_adminManager.IsAdmin(player) && _adminManager.HasAdminFlag(player, AdminFlags.Adminchat))
            return true;

        return IsDutyResponder(player);
    }

    private bool IsDutyResponder(ICommonSession player)
    {
        return _governance.Enabled &&
               _ticker.RunLevel == GameRunLevel.InRound &&
               player.Status is SessionStatus.Connected or SessionStatus.InGame &&
               player.AttachedEntity is { } entity &&
               HasComp<GhostComponent>(entity) &&
               _governance.HasActiveDuty(player.UserId, _ticker.RoundId);
    }

    private IEnumerable<ICommonSession> GetRecipients()
    {
        var seen = new HashSet<NetUserId>();
        foreach (var admin in _adminManager.ActiveAdmins)
        {
            if (_adminManager.HasAdminFlag(admin, AdminFlags.Adminchat) && seen.Add(admin.UserId))
                yield return admin;
        }

        foreach (var session in _players.Sessions)
        {
            if (IsDutyResponder(session) && seen.Add(session.UserId))
                yield return session;
        }
    }
}
