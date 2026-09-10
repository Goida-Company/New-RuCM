using Robust.Shared.GameStates;

namespace Content.Shared._CMU14.Yautja;

/// <summary>
/// CMSS13 52be8a8ce: item throwforce/speed/weight and human hitby embedding rules.
/// Kept separate from melee damage and from the smart-disc's autonomous attacks.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class YautjaThrowComponent : Component
{
    [DataField] public float Force = 1;
    [DataField] public float Speed = 6.67f;
    [DataField] public int Weight = 3;
    [DataField] public bool Sharp;
    [DataField] public bool Edge;
    [DataField] public bool Embeddable = true;

    // BYOND launch_towards moves one tile every (10 / speed - 0.5) deciseconds.
    public float FlightSpeed => 10f / (10f / Speed - 0.5f);

    public static float SourceSpeed(float velocity) => velocity <= 0 ? 0 : 10f / (10f / velocity + 0.5f);

    public float ImpactDamage(float velocity) => (1 + Force * 0.02f) * Force * 0.05f * SourceSpeed(velocity);

    // CMSS13 is_sharp requires both sharp and edge; edge alone still adds 10 AP.
    public int ArmorPiercing => (Sharp && Edge ? 30 : 0) + (Edge ? 10 : 0);

    public float EmbedChance(float damage)
    {
        var sharp = Sharp && Edge;
        var direct = sharp ? Math.Clamp(damage / (10 * Weight), 0, 1) : 0;
        var threshold = (sharp ? 5 : 15) * Weight;
        var secondary = damage > threshold ? Math.Clamp(damage / (Weight * (sharp ? 1 : 3)) / 100, 0, 1) : 0;
        return direct + (1 - direct) * secondary;
    }
}
