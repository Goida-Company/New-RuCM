using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Robust.Shared.Localization; // RuMC edit

namespace Content.Server.Discord;

public enum RoundStatusWebhookKind
{
    Starting,
    Lobby,
    Running,
    Ended,
    Shutdown,
}

public readonly record struct RoundStatusWebhookColors(
    int Starting,
    int Running,
    int Ended,
    int Shutdown);

public readonly record struct RoundStatusWebhookData(
    int RoundId,
    int PlayerCount,
    string MapName,
    string Govfor,
    string Gamemode,
    IReadOnlyList<RoundStatusRecentGamemode> RecentGamemodes,
    TimeSpan? Duration = null);

public readonly record struct RoundStatusRecentGamemode(int RoundId, string Gamemode, TimeSpan Duration);

public readonly record struct RoundStatusWebhookMessageIds(
    ulong StatusMessageId,
    ulong RoundEndPingMessageId,
    ulong GamemodeVotePingMessageId);

public static class RoundStatusWebhook
{
    private const int DetailValueLength = 96;
    private const int RecentGamemodeLength = 72;

    public static readonly RoundStatusWebhookColors DefaultColors = new(
        0xF0C419,
        0x23EB49,
        0xCD1010,
        0x6B7280);

    private static readonly JsonSerializerOptions MessageIdsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static WebhookPayload CreatePayload(
        RoundStatusWebhookKind kind,
        RoundStatusWebhookData status,
        IEnumerable<string?> roleIds,
        ILocalizationManager loc, // RuMC edit
        RoundStatusWebhookColors? colors = null)
    {
        colors ??= DefaultColors;
        var content = BuildRoleMentions(roleIds);

        if (kind == RoundStatusWebhookKind.Shutdown)
            return CreateOfflinePayload(content, loc, colors.Value); // RuMC edit

        var fields = new List<WebhookEmbedField>
        {
            // RuMC edit start
            new() { Name = loc.GetString("discord-round-status-field-status"), Value = GetState(loc, kind), Inline = true },
            new() { Name = loc.GetString("discord-round-status-field-players"), Value = status.PlayerCount.ToString(CultureInfo.InvariantCulture), Inline = true },
            new() { Name = loc.GetString("discord-round-status-field-round"), Value = $"#{status.RoundId}", Inline = true },
            // RuMC edit end
        };

        if (status.Duration is { } duration)
        // RuMC edit start
            fields.Add(new WebhookEmbedField { Name = loc.GetString("discord-round-status-field-runtime"), Value = FormatDuration(loc, duration), Inline = true });

        fields.Add(new WebhookEmbedField { Name = loc.GetString("discord-round-status-field-operation"), Value = FormatOperation(loc, status), Inline = false });
        fields.Add(new WebhookEmbedField { Name = loc.GetString("discord-round-status-field-recent-rounds"), Value = FormatRecentGamemodes(loc, status.RecentGamemodes), Inline = false });
        fields.Add(CreateLastUpdatedField(loc, DateTimeOffset.UtcNow));
        // RuMC edit end

        var payload = new WebhookPayload
        {
            Content = content,
            Embeds = new List<WebhookEmbed>
            {
                new()
                {
                    // RuMC edit start
                    Title = GetTitle(loc, kind, status.RoundId),
                    Description = GetDescription(loc, kind),
                    // RuMC edit end
                    Color = GetColor(kind, colors.Value),
                    Footer = new WebhookEmbedFooter { Text = loc.GetString("discord-round-status-footer") }, // RuMC edit
                    Fields = fields,
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(content))
            payload.AllowedMentions.AllowRoleMentions();

        return payload;
    }

    private static WebhookPayload CreateOfflinePayload(string content, ILocalizationManager loc, RoundStatusWebhookColors colors) // RuMC edit
    {
        var payload = new WebhookPayload
        {
            Content = content,
            Embeds = new List<WebhookEmbed>
            {
                new()
                {
                    // RuMC edit start
                    Title = GetTitle(loc, RoundStatusWebhookKind.Shutdown, 0),
                    Description = GetDescription(loc, RoundStatusWebhookKind.Shutdown),
                    // RuMC edit end
                    Color = colors.Shutdown,
                    Footer = new WebhookEmbedFooter { Text = loc.GetString("discord-round-status-footer") }, // RuMC edit
                    Fields = new List<WebhookEmbedField>
                    {
                        new() { Name = loc.GetString("discord-round-status-field-status"), Value = GetState(loc, RoundStatusWebhookKind.Shutdown), Inline = true }, // RuMC edit
                    },
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(content))
            payload.AllowedMentions.AllowRoleMentions();

        return payload;
    }

    public static WebhookPayload CreateRolePingPayload(IEnumerable<string?> roleIds, string? message = null)
    {
        var content = BuildRoleMentions(roleIds);
        if (!string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(message))
            content = $"{content} {message.Trim()}";

        var payload = new WebhookPayload
        {
            Content = content,
        };

        if (!string.IsNullOrWhiteSpace(content))
            payload.AllowedMentions.AllowRoleMentions();

        return payload;
    }

    public static string SerializeMessageIds(RoundStatusWebhookMessageIds messageIds)
    {
        return JsonSerializer.Serialize(messageIds, MessageIdsJsonOptions);
    }

    public static bool TryDeserializeMessageIds(string? json, out RoundStatusWebhookMessageIds messageIds)
    {
        messageIds = default;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            messageIds = JsonSerializer.Deserialize<RoundStatusWebhookMessageIds>(
                json,
                MessageIdsJsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static int ParseColor(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var color = value.Trim().TrimStart('#');
        if (color.Length != 6)
            return fallback;

        return int.TryParse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public static string? GetGamemodeRole(
        string? presetId,
        string? distressSignalRole,
        string? colonyFallRole,
        string? insurgencyRole)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            return null;

        if (presetId.Equals("DistressSignal", StringComparison.OrdinalIgnoreCase))
            return NullIfEmpty(distressSignalRole);

        if (presetId.Equals("ColonyFall", StringComparison.OrdinalIgnoreCase))
            return NullIfEmpty(colonyFallRole);

        if (presetId.Equals("Insurgency", StringComparison.OrdinalIgnoreCase))
            return NullIfEmpty(insurgencyRole);

        return null;
    }

    public static IEnumerable<string> GetRoundStatusRoleIds(
        bool includeRoundEndRole,
        string? presetId,
        string? roundEndRole,
        string? distressSignalRole,
        string? colonyFallRole,
        string? insurgencyRole)
    {
        if (includeRoundEndRole && NullIfEmpty(roundEndRole) is { } endRole)
            yield return endRole;

        if (GetGamemodeRole(presetId, distressSignalRole, colonyFallRole, insurgencyRole) is { } gamemodeRole)
            yield return gamemodeRole;
    }

    public static bool TryGetMessageId(string? responseContent, out ulong messageId)
    {
        messageId = 0;

        if (string.IsNullOrWhiteSpace(responseContent))
            return false;

        try
        {
            var id = JsonNode.Parse(responseContent)?["id"]?.GetValue<string>();
            return ulong.TryParse(id, out messageId);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetMessageIdToDelete(ulong previousMessageId, ulong newMessageId, out ulong messageId)
    {
        messageId = 0;

        if (previousMessageId == 0 || previousMessageId == newMessageId)
            return false;

        messageId = previousMessageId;
        return true;
    }

    public static bool ShouldUpdate(TimeSpan now, TimeSpan nextUpdate, TimeSpan interval, bool hasStatusMessage)
    {
        return hasStatusMessage &&
               interval > TimeSpan.Zero &&
               now >= nextUpdate;
    }

    private static string BuildRoleMentions(IEnumerable<string?> roleIds)
    {
        return string.Join(
            " ",
            roleIds
                .Where(roleId => !string.IsNullOrWhiteSpace(roleId))
                .Distinct(StringComparer.Ordinal)
                .Select(roleId => $"<@&{roleId}>"));
    }

    private static WebhookEmbedField CreateLastUpdatedField(ILocalizationManager loc, DateTimeOffset updatedAt) // RuMC edit
    {
        return new WebhookEmbedField
        {
            Name = loc.GetString("discord-round-status-field-last-updated"), // RuMC edit
            Value = $"<t:{updatedAt.ToUnixTimeSeconds()}:R>",
            Inline = false,
        };
    }

    private static string GetTitle(ILocalizationManager loc, RoundStatusWebhookKind kind, int roundId)
    {
        return kind switch
        {
            // RuMC edit start
            RoundStatusWebhookKind.Starting => loc.GetString("discord-round-status-title-starting"),
            RoundStatusWebhookKind.Lobby => loc.GetString("discord-round-status-title-lobby"),
            RoundStatusWebhookKind.Running => loc.GetString("discord-round-status-title-running", ("id", roundId.ToString(CultureInfo.InvariantCulture))),
            RoundStatusWebhookKind.Ended => loc.GetString("discord-round-status-title-ended", ("id", roundId.ToString(CultureInfo.InvariantCulture))),
            RoundStatusWebhookKind.Shutdown => loc.GetString("discord-round-status-title-offline"),
            _ => loc.GetString("discord-round-status-title-unknown"),
            // RuMC edit end
        };
    }

    private static int GetColor(RoundStatusWebhookKind kind, RoundStatusWebhookColors colors)
    {
        return kind switch
        {
            RoundStatusWebhookKind.Starting => colors.Starting,
            RoundStatusWebhookKind.Lobby => colors.Starting,
            RoundStatusWebhookKind.Running => colors.Running,
            RoundStatusWebhookKind.Ended => colors.Ended,
            RoundStatusWebhookKind.Shutdown => colors.Shutdown,
            _ => colors.Running,
        };
    }

    private static string GetDescription(ILocalizationManager loc, RoundStatusWebhookKind kind)
    {
        return kind switch
        {
            // RuMC edit start
            RoundStatusWebhookKind.Starting => loc.GetString("discord-round-status-description-starting"),
            RoundStatusWebhookKind.Lobby => loc.GetString("discord-round-status-description-lobby"),
            RoundStatusWebhookKind.Running => loc.GetString("discord-round-status-description-running"),
            RoundStatusWebhookKind.Ended => loc.GetString("discord-round-status-description-ended"),
            RoundStatusWebhookKind.Shutdown => loc.GetString("discord-round-status-description-offline"),
            _ => loc.GetString("discord-round-status-description-unknown"),
            // RuMC edit end
        };
    }

    private static string GetState(ILocalizationManager loc, RoundStatusWebhookKind kind) // RuMC edit
    {
        return kind switch
        {
            // RuMC edit start
            RoundStatusWebhookKind.Starting => loc.GetString("discord-round-status-state-starting"),
            RoundStatusWebhookKind.Lobby => loc.GetString("discord-round-status-state-lobby"),
            RoundStatusWebhookKind.Running => loc.GetString("discord-round-status-state-running"),
            RoundStatusWebhookKind.Ended => loc.GetString("discord-round-status-state-ended"),
            RoundStatusWebhookKind.Shutdown => loc.GetString("discord-round-status-state-offline"),
            _ => loc.GetString("discord-round-status-state-unknown"),
            // RuMC edit end
        };
    }

    private static string FormatOperation(ILocalizationManager loc, RoundStatusWebhookData status) // RuMC edit
    {
        return string.Join(
            "\n",
            // RuMC edit start
            loc.GetString("discord-round-status-operation-map", ("value", Shorten(loc, status.MapName, DetailValueLength))),
            loc.GetString("discord-round-status-operation-govfor", ("value", Shorten(loc, status.Govfor, DetailValueLength))),
            loc.GetString("discord-round-status-operation-mode", ("value", Shorten(loc, status.Gamemode, DetailValueLength))));
            // RuMC edit end
    }

    private static string FormatRecentGamemodes(ILocalizationManager loc, IReadOnlyList<RoundStatusRecentGamemode> recentGamemodes) // RuMC edit
    {
        if (recentGamemodes.Count == 0)
            return loc.GetString("discord-round-status-no-recent-rounds");

        return string.Join(
            "\n",
            recentGamemodes
                .Take(3)
                .Select(round => $"`#{round.RoundId}` {Shorten(loc, round.Gamemode, RecentGamemodeLength)} - {FormatShortDuration(loc, round.Duration)}")); // RuMC edit
    }

    private static string UnknownIfEmpty(ILocalizationManager loc, string value) // RuMC edit
    {
        return string.IsNullOrWhiteSpace(value)
            ? loc.GetString("discord-round-status-unknown-value") // RuMC edit
            : value.Trim();
    }

    private static string Shorten(ILocalizationManager loc, string value, int maxLength) // RuMC edit
    {
        value = UnknownIfEmpty(loc, value) // RuMC edit
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        while (value.Contains("  ", StringComparison.Ordinal))
        {
            value = value.Replace("  ", " ");
        }

        if (value.Length <= maxLength)
            return value;

        return maxLength <= 3
            ? value[..maxLength]
            : $"{value[..(maxLength - 3)].TrimEnd()}...";
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static string FormatDuration(ILocalizationManager loc, TimeSpan duration) // RuMC edit
    {
        // RuMC edit start
        return loc.GetString(
            "discord-round-status-duration-long",
            ("hours", ((int) duration.TotalHours).ToString(CultureInfo.InvariantCulture)),
            ("minutes", duration.Minutes.ToString(CultureInfo.InvariantCulture)),
            ("seconds", duration.Seconds.ToString(CultureInfo.InvariantCulture)));
        // RuMC edit end
    }

    private static string FormatShortDuration(ILocalizationManager loc, TimeSpan duration) // RuMC edit
    {
        return duration.TotalHours >= 1
        // RuMC edit start
            ? loc.GetString(
                "discord-round-status-duration-short-hours",
                ("hours", ((int) duration.TotalHours).ToString(CultureInfo.InvariantCulture)),
                ("minutes", duration.Minutes.ToString("D2", CultureInfo.InvariantCulture)))
            : loc.GetString(
                "discord-round-status-duration-short-minutes",
                ("minutes", duration.Minutes.ToString(CultureInfo.InvariantCulture)),
                ("seconds", duration.Seconds.ToString("D2", CultureInfo.InvariantCulture)));
        // RuMC edit end
    }
}
