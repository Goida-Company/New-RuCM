using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.TTS;

[Serializable, NetSerializable]
public sealed class AddReferenceVoiceRequest(string speakerName, byte[] audio) : EntityEventArgs
{
    public string SpeakerName { get; } = speakerName;
    public byte[] Audio { get; } = audio;
}

[Serializable, NetSerializable]
public sealed class AddReferenceVoiceResponse(string speakerName, AddReferenceVoiceResult result) : EntityEventArgs
{
    public string SpeakerName { get; } = speakerName;
    public AddReferenceVoiceResult Result { get; } = result;
}

[Serializable, NetSerializable]
public enum AddReferenceVoiceResult : byte
{
    Success,
    Disabled,
    NotDonor,
    InvalidName,
    InvalidAudio,
    FileTooLarge,
    AlreadyExists,
    RateLimited,
    ApiError,
}

[Serializable, NetSerializable]
public sealed class ReferenceVoiceCatalogRequest : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class ReferenceVoiceCatalogResponse(string[] speakerNames) : EntityEventArgs
{
    public string[] SpeakerNames { get; } = speakerNames;
}

[Serializable, NetSerializable]
public sealed class ReferenceVoiceAccessResponse(bool canCreate) : EntityEventArgs
{
    public bool CanCreate { get; } = canCreate;
}

[Serializable, NetSerializable]
public sealed class DeleteReferenceVoiceRequest(string speakerName) : EntityEventArgs
{
    public string SpeakerName { get; } = speakerName;
}

[Serializable, NetSerializable]
public sealed class DeleteReferenceVoiceResponse(string speakerName, DeleteReferenceVoiceResult result) : EntityEventArgs
{
    public string SpeakerName { get; } = speakerName;
    public DeleteReferenceVoiceResult Result { get; } = result;
}

[Serializable, NetSerializable]
public enum DeleteReferenceVoiceResult : byte
{
    Success,
    Forbidden,
    InvalidName,
    NotFound,
    ApiError,
}
