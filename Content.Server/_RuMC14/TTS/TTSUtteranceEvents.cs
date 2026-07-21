using Robust.Shared.Network;

namespace Content.Server.Corvax.TTS;

[ByRefEvent]
public readonly record struct TTSUtteranceDispatchedEvent(
    uint PlaybackId,
    IReadOnlySet<NetUserId> Recipients);

[ByRefEvent]
public readonly record struct TTSUtteranceUnavailableEvent;
