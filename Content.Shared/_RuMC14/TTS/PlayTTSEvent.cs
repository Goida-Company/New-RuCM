using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.TTS;

[Serializable, NetSerializable]
// ReSharper disable once InconsistentNaming
public sealed class PlayTTSEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public NetEntity? SourceUid { get; }
    public bool IsWhisper { get; }

    public bool IsRadio { get; }
    public uint PlaybackId { get; }

    public PlayTTSEvent(
        byte[] data,
        NetEntity? sourceUid = null,
        bool isWhisper = false,
        bool isRadio = false,
        uint playbackId = 0)
    {
        Data = data;
        SourceUid = sourceUid;
        IsWhisper = isWhisper;
        IsRadio = isRadio;
        PlaybackId = playbackId;
    }
}

[Serializable, NetSerializable]
public sealed class TTSPlaybackFinishedEvent(uint playbackId, NetEntity? sourceUid, bool played) : EntityEventArgs
{
    public uint PlaybackId { get; } = playbackId;
    public NetEntity? SourceUid { get; } = sourceUid;
    public bool Played { get; } = played;
}
