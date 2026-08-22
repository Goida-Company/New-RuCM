namespace Content.DiscordBot.Governance;

/// <summary>
/// Read-only projection helpers for explaining qualification progress.
/// These estimates do not grant qualification and do not mutate reputation.
/// </summary>
public static class QualificationProjection
{
    public static double AdditionalPositiveEvidenceForLowerBound(
        int score,
        double evidenceWeight,
        double targetLowerBound)
    {
        if (targetLowerBound is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(targetLowerBound));

        evidenceWeight = Math.Max(0, evidenceWeight);
        var total = ReputationPolicy.TrackPriorStrength + evidenceWeight;
        var mean = Math.Clamp(score / 1000.0, 0.001, 0.999);
        var alpha = mean * total;
        var beta = (1.0 - mean) * total;

        if (ReputationMath.BetaInverse(ReputationPolicy.CredibleProbability, alpha, beta) >= targetLowerBound)
            return 0;

        var lower = 0.0;
        var upper = 1.0;
        while (upper < 1024 &&
               ReputationMath.BetaInverse(ReputationPolicy.CredibleProbability, alpha + upper, beta) < targetLowerBound)
        {
            upper *= 2.0;
        }

        if (upper >= 1024 &&
            ReputationMath.BetaInverse(ReputationPolicy.CredibleProbability, alpha + upper, beta) < targetLowerBound)
            return double.PositiveInfinity;

        for (var i = 0; i < 64; i++)
        {
            var middle = (lower + upper) * 0.5;
            if (ReputationMath.BetaInverse(ReputationPolicy.CredibleProbability, alpha + middle, beta) < targetLowerBound)
                lower = middle;
            else
                upper = middle;
        }

        return upper;
    }
}
