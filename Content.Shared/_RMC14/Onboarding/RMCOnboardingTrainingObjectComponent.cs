using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Onboarding;

[RegisterComponent, NetworkedComponent]
public sealed partial class RMCOnboardingTrainingObjectComponent : Component
{
    [DataField]
    public RMCOnboardingStepKind Step;
}
