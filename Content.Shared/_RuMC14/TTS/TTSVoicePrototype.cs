using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Shared.Corvax.TTS;

/// <summary>
/// Prototype represent available TTS voices
/// </summary>
[Prototype("ttsVoice")]
// ReSharper disable once InconsistentNaming
public sealed partial class TTSVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("sex", required: true)]
    public Sex Sex { get; private set; } = default!;

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("speaker", required: true)]
    public string Speaker { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the species is available "at round start" (In the character editor)
    /// </summary>
    [DataField("roundStart")]
    public bool RoundStart { get; private set; } = true;

    [DataField("sponsorOnly")]
    public bool SponsorOnly { get; private set; } = false;

    [DataField("category")]
    public string Category { get; private set; } = "Other";
}

/// <summary>
/// Helpers and shared limits for voices created from a reference recording.
/// </summary>
public static class CustomTTSVoice
{
    public const string Prefix = "custom:";
    public const int MinSpeakerNameLength = 3;
    public const int MaxSpeakerNameLength = 32;
    public const int MaxAudioBytes = 10 * 1024 * 1024;

    public static string CreateVoiceId(string speakerName)
    {
        return $"{Prefix}{speakerName}";
    }

    public static bool TryGetSpeaker(string? voiceId, out string speakerName)
    {
        speakerName = string.Empty;
        if (voiceId == null || !voiceId.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var candidate = voiceId[Prefix.Length..];
        if (!IsValidSpeakerName(candidate))
            return false;

        speakerName = candidate;
        return true;
    }

    public static bool IsValidSpeakerName(string speakerName)
    {
        if (speakerName.Length is < MinSpeakerNameLength or > MaxSpeakerNameLength)
            return false;

        foreach (var character in speakerName)
        {
            var isAsciiLetter = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
            if (!isAsciiLetter && !char.IsDigit(character) && character is not '_' and not '-')
                return false;
        }

        return true;
    }

    public static bool IsValidWaveFile(byte[] audio)
    {
        if (audio.Length < 12 ||
            !MatchesFourCc(audio, 0, 'R', 'I', 'F', 'F') ||
            !MatchesFourCc(audio, 8, 'W', 'A', 'V', 'E'))
            return false;

        var riffSize = ReadUInt32LittleEndian(audio, 4);
        if (riffSize > (uint) (audio.Length - 8))
            return false;

        var riffEnd = (int) riffSize + 8;
        var hasValidFormat = false;
        var hasAudioData = false;
        var offset = 12;
        while (offset <= riffEnd - 8)
        {
            var chunkSize = ReadUInt32LittleEndian(audio, offset + 4);
            offset += 8;
            if (chunkSize > (uint) (riffEnd - offset))
                return false;

            if (MatchesFourCc(audio, offset - 8, 'f', 'm', 't', ' ') && chunkSize >= 16)
            {
                var format = ReadUInt16LittleEndian(audio, offset);
                var channels = ReadUInt16LittleEndian(audio, offset + 2);
                var sampleRate = ReadUInt32LittleEndian(audio, offset + 4);
                var bitsPerSample = ReadUInt16LittleEndian(audio, offset + 14);
                hasValidFormat = format is 1 or 3 &&
                                 channels is >= 1 and <= 2 &&
                                 sampleRate is >= 8_000 and <= 192_000 &&
                                 bitsPerSample is 8 or 16 or 24 or 32;
            }
            else if (MatchesFourCc(audio, offset - 8, 'd', 'a', 't', 'a') && chunkSize > 0)
            {
                hasAudioData = true;
            }

            var paddedSize = (long) chunkSize + (chunkSize & 1);
            if (paddedSize > riffEnd - offset)
                break;
            offset += (int) paddedSize;
        }

        return hasValidFormat && hasAudioData;
    }

    private static bool MatchesFourCc(byte[] data, int offset, char a, char b, char c, char d)
    {
        return data[offset] == a &&
               data[offset + 1] == b &&
               data[offset + 2] == c &&
               data[offset + 3] == d;
    }

    private static ushort ReadUInt16LittleEndian(byte[] data, int offset)
    {
        return (ushort) (data[offset] | data[offset + 1] << 8);
    }

    private static uint ReadUInt32LittleEndian(byte[] data, int offset)
    {
        return (uint) (data[offset] |
                       data[offset + 1] << 8 |
                       data[offset + 2] << 16 |
                       data[offset + 3] << 24);
    }
}
