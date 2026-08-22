namespace Content.DiscordBot.Governance;

public static class ModerationQualificationPolicy
{
    public static short EligibleLevel(ModerationTrustProfile profile)
    {
        if (profile.CompletedDuties >= 30 &&
            profile.ReviewedActions >= 12 &&
            profile.TrustScore >= 875 &&
            profile.ProceduralScore >= 92 &&
            profile.ReliabilityScore >= 90 &&
            profile.Confidence >= 70)
            return 4;

        if (profile.CompletedDuties >= 15 &&
            profile.ReviewedActions >= 5 &&
            profile.TrustScore >= 800 &&
            profile.ProceduralScore >= 85 &&
            profile.ReliabilityScore >= 85 &&
            profile.Confidence >= 40)
            return 3;

        if (profile.CompletedDuties >= 5 &&
            profile.FailedDuties <= 1 &&
            profile.TrustScore >= 700 &&
            profile.ReliabilityScore >= 80)
            return 2;

        return 1;
    }
}
