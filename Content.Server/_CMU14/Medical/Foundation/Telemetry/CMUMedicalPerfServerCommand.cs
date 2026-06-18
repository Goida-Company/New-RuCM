using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration;
using Content.Shared._CMU14.Medical.Human.Components;
using Content.Shared._CMU14.Medical.Human.Data;
using Content.Shared._CMU14.Medical.Human.Diagnostics;
using Content.Shared._CMU14.Medical.Human.Systems;
using Content.Shared.Administration;
using Content.Shared.FixedPoint;
using Robust.Shared.Console;

namespace Content.Server._CMU14.Medical.Foundation.Telemetry;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class CMUMedicalPerfServerCommand : IConsoleCommand
{
    private const int DefaultTopCount = 10;
    private const int MaxTopCount = 100;

    private static readonly MedicalDirtyFlags[] DirtyFlagValues =
    {
        MedicalDirtyFlags.Regions,
        MedicalDirtyFlags.Injuries,
        MedicalDirtyFlags.Skeletal,
        MedicalDirtyFlags.Organs,
        MedicalDirtyFlags.Bleeding,
        MedicalDirtyFlags.DetachedLimbs,
        MedicalDirtyFlags.Summary,
        MedicalDirtyFlags.ForeignObjects,
    };

    private static readonly MedicalActivityFlags[] ActivityFlagValues =
    {
        MedicalActivityFlags.ActiveBleeding,
        MedicalActivityFlags.ActiveOrganSymptoms,
        MedicalActivityFlags.ActiveBoneKnitting,
        MedicalActivityFlags.ActiveMedicalSummaryDirty,
        MedicalActivityFlags.ActiveTourniquet,
        MedicalActivityFlags.ActiveTreatedWoundHealing,
        MedicalActivityFlags.ActiveUnsplintedFractureRisk,
        MedicalActivityFlags.ActiveEmbeddedObjectMovement,
    };

    [Dependency] private IEntityManager _entities = default!;

    public string Command => "cmu_medical_perf_server";
    public string Description => "Reports authoritative CMU medical ledger, worker, and summary churn diagnostics.";
    public string Help => "Usage: cmu_medical_perf_server [top=10] [deep]\n" +
                          "  top: positive number of ranked bodies to print, capped at 100.\n" +
                          "  deep: additionally project dirty summaries to tell whether they would publish a new summary revision.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!TryParseArgs(shell, args, out var topCount, out var deep))
            return;

        var report = BuildReport(deep);
        PrintReport(shell, report, topCount, deep);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                new[]
                {
                    new CompletionOption("10", "print the top 10 medical bodies"),
                    new CompletionOption("25", "print the top 25 medical bodies"),
                    new CompletionOption("deep", "compare dirty summaries against projected presentation state"),
                },
                "top count or deep");
        }

        if (args.Length == 2)
            return CompletionResult.FromHint("optional top count or deep");

        return CompletionResult.Empty;
    }

    private bool TryParseArgs(
        IConsoleShell shell,
        string[] args,
        out int topCount,
        out bool deep)
    {
        topCount = DefaultTopCount;
        deep = false;

        foreach (var arg in args)
        {
            if (arg.Equals("deep", StringComparison.OrdinalIgnoreCase))
            {
                deep = true;
                continue;
            }

            if (!int.TryParse(arg, out var parsed) || parsed <= 0)
            {
                shell.WriteError(Help);
                return false;
            }

            topCount = Math.Min(parsed, MaxTopCount);
        }

        return true;
    }

    private MedicalPerfReport BuildReport(bool deep)
    {
        var report = new MedicalPerfReport();
        var query = _entities.EntityQueryEnumerator<HumanMedicalComponent>();
        while (query.MoveNext(out var uid, out var medical))
        {
            if (_entities.Deleted(uid))
                continue;

            report.Add(AnalyzeBody(uid, medical, deep));
        }

        return report;
    }

    private MedicalPerfBody AnalyzeBody(EntityUid uid, HumanMedicalComponent medical, bool deep)
    {
        var bleeding = HumanBleedingSystem.CalculateBleedingTick(medical);
        var organs = HumanOrganSymptomSystem.CalculateOrganSymptomTick(medical);
        var boneKnitting = HumanBoneKnittingSystem.CalculateBoneKnittingTick(medical);
        var tourniquets = HumanTourniquetSystem.CalculateTourniquetTick(medical);
        var healing = HumanTreatedWoundHealingSystem.CalculateTreatedWoundHealingTick(medical);

        var summaryDirty = medical.DirtyFlags.HasFlag(MedicalDirtyFlags.Summary);
        var projectedSummaryChanged = false;
        var projectedSummaryUnchanged = false;
        if (deep && summaryDirty)
        {
            var projected = MedicalSummaryBuilder.BuildForCurrentRevision(medical, medical.Summary);
            projectedSummaryChanged = !MedicalSummaryBuilder.ProjectionEquals(projected, medical.Summary);
            projectedSummaryUnchanged = !projectedSummaryChanged;
        }

        var expectedActivity = MedicalActivityClassifier.Classify(medical);
        var markerActivity = GetMarkerActivity(uid);
        var staleMarkers = markerActivity & ~expectedActivity;
        var missingMarkers = expectedActivity & ~markerActivity;

        var body = new MedicalPerfBody(uid, FormatEntity(uid))
        {
            LedgerRevision = medical.Revision,
            SummaryRevision = medical.Summary.Revision,
            RevisionGap = Math.Max(0, medical.Revision - medical.Summary.Revision),
            SummaryInitialized = medical.SummaryInitialized,
            DirtyFlags = medical.DirtyFlags,
            DirtyFlagCount = CountDirtyFlags(medical.DirtyFlags),
            SummaryDirty = summaryDirty,
            ProjectedSummaryChanged = projectedSummaryChanged,
            ProjectedSummaryUnchanged = projectedSummaryUnchanged,
            ExpectedActivity = expectedActivity,
            MarkerActivity = markerActivity,
            StaleMarkers = staleMarkers,
            MissingMarkers = missingMarkers,
            MarkerCount = CountActivityFlags(markerActivity),
            StaleMarkerCount = CountActivityFlags(staleMarkers),
            MissingMarkerCount = CountActivityFlags(missingMarkers),
            InjuryCount = medical.Injuries.Count,
            BleedSourceCount = medical.BleedSources.Count,
            ActiveBleedSourceCount = bleeding.ActiveSources,
            BleedRate = bleeding.TotalRate,
            SymptomaticOrganCount = organs.SymptomaticOrgans,
            WorstOrganStatus = organs.WorstStatus,
            BoneKnittingRegionCount = boneKnitting.ActiveRegions,
            ShortestBoneKnitting = boneKnitting.ShortestRemaining,
            TourniquetRegionCount = tourniquets.ActiveRegions,
            ShortestTourniquetNecrosis = tourniquets.ShortestNecrosisSecondsRemaining,
            HealingInjuryCount = healing.ActiveInjuries,
            BruteRecoveryRate = healing.BruteRecoveryRate,
            BurnRecoveryRate = healing.BurnRecoveryRate,
            ForeignObjectCount = medical.ForeignObjects.Count,
            DetachedLimbCount = medical.DetachedLimbs.Count,
        };

        if (_entities.TryGetComponent(uid, out HumanMedicalSummaryComponent? summaryComponent))
        {
            body.HasSummaryComponent = true;
            body.StaleSummaryComponent = summaryComponent.Summary != medical.Summary;
        }

        foreach (var region in medical.Regions)
        {
            if (region.Region == BodyRegion.None)
                continue;

            var regionDamage = region.BruteDamage + region.BurnDamage;
            body.TotalRegionDamage += regionDamage;

            if (regionDamage > FixedPoint2.Zero)
                body.DamagedRegionCount++;

            if (region.Skeletal.Broken)
            {
                body.BrokenRegionCount++;
                if (!region.Skeletal.Stabilized)
                    body.UnsplintedBrokenRegionCount++;
            }

            if (region.Tourniquet.Applied)
                body.AppliedTourniquetRegionCount++;
        }

        foreach (var organ in medical.Organs)
        {
            if (organ.Slot == OrganSlot.None)
                continue;

            body.TotalOrganDamage += organ.Damage;
            if (organ.Damage > FixedPoint2.Zero || organ.Status != OrganDamageStatus.None)
                body.DamagedOrganCount++;
        }

        foreach (var foreignObject in medical.ForeignObjects)
        {
            if (!foreignObject.Active)
                continue;

            body.ActiveForeignObjectCount++;
            body.ForeignObjectFragments += foreignObject.Fragments;
        }

        body.Score = CalculateScore(body);
        return body;
    }

    private MedicalActivityFlags GetMarkerActivity(EntityUid uid)
    {
        var flags = MedicalActivityFlags.None;

        if (_entities.HasComponent<ActiveBleedingComponent>(uid))
            flags |= MedicalActivityFlags.ActiveBleeding;

        if (_entities.HasComponent<ActiveOrganSymptomsComponent>(uid))
            flags |= MedicalActivityFlags.ActiveOrganSymptoms;

        if (_entities.HasComponent<ActiveBoneKnittingComponent>(uid))
            flags |= MedicalActivityFlags.ActiveBoneKnitting;

        if (_entities.HasComponent<ActiveMedicalSummaryDirtyComponent>(uid))
            flags |= MedicalActivityFlags.ActiveMedicalSummaryDirty;

        if (_entities.HasComponent<ActiveTourniquetComponent>(uid))
            flags |= MedicalActivityFlags.ActiveTourniquet;

        if (_entities.HasComponent<ActiveTreatedWoundHealingComponent>(uid))
            flags |= MedicalActivityFlags.ActiveTreatedWoundHealing;

        if (_entities.HasComponent<ActiveUnsplintedFractureRiskComponent>(uid))
            flags |= MedicalActivityFlags.ActiveUnsplintedFractureRisk;

        if (_entities.HasComponent<ActiveEmbeddedObjectMovementComponent>(uid))
            flags |= MedicalActivityFlags.ActiveEmbeddedObjectMovement;

        return flags;
    }

    private static int CalculateScore(MedicalPerfBody body)
    {
        var score = 0;
        score += body.MarkerCount * 25;
        score += body.StaleMarkerCount * 20;
        score += body.MissingMarkerCount * 10;
        score += body.DirtyFlagCount * 10;
        score += Math.Min(body.RevisionGap, 250);
        score += body.ActiveBleedSourceCount * 12;
        score += body.SymptomaticOrganCount * 10;
        score += body.BoneKnittingRegionCount * 8;
        score += body.TourniquetRegionCount * 8;
        score += body.HealingInjuryCount * 8;
        score += body.ActiveForeignObjectCount * 6;
        score += body.InjuryCount * 2;
        score += body.BleedSourceCount * 2;

        if (body.SummaryDirty)
            score += 12;

        if (body.ProjectedSummaryChanged)
            score += 20;

        if (body.StaleSummaryComponent)
            score += 15;

        return score;
    }

    private void PrintReport(
        IConsoleShell shell,
        MedicalPerfReport report,
        int topCount,
        bool deep)
    {
        shell.WriteLine($"CMU medical server perf snapshot: bodies={report.TotalBodies}, top={topCount}, deep={deep}");
        if (report.TotalBodies == 0)
            return;

        shell.WriteLine("Ledger and summary:");
        shell.WriteLine($"  initialized={report.SummaryInitializedBodies}, summaryComponents={report.SummaryComponentBodies}, staleSummaryComponents={report.StaleSummaryComponentBodies}");
        shell.WriteLine($"  ledgerRevisions={report.TotalLedgerRevisions}, summaryRevisions={report.TotalSummaryRevisions}, revisionGapTotal={report.TotalRevisionGap}, maxGap={report.MaxRevisionGap}");
        shell.WriteLine($"  dirtySummary={report.DirtySummaryBodies}, activeSummaryDirtyMarkers={report.ActiveSummaryDirtyMarkers}");
        if (deep)
        {
            shell.WriteLine($"  summary projection: wouldPublish={report.ProjectedSummaryChangedBodies}, dirtyButUnchanged={report.ProjectedSummaryUnchangedBodies}");
        }
        else
        {
            shell.WriteLine("  summary projection: skipped; add deep to compare dirty summaries against projected state.");
        }

        shell.WriteLine("Worker markers:");
        shell.WriteLine($"  bleeding={report.ActiveBleedingMarkers}, organSymptoms={report.ActiveOrganSymptomMarkers}, boneKnitting={report.ActiveBoneKnittingMarkers}, tourniquet={report.ActiveTourniquetMarkers}");
        shell.WriteLine($"  treatedHealing={report.ActiveTreatedWoundHealingMarkers}, summaryDirty={report.ActiveSummaryDirtyMarkers}, fractureRisk={report.ActiveUnsplintedFractureRiskMarkers}, embeddedMovement={report.ActiveEmbeddedObjectMovementMarkers}");
        shell.WriteLine($"  staleMarkers={report.StaleMarkerBodies}, missingMarkers={report.MissingMarkerBodies}");

        shell.WriteLine("Estimated worker workload:");
        shell.WriteLine($"  activeBleeds={report.ActiveBleedSources}/{report.BleedSources}, bleedRate={FormatFixed(report.TotalBleedRate)}/s");
        shell.WriteLine($"  symptomaticOrgans={report.SymptomaticOrgans}, boneKnittingRegions={report.BoneKnittingRegions}, tourniquetRegions={report.TourniquetRegions}, treatedHealingInjuries={report.HealingInjuries}");
        shell.WriteLine($"  healingRate brute={FormatFixed(report.TotalBruteRecoveryRate)}/s burn={FormatFixed(report.TotalBurnRecoveryRate)}/s");
        shell.WriteLine($"  shortest bone knit={FormatSeconds(report.ShortestBoneKnitting)}, shortest tourniquet necrosis={FormatSeconds(report.ShortestTourniquetNecrosis)}");

        shell.WriteLine("Ledger records:");
        shell.WriteLine($"  injuries={report.Injuries}, bleedSources={report.BleedSources}, foreignObjects={report.ForeignObjects} activeForeignObjects={report.ActiveForeignObjects} fragments={report.ForeignObjectFragments}");
        shell.WriteLine($"  damagedRegions={report.DamagedRegions}, brokenRegions={report.BrokenRegions}, unsplintedBrokenRegions={report.UnsplintedBrokenRegions}, damagedOrgans={report.DamagedOrgans}, detachedLimbs={report.DetachedLimbs}");
        shell.WriteLine($"  totalRegionDamage={FormatFixed(report.TotalRegionDamage)}, totalOrganDamage={FormatFixed(report.TotalOrganDamage)}");

        shell.WriteLine("Dirty flags:");
        shell.WriteLine($"  regions={report.DirtyRegions}, injuries={report.DirtyInjuries}, skeletal={report.DirtySkeletal}, organs={report.DirtyOrgans}");
        shell.WriteLine($"  bleeding={report.DirtyBleeding}, detachedLimbs={report.DirtyDetachedLimbs}, summary={report.DirtySummaryBodies}, foreignObjects={report.DirtyForeignObjects}");

        shell.WriteLine("Top medical performance bodies:");
        foreach (var body in report.Bodies
                     .OrderByDescending(body => body.Score)
                     .ThenByDescending(body => body.RevisionGap)
                     .ThenByDescending(body => body.MarkerCount)
                     .Take(topCount))
        {
            shell.WriteLine($"  {body.Name}: score={body.Score}, ledger={body.LedgerRevision}, summary={body.SummaryRevision}, gap={body.RevisionGap}, dirty={FormatDirtyFlags(body.DirtyFlags)}");
            shell.WriteLine($"    markers={FormatActivityFlags(body.MarkerActivity)}, stale={FormatActivityFlags(body.StaleMarkers)}, missing={FormatActivityFlags(body.MissingMarkers)}");
            shell.WriteLine($"    workload bleed={body.ActiveBleedSourceCount}/{body.BleedSourceCount} rate={FormatFixed(body.BleedRate)}/s, organs={body.SymptomaticOrganCount} worst={body.WorstOrganStatus}, bone={body.BoneKnittingRegionCount}, tourniquet={body.TourniquetRegionCount}, healing={body.HealingInjuryCount}");
            shell.WriteLine($"    records injuries={body.InjuryCount}, foreign={body.ActiveForeignObjectCount}/{body.ForeignObjectCount} fragments={body.ForeignObjectFragments}, damagedRegions={body.DamagedRegionCount}, broken={body.BrokenRegionCount}, unsplinted={body.UnsplintedBrokenRegionCount}, damage={FormatFixed(body.TotalRegionDamage)}");
            if (deep && body.SummaryDirty)
                shell.WriteLine($"    summaryProjection={(body.ProjectedSummaryChanged ? "would publish" : "unchanged")}, staleSummaryComponent={body.StaleSummaryComponent}");
        }
    }

    private string FormatEntity(EntityUid uid)
    {
        if (_entities.TryGetComponent(uid, out MetaDataComponent? meta))
            return $"{meta.EntityName}({uid})";

        return uid.ToString();
    }

    private static int CountDirtyFlags(MedicalDirtyFlags flags)
    {
        var count = 0;
        foreach (var flag in DirtyFlagValues)
        {
            if (flags.HasFlag(flag))
                count++;
        }

        return count;
    }

    private static int CountActivityFlags(MedicalActivityFlags flags)
    {
        var count = 0;
        foreach (var flag in ActivityFlagValues)
        {
            if (flags.HasFlag(flag))
                count++;
        }

        return count;
    }

    private static string FormatDirtyFlags(MedicalDirtyFlags flags)
    {
        return flags == MedicalDirtyFlags.None
            ? "none"
            : flags.ToString();
    }

    private static string FormatActivityFlags(MedicalActivityFlags flags)
    {
        return flags == MedicalActivityFlags.None
            ? "none"
            : flags.ToString();
    }

    private static string FormatFixed(FixedPoint2 value)
    {
        return value.Float().ToString("N2");
    }

    private static string FormatSeconds(FixedPoint2 value)
    {
        return value <= FixedPoint2.Zero
            ? "none"
            : $"{value.Float():N1}s";
    }

    private sealed class MedicalPerfReport
    {
        public readonly List<MedicalPerfBody> Bodies = new();

        public int TotalBodies;
        public int SummaryInitializedBodies;
        public int SummaryComponentBodies;
        public int StaleSummaryComponentBodies;
        public int DirtySummaryBodies;
        public int ProjectedSummaryChangedBodies;
        public int ProjectedSummaryUnchangedBodies;
        public int TotalLedgerRevisions;
        public int TotalSummaryRevisions;
        public int TotalRevisionGap;
        public int MaxRevisionGap;
        public int ActiveBleedingMarkers;
        public int ActiveOrganSymptomMarkers;
        public int ActiveBoneKnittingMarkers;
        public int ActiveSummaryDirtyMarkers;
        public int ActiveTourniquetMarkers;
        public int ActiveTreatedWoundHealingMarkers;
        public int ActiveUnsplintedFractureRiskMarkers;
        public int ActiveEmbeddedObjectMovementMarkers;
        public int StaleMarkerBodies;
        public int MissingMarkerBodies;
        public int Injuries;
        public int BleedSources;
        public int ActiveBleedSources;
        public int SymptomaticOrgans;
        public int BoneKnittingRegions;
        public int TourniquetRegions;
        public int HealingInjuries;
        public int ForeignObjects;
        public int ActiveForeignObjects;
        public int ForeignObjectFragments;
        public int DetachedLimbs;
        public int DamagedRegions;
        public int BrokenRegions;
        public int UnsplintedBrokenRegions;
        public int DamagedOrgans;
        public int DirtyRegions;
        public int DirtyInjuries;
        public int DirtySkeletal;
        public int DirtyOrgans;
        public int DirtyBleeding;
        public int DirtyDetachedLimbs;
        public int DirtyForeignObjects;
        public FixedPoint2 TotalBleedRate;
        public FixedPoint2 TotalBruteRecoveryRate;
        public FixedPoint2 TotalBurnRecoveryRate;
        public FixedPoint2 ShortestBoneKnitting;
        public FixedPoint2 ShortestTourniquetNecrosis;
        public FixedPoint2 TotalRegionDamage;
        public FixedPoint2 TotalOrganDamage;

        public void Add(MedicalPerfBody body)
        {
            Bodies.Add(body);
            TotalBodies++;

            if (body.SummaryInitialized)
                SummaryInitializedBodies++;

            if (body.HasSummaryComponent)
                SummaryComponentBodies++;

            if (body.StaleSummaryComponent)
                StaleSummaryComponentBodies++;

            if (body.SummaryDirty)
                DirtySummaryBodies++;

            if (body.ProjectedSummaryChanged)
                ProjectedSummaryChangedBodies++;

            if (body.ProjectedSummaryUnchanged)
                ProjectedSummaryUnchangedBodies++;

            TotalLedgerRevisions += body.LedgerRevision;
            TotalSummaryRevisions += body.SummaryRevision;
            TotalRevisionGap += body.RevisionGap;
            MaxRevisionGap = Math.Max(MaxRevisionGap, body.RevisionGap);

            AddMarkerCounts(body.MarkerActivity);
            if (body.StaleMarkers != MedicalActivityFlags.None)
                StaleMarkerBodies++;

            if (body.MissingMarkers != MedicalActivityFlags.None)
                MissingMarkerBodies++;

            Injuries += body.InjuryCount;
            BleedSources += body.BleedSourceCount;
            ActiveBleedSources += body.ActiveBleedSourceCount;
            SymptomaticOrgans += body.SymptomaticOrganCount;
            BoneKnittingRegions += body.BoneKnittingRegionCount;
            TourniquetRegions += body.TourniquetRegionCount;
            HealingInjuries += body.HealingInjuryCount;
            ForeignObjects += body.ForeignObjectCount;
            ActiveForeignObjects += body.ActiveForeignObjectCount;
            ForeignObjectFragments += body.ForeignObjectFragments;
            DetachedLimbs += body.DetachedLimbCount;
            DamagedRegions += body.DamagedRegionCount;
            BrokenRegions += body.BrokenRegionCount;
            UnsplintedBrokenRegions += body.UnsplintedBrokenRegionCount;
            DamagedOrgans += body.DamagedOrganCount;
            TotalBleedRate += body.BleedRate;
            TotalBruteRecoveryRate += body.BruteRecoveryRate;
            TotalBurnRecoveryRate += body.BurnRecoveryRate;
            TotalRegionDamage += body.TotalRegionDamage;
            TotalOrganDamage += body.TotalOrganDamage;

            if (body.BoneKnittingRegionCount > 0 &&
                (ShortestBoneKnitting <= FixedPoint2.Zero || body.ShortestBoneKnitting < ShortestBoneKnitting))
            {
                ShortestBoneKnitting = body.ShortestBoneKnitting;
            }

            if (body.TourniquetRegionCount > 0 &&
                (ShortestTourniquetNecrosis <= FixedPoint2.Zero || body.ShortestTourniquetNecrosis < ShortestTourniquetNecrosis))
            {
                ShortestTourniquetNecrosis = body.ShortestTourniquetNecrosis;
            }

            if (body.DirtyFlags.HasFlag(MedicalDirtyFlags.Regions))
                DirtyRegions++;

            if (body.DirtyFlags.HasFlag(MedicalDirtyFlags.Injuries))
                DirtyInjuries++;

            if (body.DirtyFlags.HasFlag(MedicalDirtyFlags.Skeletal))
                DirtySkeletal++;

            if (body.DirtyFlags.HasFlag(MedicalDirtyFlags.Organs))
                DirtyOrgans++;

            if (body.DirtyFlags.HasFlag(MedicalDirtyFlags.Bleeding))
                DirtyBleeding++;

            if (body.DirtyFlags.HasFlag(MedicalDirtyFlags.DetachedLimbs))
                DirtyDetachedLimbs++;

            if (body.DirtyFlags.HasFlag(MedicalDirtyFlags.ForeignObjects))
                DirtyForeignObjects++;
        }

        private void AddMarkerCounts(MedicalActivityFlags markerActivity)
        {
            if (markerActivity.HasFlag(MedicalActivityFlags.ActiveBleeding))
                ActiveBleedingMarkers++;

            if (markerActivity.HasFlag(MedicalActivityFlags.ActiveOrganSymptoms))
                ActiveOrganSymptomMarkers++;

            if (markerActivity.HasFlag(MedicalActivityFlags.ActiveBoneKnitting))
                ActiveBoneKnittingMarkers++;

            if (markerActivity.HasFlag(MedicalActivityFlags.ActiveMedicalSummaryDirty))
                ActiveSummaryDirtyMarkers++;

            if (markerActivity.HasFlag(MedicalActivityFlags.ActiveTourniquet))
                ActiveTourniquetMarkers++;

            if (markerActivity.HasFlag(MedicalActivityFlags.ActiveTreatedWoundHealing))
                ActiveTreatedWoundHealingMarkers++;

            if (markerActivity.HasFlag(MedicalActivityFlags.ActiveUnsplintedFractureRisk))
                ActiveUnsplintedFractureRiskMarkers++;

            if (markerActivity.HasFlag(MedicalActivityFlags.ActiveEmbeddedObjectMovement))
                ActiveEmbeddedObjectMovementMarkers++;
        }
    }

    private sealed class MedicalPerfBody
    {
        public readonly EntityUid Uid;
        public readonly string Name;

        public int Score;
        public int LedgerRevision;
        public int SummaryRevision;
        public int RevisionGap;
        public bool SummaryInitialized;
        public bool HasSummaryComponent;
        public bool StaleSummaryComponent;
        public bool SummaryDirty;
        public bool ProjectedSummaryChanged;
        public bool ProjectedSummaryUnchanged;
        public MedicalDirtyFlags DirtyFlags;
        public int DirtyFlagCount;
        public MedicalActivityFlags ExpectedActivity;
        public MedicalActivityFlags MarkerActivity;
        public MedicalActivityFlags StaleMarkers;
        public MedicalActivityFlags MissingMarkers;
        public int MarkerCount;
        public int StaleMarkerCount;
        public int MissingMarkerCount;
        public int InjuryCount;
        public int BleedSourceCount;
        public int ActiveBleedSourceCount;
        public int SymptomaticOrganCount;
        public OrganDamageStatus WorstOrganStatus;
        public int BoneKnittingRegionCount;
        public int TourniquetRegionCount;
        public int AppliedTourniquetRegionCount;
        public int HealingInjuryCount;
        public int ForeignObjectCount;
        public int ActiveForeignObjectCount;
        public int ForeignObjectFragments;
        public int DetachedLimbCount;
        public int DamagedRegionCount;
        public int BrokenRegionCount;
        public int UnsplintedBrokenRegionCount;
        public int DamagedOrganCount;
        public FixedPoint2 BleedRate;
        public FixedPoint2 BruteRecoveryRate;
        public FixedPoint2 BurnRecoveryRate;
        public FixedPoint2 ShortestBoneKnitting;
        public FixedPoint2 ShortestTourniquetNecrosis;
        public FixedPoint2 TotalRegionDamage;
        public FixedPoint2 TotalOrganDamage;

        public MedicalPerfBody(EntityUid uid, string name)
        {
            Uid = uid;
            Name = name;
        }
    }
}
