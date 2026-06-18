using System;
using Content.Shared.FixedPoint;

namespace Content.Shared._CMU14.Medical.Human.Systems;

public static class HumanMedicalWorkerTiming
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    public static bool TryGetElapsed(
        TimeSpan now,
        ref TimeSpan lastUpdate,
        ref TimeSpan nextUpdate,
        out FixedPoint2 elapsed)
    {
        return TryGetElapsed(
            now,
            DefaultInterval,
            ref lastUpdate,
            ref nextUpdate,
            out elapsed);
    }

    public static bool TryGetElapsed(
        TimeSpan now,
        TimeSpan interval,
        ref TimeSpan lastUpdate,
        ref TimeSpan nextUpdate,
        out FixedPoint2 elapsed)
    {
        elapsed = FixedPoint2.Zero;

        if (lastUpdate == TimeSpan.Zero && nextUpdate == TimeSpan.Zero)
        {
            lastUpdate = now;
            nextUpdate = now + interval;
            return false;
        }

        if (nextUpdate > now)
            return false;

        var delta = now - lastUpdate;
        lastUpdate = now;
        nextUpdate = now + interval;

        if (delta <= TimeSpan.Zero)
            return false;

        elapsed = FixedPoint2.New((float) delta.TotalSeconds);
        return true;
    }
}
