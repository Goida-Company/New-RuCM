namespace Content.DiscordBot.Governance;

public static class ReputationTracks
{
    public const string General = "general";
    // Legacy-only track retained so immutable historical observations remain readable.
    // It is no longer an active service path and must not be offered for selection.
    public const string Support = "support";
    public const string Moderation = "moderation";
    public const string Jury = "jury";
    public const string Event = "event";
    public const string Contributor = "contributor";

    public static readonly string[] ServicePaths = [Moderation, Jury, Event, Contributor];
    public static readonly string[] All = [General, Support, Moderation, Jury, Event, Contributor];

    public static bool IsPath(string value) => ServicePaths.Contains(value, StringComparer.Ordinal);
    public static bool IsTrack(string value) => All.Contains(value, StringComparer.Ordinal);
}

public static class ReputationReasons
{
    // Historical reason key is intentionally preserved for immutable observations.
    // New AHelp observations are recorded on the moderation track.
    public const string AHelpResolved = "support.ahelp_resolved";
    public const string DutyCompleted = "moderation.duty_completed";
    public const string DutyFailed = "moderation.duty_failed";
    public const string JuryCompleted = "jury.duty_completed";
    public const string JuryFailed = "jury.duty_failed";
    public const string EventReviewCompleted = "event.review_completed";
    public const string EventReviewFailed = "event.review_failed";
    public const string EventSessionCompleted = "event.session_completed";
    public const string EventSessionAborted = "event.session_aborted";
    public const string ModerationReviewCompleted = "moderation.review_completed";
    public const string ModerationReviewFailed = "moderation.review_failed";
    public const string ModerationActionCorrect = "moderation.action_correct";
    public const string ModerationActionMinorIssue = "moderation.action_minor_issue";
    public const string ModerationActionWrong = "moderation.action_wrong";
    public const string FalseReport = "general.false_report";
    public const string ContributionAccepted = "contributor.accepted";
}

public sealed record ReputationPosterior(
    string Track,
    double Alpha,
    double Beta,
    double Mean,
    double LowerBound,
    double EvidenceWeight,
    int Score);

public sealed record GameActivityEvidence(
    double OverallHours,
    int ActiveWeeks,
    int AccountAgeDays,
    double ActivityIndex,
    double EvidenceWeight);

public sealed record ReputationObservationInput(
    Guid UserId,
    string Track,
    double SuccessWeight,
    double FailureWeight,
    bool SeriousNegative,
    string Reason,
    string EntityType,
    string EntityId,
    DateTime OccurredAt,
    string CreatedByType,
    string? CreatedById,
    string IdempotencyKey,
    string Metadata = "{}");

public sealed record ReputationObservationValue(
    DateTime OccurredAt,
    string Reason,
    double SuccessWeight,
    double FailureWeight,
    bool SeriousNegative);

public static class ReputationPolicy
{
    public const double GeneralPriorAlpha = 5.0;
    public const double GeneralPriorBeta = 5.0;
    public const double TrackPriorStrength = 6.0;
    public const double PositiveHalfLifeDays = 180.0;
    public const double NegativeHalfLifeDays = 270.0;
    public const double SeriousNegativeHalfLifeDays = 365.0;
    public const double RehabilitationRate = 0.12;
    public const double RecidivismStep = 0.35;
    public const double GeneralPathSpillover = 0.25;
    public const double GeneralNegativeSpillover = 0.45;
    public const double GameActivityMaxEvidence = 3.0;
    public const double CredibleProbability = 0.10; // one-sided 90% lower credible bound
    public const int NeutralScore = 500;

    public static (double Success, double Failure, bool Serious) EvidenceFor(string reason) => reason switch
    {
        ReputationReasons.AHelpResolved => (0.35, 0, false),
        ReputationReasons.DutyCompleted => (0.55, 0, false),
        ReputationReasons.DutyFailed => (0, 1.00, false),
        ReputationReasons.JuryCompleted => (1.00, 0, false),
        ReputationReasons.JuryFailed => (0, 1.50, false),
        ReputationReasons.EventReviewCompleted => (0.85, 0, false),
        ReputationReasons.EventReviewFailed => (0, 1.25, false),
        ReputationReasons.EventSessionCompleted => (1.20, 0, false),
        ReputationReasons.EventSessionAborted => (0, 0.35, false),
        ReputationReasons.ModerationReviewCompleted => (0.85, 0, false),
        ReputationReasons.ModerationReviewFailed => (0, 1.25, false),
        ReputationReasons.ModerationActionCorrect => (1.25, 0, false),
        ReputationReasons.ModerationActionMinorIssue => (0.20, 0.55, false),
        ReputationReasons.ModerationActionWrong => (0, 1.80, true),
        ReputationReasons.FalseReport => (0, 3.00, true),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Неизвестное репутационное событие."),
    };

    public static double ContributionSuccessWeight(double impact, double quality, double stability)
    {
        if (impact <= 0 || quality <= 0 || stability <= 0)
            return 0;
        var raw = impact * quality * stability;
        // Logarithmic saturation: a large contribution matters, but cannot dwarf months of reliable service.
        return Math.Clamp(Math.Log(1.0 + raw) / Math.Log(1.0 + 6.75) * 2.5, 0.10, 2.50);
    }

    public static short EligibleQualificationLevel(string track, ReputationPosterior posterior, int completedAssignments)
    {
        // Promotion uses the conservative lower credible bound rather than the optimistic mean.
        if (posterior.LowerBound >= 0.85 && posterior.EvidenceWeight >= 20 && completedAssignments >= 20)
            return 4;
        if (posterior.LowerBound >= 0.75 && posterior.EvidenceWeight >= 10 && completedAssignments >= 10)
            return 3;
        if (posterior.LowerBound >= 0.65 && posterior.EvidenceWeight >= 4 && completedAssignments >= 4)
            return 2;
        return 1;
    }

    public static double DemotionThreshold(short currentLevel) => currentLevel switch
    {
        >= 4 => 0.78,
        3 => 0.68,
        2 => 0.57,
        _ => 0.0,
    };
}

public static class ReputationMath
{
    private static readonly double[] Lanczos =
    [
        676.5203681218851,
        -1259.1392167224028,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7,
    ];

    public static GameActivityEvidence Activity(double overallHours, int activeWeeks, int accountAgeDays)
    {
        overallHours = Math.Max(0, overallHours);
        activeWeeks = Math.Max(0, activeWeeks);
        accountAgeDays = Math.Max(0, accountAgeDays);
        var hours = 1.0 - Math.Exp(-overallHours / 120.0);
        var weeks = 1.0 - Math.Exp(-activeWeeks / 26.0);
        var tenure = 1.0 - Math.Exp(-accountAgeDays / 365.0);
        var index = Math.Clamp(hours * 0.45 + weeks * 0.40 + tenure * 0.15, 0, 1);
        return new GameActivityEvidence(overallHours, activeWeeks, accountAgeDays, index,
            index * ReputationPolicy.GameActivityMaxEvidence);
    }

    public static bool IsAuthoritativeReason(string reason) =>
        !reason.StartsWith("legacy:", StringComparison.Ordinal) &&
        !reason.StartsWith("spillover:legacy:", StringComparison.Ordinal);

    public static ReputationPosterior Posterior(
        string track,
        IReadOnlyList<ReputationObservationValue> observations,
        DateTime now,
        double priorMean = 0.5,
        double? priorStrength = null,
        double extraSuccessEvidence = 0,
        double extraFailureEvidence = 0)
    {
        var strength = priorStrength ?? (track == ReputationTracks.General
            ? ReputationPolicy.GeneralPriorAlpha + ReputationPolicy.GeneralPriorBeta
            : ReputationPolicy.TrackPriorStrength);

        // Until RUCM has enough calibrated outcome data, service-track priors must stay neutral.
        // A newcomer must not inherit the community's accumulated trust merely by joining a path.
        if (track != ReputationTracks.General)
            priorMean = 0.5;
        priorMean = Math.Clamp(priorMean, 0.05, 0.95);

        var alpha = track == ReputationTracks.General && priorStrength == null
            ? ReputationPolicy.GeneralPriorAlpha
            : priorMean * strength;
        var beta = track == ReputationTracks.General && priorStrength == null
            ? ReputationPolicy.GeneralPriorBeta
            : (1.0 - priorMean) * strength;

        // Legacy linear-rating imports remain immutable and visible for audit, but they are not calibrated
        // Bayesian evidence. Operational v2 observations rebuilt from source tables are authoritative.
        var ordered = observations
            .Where(value => IsAuthoritativeReason(value.Reason))
            .OrderBy(value => value.OccurredAt)
            .ToArray();
        // Anti-farm is intentionally local to a day. Repeated identical work in one burst has diminishing
        // returns, while sustained service on different days regains full base weight.
        var repeated = new Dictionary<(string Reason, DateOnly Day), int>();
        var previousSerious = 0;
        var successTotal = Math.Max(0, extraSuccessEvidence);
        var failureTotal = Math.Max(0, extraFailureEvidence);

        for (var index = 0; index < ordered.Length; index++)
        {
            var observation = ordered[index];
            var ageDays = Math.Max(0, (now - observation.OccurredAt).TotalDays);
            var bucket = (observation.Reason, DateOnly.FromDateTime(observation.OccurredAt));
            repeated.TryGetValue(bucket, out var repeatCount);
            repeatCount++;
            repeated[bucket] = repeatCount;
            var repetitionWeight = 1.0 / Math.Sqrt(repeatCount);

            if (observation.SuccessWeight > 0)
            {
                var timeWeight = HalfLife(ageDays, ReputationPolicy.PositiveHalfLifeDays);
                successTotal += observation.SuccessWeight * timeWeight * repetitionWeight;
            }

            if (observation.FailureWeight <= 0)
                continue;

            var halfLife = observation.SeriousNegative
                ? ReputationPolicy.SeriousNegativeHalfLifeDays
                : ReputationPolicy.NegativeHalfLifeDays;
            var failure = observation.FailureWeight * HalfLife(ageDays, halfLife) * repetitionWeight;
            if (observation.SeriousNegative)
            {
                var nextSerious = Array.FindIndex(ordered, index + 1, value => value.SeriousNegative && value.FailureWeight > 0);
                var rehabilitationEnd = nextSerious >= 0 ? ordered[nextSerious].OccurredAt : now;
                var positiveAfter = ordered
                    .Where(value => value.OccurredAt > observation.OccurredAt && value.OccurredAt < rehabilitationEnd)
                    .Sum(value => value.SuccessWeight * HalfLife(Math.Max(0, (now - value.OccurredAt).TotalDays), ReputationPolicy.PositiveHalfLifeDays));
                failure *= Math.Exp(-ReputationPolicy.RehabilitationRate * positiveAfter);
                failure *= 1.0 + ReputationPolicy.RecidivismStep * previousSerious;
                previousSerious++;
            }
            failureTotal += failure;
        }

        alpha += successTotal;
        beta += failureTotal;
        var mean = alpha / (alpha + beta);
        var lower = BetaInverse(ReputationPolicy.CredibleProbability, alpha, beta);
        var score = Math.Clamp((int) Math.Round(mean * 1000.0), 0, 1000);
        return new ReputationPosterior(track, alpha, beta, mean, lower,
            successTotal + failureTotal, score);
    }

    public static double SampleBeta(double alpha, double beta, Random? random = null)
    {
        random ??= Random.Shared;
        var x = SampleGamma(alpha, random);
        var y = SampleGamma(beta, random);
        return x / (x + y);
    }

    public static double HalfLife(double ageDays, double halfLifeDays) =>
        Math.Pow(2.0, -Math.Max(0, ageDays) / halfLifeDays);

    public static double BetaInverse(double probability, double alpha, double beta)
    {
        probability = Math.Clamp(probability, 1e-10, 1.0 - 1e-10);
        var lower = 0.0;
        var upper = 1.0;
        for (var i = 0; i < 80; i++)
        {
            var middle = (lower + upper) * 0.5;
            if (RegularizedIncompleteBeta(alpha, beta, middle) < probability)
                lower = middle;
            else
                upper = middle;
        }
        return (lower + upper) * 0.5;
    }

    private static double RegularizedIncompleteBeta(double a, double b, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var logarithm = LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1.0 - x);
        var front = Math.Exp(logarithm);
        if (x < (a + 1.0) / (a + b + 2.0))
            return front * BetaContinuedFraction(a, b, x) / a;
        return 1.0 - front * BetaContinuedFraction(b, a, 1.0 - x) / b;
    }

    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const int maxIterations = 200;
        const double epsilon = 3e-14;
        const double fpMin = 1e-300;
        var qab = a + b;
        var qap = a + 1.0;
        var qam = a - 1.0;
        var c = 1.0;
        var d = 1.0 - qab * x / qap;
        if (Math.Abs(d) < fpMin) d = fpMin;
        d = 1.0 / d;
        var h = d;
        for (var m = 1; m <= maxIterations; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1.0 + aa * d;
            if (Math.Abs(d) < fpMin) d = fpMin;
            c = 1.0 + aa / c;
            if (Math.Abs(c) < fpMin) c = fpMin;
            d = 1.0 / d;
            h *= d * c;

            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1.0 + aa * d;
            if (Math.Abs(d) < fpMin) d = fpMin;
            c = 1.0 + aa / c;
            if (Math.Abs(c) < fpMin) c = fpMin;
            d = 1.0 / d;
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1.0) < epsilon)
                break;
        }
        return h;
    }

    private static double LogGamma(double z)
    {
        if (z < 0.5)
            return Math.Log(Math.PI) - Math.Log(Math.Sin(Math.PI * z)) - LogGamma(1.0 - z);
        z -= 1.0;
        var x = 0.99999999999980993;
        for (var i = 0; i < Lanczos.Length; i++)
            x += Lanczos[i] / (z + i + 1.0);
        var t = z + Lanczos.Length - 0.5;
        return 0.5 * Math.Log(2.0 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(x);
    }

    private static double SampleGamma(double shape, Random random)
    {
        if (shape <= 0)
            throw new ArgumentOutOfRangeException(nameof(shape));
        if (shape < 1.0)
        {
            var u = Math.Max(double.Epsilon, random.NextDouble());
            return SampleGamma(shape + 1.0, random) * Math.Pow(u, 1.0 / shape);
        }

        var d = shape - 1.0 / 3.0;
        var c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            var x = StandardNormal(random);
            var v = 1.0 + c * x;
            if (v <= 0)
                continue;
            v = v * v * v;
            var u = random.NextDouble();
            if (u < 1.0 - 0.0331 * x * x * x * x)
                return d * v;
            if (Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log(v)))
                return d * v;
        }
    }

    private static double StandardNormal(Random random)
    {
        var u1 = Math.Max(double.Epsilon, random.NextDouble());
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
