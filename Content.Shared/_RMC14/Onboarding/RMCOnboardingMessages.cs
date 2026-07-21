using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Onboarding;

[Serializable, NetSerializable]
public enum RMCOnboardingTrack : byte
{
    FullNewbie,
    MilitaryEquipment,
    SS14Veteran,
    Engineering,
    Medical,
    Command,
}

[Serializable, NetSerializable]
public enum RMCOnboardingStepKind : byte
{
    Move,
    AresConsole,
    PickUp,
    UseInHand,
    Drop,
    ToggleCombat,
    Supply,
    Medical,
    Engineering,
    Command,
    ApproachFoodVendor,
    TakeFood,
    SwitchHandsTwice,
    TakeFirstBite,
    FinishFood,
    SayNearby,
    SayLooc,
    InsertMagazine,
    KillDrone,
    AttachSling,
    DropSlungWeapon,
    PickUpGrenade,
    KillGrenadeTargets,
    MoveMedicalPatient,
    ExamineMedicalPatient,
    TreatFirstBleed,
    TreatSecondBleed,
    GiveTramadol,
    GiveTricordrazine,
    MedicalRecovery,
    MedicalAftercare,
    PerformCpr,
    Automatic,
}

public static class RMCOnboardingStepKinds
{
    public static string LocHint(this RMCOnboardingStepKind step)
    {
        return step switch
        {
            RMCOnboardingStepKind.Move => "rmc-onboarding-hint-move",
            RMCOnboardingStepKind.PickUp => "rmc-onboarding-hint-pick-up",
            RMCOnboardingStepKind.UseInHand => "rmc-onboarding-hint-use-in-hand",
            RMCOnboardingStepKind.Drop => "rmc-onboarding-hint-drop",
            RMCOnboardingStepKind.ToggleCombat => "rmc-onboarding-hint-combat",
            RMCOnboardingStepKind.ApproachFoodVendor => "rmc-onboarding-hint-approach-food",
            RMCOnboardingStepKind.TakeFood => "rmc-onboarding-hint-take-food",
            RMCOnboardingStepKind.SwitchHandsTwice => "rmc-onboarding-hint-switch-hands",
            RMCOnboardingStepKind.TakeFirstBite => "rmc-onboarding-hint-first-bite",
            RMCOnboardingStepKind.FinishFood => "rmc-onboarding-hint-finish-food",
            RMCOnboardingStepKind.SayNearby => "rmc-onboarding-hint-say-nearby",
            RMCOnboardingStepKind.SayLooc => "rmc-onboarding-hint-say-looc",
            RMCOnboardingStepKind.InsertMagazine => "rmc-onboarding-hint-insert-magazine",
            RMCOnboardingStepKind.KillDrone => "rmc-onboarding-hint-kill-drone",
            RMCOnboardingStepKind.AttachSling => "rmc-onboarding-hint-attach-sling",
            RMCOnboardingStepKind.DropSlungWeapon => "rmc-onboarding-hint-drop-slung-weapon",
            RMCOnboardingStepKind.PickUpGrenade => "rmc-onboarding-hint-pick-up-grenade",
            RMCOnboardingStepKind.KillGrenadeTargets => "rmc-onboarding-hint-kill-grenade-targets",
            RMCOnboardingStepKind.MoveMedicalPatient => "rmc-onboarding-hint-move-medical-patient",
            RMCOnboardingStepKind.ExamineMedicalPatient => "rmc-onboarding-hint-examine-medical-patient",
            RMCOnboardingStepKind.TreatFirstBleed or RMCOnboardingStepKind.TreatSecondBleed => "rmc-onboarding-hint-treat-bleed",
            RMCOnboardingStepKind.GiveTramadol => "rmc-onboarding-hint-give-tramadol",
            RMCOnboardingStepKind.GiveTricordrazine => "rmc-onboarding-hint-give-tricordrazine",
            RMCOnboardingStepKind.PerformCpr => "rmc-onboarding-hint-perform-cpr",
            RMCOnboardingStepKind.MedicalRecovery or RMCOnboardingStepKind.MedicalAftercare => "rmc-onboarding-hint-listen",
            RMCOnboardingStepKind.Automatic => "rmc-onboarding-hint-listen",
            _ => "rmc-onboarding-hint-interact",
        };
    }
}

public static class RMCOnboardingTracks
{
    public static readonly RMCOnboardingTrack[] Default =
    [
        RMCOnboardingTrack.FullNewbie,
        RMCOnboardingTrack.MilitaryEquipment,
        RMCOnboardingTrack.SS14Veteran,
        RMCOnboardingTrack.Engineering,
        RMCOnboardingTrack.Medical,
        RMCOnboardingTrack.Command,
    ];

    public static string LocName(this RMCOnboardingTrack track)
    {
        return track switch
        {
            RMCOnboardingTrack.FullNewbie => "rmc-onboarding-track-full-newbie",
            RMCOnboardingTrack.MilitaryEquipment => "rmc-onboarding-track-military-equipment",
            RMCOnboardingTrack.SS14Veteran => "rmc-onboarding-track-ss14-veteran",
            RMCOnboardingTrack.Engineering => "rmc-onboarding-track-engineering",
            RMCOnboardingTrack.Medical => "rmc-onboarding-track-medical",
            RMCOnboardingTrack.Command => "rmc-onboarding-track-command",
            _ => "rmc-onboarding-track-unknown",
        };
    }

    public static string LocDescription(this RMCOnboardingTrack track)
    {
        return track switch
        {
            RMCOnboardingTrack.FullNewbie => "rmc-onboarding-track-full-newbie-desc",
            RMCOnboardingTrack.MilitaryEquipment => "rmc-onboarding-track-military-equipment-desc",
            RMCOnboardingTrack.SS14Veteran => "rmc-onboarding-track-ss14-veteran-desc",
            RMCOnboardingTrack.Engineering => "rmc-onboarding-track-engineering-desc",
            RMCOnboardingTrack.Medical => "rmc-onboarding-track-medical-desc",
            RMCOnboardingTrack.Command => "rmc-onboarding-track-command-desc",
            _ => "rmc-onboarding-track-unknown-desc",
        };
    }

    public static bool IsAvailable(this RMCOnboardingTrack track)
    {
        return track is RMCOnboardingTrack.FullNewbie or
            RMCOnboardingTrack.MilitaryEquipment or
            RMCOnboardingTrack.Medical;
    }
}

[Serializable, NetSerializable]
public readonly record struct RMCOnboardingTrackStatus(
    RMCOnboardingTrack Track,
    bool Available,
    bool Completed);

[Serializable, NetSerializable]
public sealed class RMCOnboardingOfferEvent(RMCOnboardingTrackStatus[] tracks) : EntityEventArgs
{
    public RMCOnboardingTrackStatus[] Tracks { get; } = tracks;
}

[Serializable, NetSerializable]
public sealed class RMCOnboardingRequestMenuEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class RMCOnboardingSelectTrackEvent(bool accepted, RMCOnboardingTrack track) : EntityEventArgs
{
    public bool Accepted { get; } = accepted;
    public RMCOnboardingTrack Track { get; } = track;
}

[Serializable, NetSerializable]
public sealed class RMCOnboardingTaskEvent(
    bool active,
    string title,
    string description,
    string hint,
    int step,
    int stepCount) : EntityEventArgs
{
    public bool Active { get; } = active;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public string Hint { get; } = hint;
    public int Step { get; } = step;
    public int StepCount { get; } = stepCount;
}

[Serializable, NetSerializable]
public sealed class RMCOnboardingExitEvent : EntityEventArgs;
