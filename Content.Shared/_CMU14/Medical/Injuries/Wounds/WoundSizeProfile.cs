using System;
using Content.Shared._RMC14.Medical.Wounds;

namespace Content.Shared._CMU14.Medical.Injuries.Wounds;

public static class WoundSizeProfile
{
    private readonly record struct WoundStage(float Threshold, string Name);

    private static readonly WoundStage[] CutSmallStages =
    {
        // RuCM edit start
        new(20f, "cmu-wound-stage-cut-small-ugly-ripped"),
        new(10f, "cmu-wound-stage-cut-small-ripped"),
        new(5f,  "cmu-wound-stage-cut-small"),
        new(2f,  "cmu-wound-stage-cut-small-healing"),
        new(0f,  "cmu-wound-stage-scab-small"),
        // RuCM edit end
    };

    private static readonly WoundStage[] CutDeepStages =
    {
        // RuCM edit start
        new(25f, "cmu-wound-stage-cut-deep-ugly-ripped"),
        new(20f, "cmu-wound-stage-cut-deep-ripped"),
        new(15f, "cmu-wound-stage-cut-deep"),
        new(8f,  "cmu-wound-stage-cut-deep-clotted"),
        new(2f,  "cmu-wound-stage-scab"),
        new(0f,  "cmu-wound-stage-fresh-skin"),
        // RuCM edit end
    };

    private static readonly WoundStage[] CutFleshStages =
    {
        // RuCM edit start
        new(35f, "cmu-wound-stage-cut-flesh-ugly-ripped"),
        new(30f, "cmu-wound-stage-cut-flesh-ugly"),
        new(25f, "cmu-wound-stage-cut-flesh"),
        new(15f, "cmu-wound-stage-cut-flesh-clot"),
        new(5f,  "cmu-wound-stage-scab-large"),
        new(0f,  "cmu-wound-stage-fresh-skin"),
        // RuCM edit end
    };

    private static readonly WoundStage[] CutGapingStages =
    {
        // RuCM edit start
        new(50f, "cmu-wound-stage-cut-gaping"),
        new(25f, "cmu-wound-stage-cut-gaping-clot-large"),
        new(15f, "cmu-wound-stage-cut-gaping-clot"),
        new(5f,  "cmu-wound-stage-scar-small-angry"),
        new(0f,  "cmu-wound-stage-scar-small-straight"),
        // RuCM edit end
    };

    private static readonly WoundStage[] CutGapingBigStages =
    {
        // RuCM edit start
        new(60f, "cmu-wound-stage-cut-gaping-big"),
        new(40f, "cmu-wound-stage-cut-gaping-big-healing"),
        new(10f, "cmu-wound-stage-scar-large-angry"),
        new(0f,  "cmu-wound-stage-scar-large-straight"),
        // RuCM edit end
    };

    private static readonly WoundStage[] CutMassiveStages =
    {
        // RuCM edit start
        new(70f, "cmu-wound-stage-cut-massive"),
        new(50f, "cmu-wound-stage-cut-massive-healing"),
        new(10f, "cmu-wound-stage-scar-massive-angry"),
        new(0f,  "cmu-wound-stage-scar-massive-jagged"),
        // RuCM edit end
    };

    private static readonly WoundStage[] BruiseStages =
    {
        // RuCM edit start
        new(80f, "cmu-wound-stage-bruise-monumental"),
        new(50f, "cmu-wound-stage-bruise-huge"),
        new(30f, "cmu-wound-stage-bruise-large"),
        new(20f, "cmu-wound-stage-bruise-moderate"),
        new(10f, "cmu-wound-stage-bruise-small"),
        new(5f,  "cmu-wound-stage-bruise-tiny"),
        new(0f,  "cmu-wound-stage-bruise-fading"),
        // RuCM edit end
    };

    private static readonly WoundStage[] BurnModerateStages =
    {
        // RuCM edit start
        new(10f, "cmu-wound-stage-burn-moderate-ripped"),
        new(5f,  "cmu-wound-stage-burn-moderate"),
        new(2f,  "cmu-wound-stage-burn-moderate-healing"),
        new(0f,  "cmu-wound-stage-fresh-skin"),
        // RuCM edit end
    };

    private static readonly WoundStage[] BurnLargeStages =
    {
        // RuCM edit start
        new(20f, "cmu-wound-stage-burn-large-ripped"),
        new(15f, "cmu-wound-stage-burn-large"),
        new(5f,  "cmu-wound-stage-burn-large-healing"),
        new(0f,  "cmu-wound-stage-fresh-skin"),
        // RuCM edit end
    };

    private static readonly WoundStage[] BurnSevereStages =
    {
        // RuCM edit start
        new(35f, "cmu-wound-stage-burn-severe-ripped"),
        new(30f, "cmu-wound-stage-burn-severe"),
        new(10f, "cmu-wound-stage-burn-severe-healing"),
        new(0f,  "cmu-wound-stage-burn-scar"),
        // RuCM edit end
    };

    private static readonly WoundStage[] BurnDeepStages =
    {
        // RuCM edit start
        new(45f, "cmu-wound-stage-burn-deep-ripped"),
        new(40f, "cmu-wound-stage-burn-deep"),
        new(15f, "cmu-wound-stage-burn-deep-healing"),
        new(0f,  "cmu-wound-stage-burn-scar-large"),
        // RuCM edit end
    };

    private static readonly WoundStage[] BurnCarbonisedStages =
    {
        // RuCM edit start
        new(50f, "cmu-wound-stage-burn-carbonised"),
        new(20f, "cmu-wound-stage-burn-carbonised-healing"),
        new(0f,  "cmu-wound-stage-burn-scar-massive"),
        // RuCM edit end
    };

    private static readonly WoundStage[] InternalBleedingStages =
    {
        new(0f, "cmu-wound-stage-bruised-artery"), // RuCM edit
    };

    private static readonly WoundStage[] LostLimbSmallStages =
    {
        // RuCM edit start
        new(40f, "cmu-wound-stage-stump-ripped"),
        new(30f, "cmu-wound-stage-stump-bloody"),
        new(15f, "cmu-wound-stage-stump-clotted"),
        new(0f,  "cmu-wound-stage-stump-scarred"),
        // RuCM edit end
    };

    private static readonly WoundStage[] LostLimbStages =
    {
        // RuCM edit start
        new(65f, "cmu-wound-stage-stump-ripped"),
        new(50f, "cmu-wound-stage-stump-bloody"),
        new(25f, "cmu-wound-stage-stump-clotted"),
        new(0f,  "cmu-wound-stage-stump-scarred"),
        // RuCM edit end
    };

    public static WoundSize FromDamage(float damage)
    {
        return CutFromDamage(damage);
    }

    public static WoundSize FromDamage(WoundType type, WoundMechanism mechanism, float damage)
    {
        if (type == WoundType.Burn || mechanism == WoundMechanism.Burn)
            return BurnFromDamage(damage);

        if (mechanism == WoundMechanism.Crush)
            return WoundSize.Bruise;

        return CutFromDamage(damage);
    }

    public static WoundSize CutFromDamage(float damage)
    {
        if (damage >= 70f)
            return WoundSize.CutMassive;
        if (damage >= 60f)
            return WoundSize.CutGapingBig;
        if (damage >= 50f)
            return WoundSize.CutGaping;
        if (damage >= 25f)
            return WoundSize.CutFlesh;
        if (damage >= 15f)
            return WoundSize.CutDeep;
        return WoundSize.CutSmall;
    }

    public static WoundSize BurnFromDamage(float damage)
    {
        if (damage >= 50f)
            return WoundSize.BurnCarbonised;
        if (damage >= 40f)
            return WoundSize.BurnDeep;
        if (damage >= 30f)
            return WoundSize.BurnSevere;
        if (damage >= 15f)
            return WoundSize.BurnLarge;
        return WoundSize.BurnModerate;
    }

    public static WoundCategory Category(WoundSize size) => size switch
    {
        WoundSize.Bruise => WoundCategory.Bruise,
        WoundSize.BurnModerate
            or WoundSize.BurnLarge
            or WoundSize.BurnSevere
            or WoundSize.BurnDeep
            or WoundSize.BurnCarbonised => WoundCategory.Burn,
        WoundSize.InternalBleeding => WoundCategory.InternalBleeding,
        WoundSize.LostLimbSmall or WoundSize.LostLimb => WoundCategory.LostLimb,
        _ => WoundCategory.Cut,
    };

    public static string StageName(WoundSize size, float damage)
    {
        return StageName(damage, Stages(size));
    }

    public static string TierName(WoundSize size) => size switch
    {
        // RuMC edit start
        WoundSize.CutSmall       => "cmu-wound-tier-cut-small",
        WoundSize.CutDeep        => "cmu-wound-tier-cut-deep",
        WoundSize.CutFlesh       => "cmu-wound-tier-cut-flesh",
        WoundSize.CutGaping      => "cmu-wound-tier-cut-gaping",
        WoundSize.CutGapingBig   => "cmu-wound-tier-cut-gaping-big",
        WoundSize.CutMassive     => "cmu-wound-tier-cut-massive",
        WoundSize.Bruise         => "cmu-wound-tier-bruise",
        WoundSize.BurnModerate   => "cmu-wound-tier-burn-moderate",
        WoundSize.BurnLarge      => "cmu-wound-tier-burn-large",
        WoundSize.BurnSevere     => "cmu-wound-tier-burn-severe",
        WoundSize.BurnDeep       => "cmu-wound-tier-burn-deep",
        WoundSize.BurnCarbonised => "cmu-wound-tier-burn-carbonised",
        WoundSize.InternalBleeding => "cmu-wound-tier-bruised-artery",
        WoundSize.LostLimbSmall or WoundSize.LostLimb => "cmu-wound-tier-stump",
        _                        => "cmu-wound-tier-generic",
        // RuMC edit end
    };

    public static int SeverityRank(WoundSize size, float damage = 0f)
    {
        return size switch
        {
            WoundSize.CutSmall or WoundSize.BurnModerate => 0,
            WoundSize.CutDeep or WoundSize.BurnLarge => 1,
            WoundSize.CutFlesh
                or WoundSize.CutGaping
                or WoundSize.BurnSevere
                or WoundSize.BurnDeep
                or WoundSize.LostLimbSmall => 2,
            WoundSize.CutGapingBig
                or WoundSize.CutMassive
                or WoundSize.BurnCarbonised
                or WoundSize.InternalBleeding
                or WoundSize.LostLimb => 3,
            WoundSize.Bruise => BruiseSeverityRank(damage),
            _ => 1,
        };
    }

    public static TimeSpan BandageDelay(WoundSize size, float damage = 0f) => SeverityRank(size, damage) switch
    {
        0 => TimeSpan.FromSeconds(0.5),
        1 => TimeSpan.FromSeconds(1.0),
        2 => TimeSpan.FromSeconds(2.0),
        3 => TimeSpan.FromSeconds(4.0),
        _ => TimeSpan.FromSeconds(1.0),
    };

    public static int BandagesRequired(WoundSize size, float damage = 0f) => SeverityRank(size, damage) switch
    {
        0 => 1,
        1 => 2,
        2 => 3,
        3 => 4,
        _ => 1,
    };

    public static float BleedMultiplier(WoundSize size, float damage = 0f) => SeverityRank(size, damage) switch
    {
        0 => 0.5f,
        1 => 1.0f,
        2 => 1.5f,
        3 => 2.0f,
        _ => 1.0f,
    };

    public static float FieldTreatmentPenalty(WoundSize size, float damage = 0f) => SeverityRank(size, damage) switch
    {
        0 => 0.05f,
        1 => 0.12f,
        2 => 0.20f,
        3 => 0.30f,
        _ => 0.12f,
    };

    public static float PainTarget(WoundSize size, float damage = 0f) => SeverityRank(size, damage) switch
    {
        0 => 5f,
        1 => 15f,
        2 => 30f,
        3 => 50f,
        _ => 0f,
    };

    private static int BruiseSeverityRank(float damage)
    {
        if (damage >= 50f)
            return 3;
        if (damage >= 30f)
            return 2;
        if (damage >= 10f)
            return 1;
        return 0;
    }

    private static WoundStage[] Stages(WoundSize size) => size switch
    {
        WoundSize.CutSmall => CutSmallStages,
        WoundSize.CutDeep => CutDeepStages,
        WoundSize.CutFlesh => CutFleshStages,
        WoundSize.CutGaping => CutGapingStages,
        WoundSize.CutGapingBig => CutGapingBigStages,
        WoundSize.CutMassive => CutMassiveStages,
        WoundSize.Bruise => BruiseStages,
        WoundSize.BurnModerate => BurnModerateStages,
        WoundSize.BurnLarge => BurnLargeStages,
        WoundSize.BurnSevere => BurnSevereStages,
        WoundSize.BurnDeep => BurnDeepStages,
        WoundSize.BurnCarbonised => BurnCarbonisedStages,
        WoundSize.InternalBleeding => InternalBleedingStages,
        WoundSize.LostLimbSmall => LostLimbSmallStages,
        WoundSize.LostLimb => LostLimbStages,
        _ => CutDeepStages,
    };

    private static string StageName(float damage, WoundStage[] stages)
    {
        foreach (var stage in stages)
        {
            if (damage >= stage.Threshold)
                return stage.Name;
        }

        return stages[^1].Name;
    }
}
