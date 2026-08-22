using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.Corvax.CCCVars;

/// <summary>
///     Corvax and RUMC modules console variables
/// </summary>
[CVarDefs]
// ReSharper disable once InconsistentNaming
public sealed class CCCVars : CVars
{
    /// <summary>
    /// Deny any VPN connections.
    /// </summary>
    public static readonly CVarDef<bool> PanicBunkerDenyVPN =
        CVarDef.Create("game.panic_bunker.deny_vpn", false, CVar.SERVERONLY);

    /**
     * TTS (Text-To-Speech)
     */

    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<bool> TTSEnabled =
        CVarDef.Create("tts.enabled", true, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiUrl =
        CVarDef.Create("tts.api_url", "", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Auth token of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiToken =
        CVarDef.Create("tts.api_token", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Amount of seconds before timeout for API
    /// </summary>
    public static readonly CVarDef<int> TTSApiTimeout =
        CVarDef.Create("tts.api_timeout", 5, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Amount of seconds before a reference voice upload times out.
    /// Voice creation is expected to take longer than regular synthesis.
    /// </summary>
    public static readonly CVarDef<int> TTSReferenceVoiceApiTimeout =
        CVarDef.Create("tts.reference_voice_api_timeout", 60, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Restricts reference voice creation to players with an active donation.
    /// Disabled by default until the patron integration is ready for production use.
    /// </summary>
    public static readonly CVarDef<bool> TTSReferenceVoiceDonorOnly =
        CVarDef.Create("tts.reference_voice_donor_only", false, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of TTS sound
    /// </summary>
    public static readonly CVarDef<float> TTSVolume =
        CVarDef.Create("tts.volume", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Count of in-memory cached tts voice lines.
    /// </summary>
    public static readonly CVarDef<int> TTSMaxCache =
        CVarDef.Create("tts.max_cache", 250, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Tts rate limit values are accounted in periods of this size (seconds).
    /// After the period has passed, the count resets.
    /// </summary>
    public static readonly CVarDef<float> TTSRateLimitPeriod =
        CVarDef.Create("tts.rate_limit_period", 2f, CVar.SERVERONLY);

    /// <summary>
    /// How many tts preview messages are allowed in a single rate limit period.
    /// </summary>
    public static readonly CVarDef<int> TTSRateLimitCount =
        CVarDef.Create("tts.rate_limit_count", 3, CVar.SERVERONLY);

    /*
     * Peaceful Round End
     */

    /// <summary>
    /// Making everyone a pacifist at the end of a round.
    /// </summary>
    public static readonly CVarDef<bool> PeacefulRoundEnd =
        CVarDef.Create("game.peaceful_end", false, CVar.SERVERONLY);

    /*
     * Station Goal
     */

    /// <summary>
    /// Send station goal on round start or not.
    /// </summary>
    public static readonly CVarDef<bool> StationGoal =
        CVarDef.Create("game.station_goal", true, CVar.SERVERONLY);

#region RUMC
    /// <summary>
    /// Auth token of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> PlaytimeApiToken =
        CVarDef.Create("playtime.api_token", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);
    public static readonly CVarDef<string> PlaytimeApiAllowedIP =
        CVarDef.Create("playtime.allowed_ip", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Enables the in-game RUCM Community Governance enforcement boundary.
    /// Requires the main database engine to be PostgreSQL and the governance schema to be installed.
    /// </summary>
    public static readonly CVarDef<bool> GovernanceEnabled =
        CVarDef.Create("governance.enabled", false, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Enables physical execution of Event Governance actions on the game server.
    /// Intentionally disabled by default while the event workflow is deferred from production acceptance.
    /// </summary>
    public static readonly CVarDef<bool> GovernanceEventEnabled =
        CVarDef.Create("governance.event_enabled", false, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Maximum duration of a temporary governance freeze.
    /// </summary>
    public static readonly CVarDef<int> GovernanceFreezeMaxSeconds =
        CVarDef.Create("governance.freeze_max_seconds", 120, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Number of simultaneous community responders requested for an active round.
    /// </summary>
    public static readonly CVarDef<int> GovernanceDutyTargetResponders =
        CVarDef.Create("governance.duty_target_responders", 1, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Interval between server-side duty staffing and open AHelp checks.
    /// Kept short because this is also the responder notification latency.
    /// </summary>
    public static readonly CVarDef<int> GovernanceDutyCheckSeconds =
        CVarDef.Create("governance.duty_check_seconds", 10, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Time an in-game duty invitation remains open.
    /// </summary>
    public static readonly CVarDef<int> GovernanceDutyInviteSeconds =
        CVarDef.Create("governance.duty_invite_seconds", 90, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Maximum lifetime of an accepted duty session. Round end always closes it sooner.
    /// </summary>
    public static readonly CVarDef<int> GovernanceDutySessionMinutes =
        CVarDef.Create("governance.duty_session_minutes", 240, CVar.SERVERONLY | CVar.ARCHIVE);

    // Legacy compatibility only. Reputation v2 deliberately treats invitation accept/decline/expiry
    // as neutral, and the server always passes zero to the old ledger API.
    public static readonly CVarDef<int> GovernanceDutyAcceptReward =
        CVarDef.Create("governance.duty_accept_reward", 0, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> GovernanceDutyDeclinePenalty =
        CVarDef.Create("governance.duty_decline_penalty", 0, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<int> GovernanceDutyExpiryPenalty =
        CVarDef.Create("governance.duty_expiry_penalty", 0, CVar.SERVERONLY | CVar.ARCHIVE);

#endregion
}
