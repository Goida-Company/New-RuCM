using System;
using System.Buffers.Binary;
using Content.Shared.Corvax.TTS;
using NUnit.Framework;

namespace Content.Tests.Shared._RMC14;

[TestFixture]
public sealed class CustomTTSVoiceTest
{
    [TestCase("aaron")]
    [TestCase("voice_01")]
    [TestCase("My-Voice")]
    public void ValidSpeakerNamesRoundTripThroughVoiceId(string speakerName)
    {
        var voiceId = CustomTTSVoice.CreateVoiceId(speakerName);

        Assert.That(CustomTTSVoice.TryGetSpeaker(voiceId, out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(speakerName));
    }

    [TestCase("")]
    [TestCase("ab")]
    [TestCase("voice name")]
    [TestCase("голос")]
    [TestCase("voice!")]
    public void InvalidSpeakerNamesAreRejected(string speakerName)
    {
        Assert.That(CustomTTSVoice.IsValidSpeakerName(speakerName), Is.False);
    }

    [Test]
    public void NonCustomVoiceIdIsRejected()
    {
        Assert.That(CustomTTSVoice.TryGetSpeaker("AARON", out _), Is.False);
    }

    [Test]
    public void ValidPcmWaveIsAccepted()
    {
        Assert.That(CustomTTSVoice.IsValidWaveFile(CreatePcmWave()), Is.True);
    }

    [Test]
    public void HeaderOnlyWaveIsRejected()
    {
        var audio = CreatePcmWave()[..12];
        BinaryPrimitives.WriteUInt32LittleEndian(audio.AsSpan(4, 4), 4);

        Assert.That(CustomTTSVoice.IsValidWaveFile(audio), Is.False);
    }

    [Test]
    public void WaveWithChunkOutsideFileIsRejected()
    {
        var audio = CreatePcmWave();
        BinaryPrimitives.WriteUInt32LittleEndian(audio.AsSpan(40, 4), 1024);

        Assert.That(CustomTTSVoice.IsValidWaveFile(audio), Is.False);
    }

    private static byte[] CreatePcmWave()
    {
        var audio = new byte[46];
        "RIFF"u8.CopyTo(audio);
        BinaryPrimitives.WriteUInt32LittleEndian(audio.AsSpan(4, 4), 38);
        "WAVE"u8.CopyTo(audio.AsSpan(8));
        "fmt "u8.CopyTo(audio.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(audio.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(audio.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(audio.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(audio.AsSpan(24, 4), 16_000);
        BinaryPrimitives.WriteUInt32LittleEndian(audio.AsSpan(28, 4), 32_000);
        BinaryPrimitives.WriteUInt16LittleEndian(audio.AsSpan(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(audio.AsSpan(34, 2), 16);
        "data"u8.CopyTo(audio.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(audio.AsSpan(40, 4), 2);
        return audio;
    }
}
