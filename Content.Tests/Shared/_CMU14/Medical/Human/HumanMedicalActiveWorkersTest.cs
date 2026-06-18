using System;
using System.IO;
using Content.Shared._CMU14.Medical.Human.Components;
using Content.Shared._CMU14.Medical.Human.Data;
using Content.Shared._CMU14.Medical.Human.Diagnostics;
using Content.Shared._CMU14.Medical.Human.Systems;
using Content.Shared._CMU14.Medical.Human.Care;
using Content.Shared.FixedPoint;
using NUnit.Framework;

namespace Content.Tests.Shared._CMU14.Medical.Human;

[TestFixture]
public sealed class HumanMedicalActiveWorkersTest
{
    [Test]
    public void AddingFirstActiveBleedRequestsBleedingMarker()
    {
        var medical = HumanMedicalLedger.CreateDefault();
        var transaction = new MedicalTransaction(BodyRegion.Chest);
        transaction.Add(MedicalEffect.AddBleedSource(
            BodyRegion.Chest,
            BleedKind.Internal,
            FixedPoint2.New(1)));

        var result = HumanMedicalLedger.ApplyTransaction(medical, transaction);
        var activity = MedicalActivityClassifier.Classify(medical);
        var tick = HumanBleedingSystem.CalculateBleedingTick(medical);

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied, Is.True);
            Assert.That(activity.HasFlag(MedicalActivityFlags.ActiveBleeding), Is.True);
            Assert.That(tick.ActiveSources, Is.EqualTo(1));
            Assert.That(tick.TotalRate, Is.EqualTo(FixedPoint2.New(1)));
        });
    }

    [Test]
    public void RemovingLastActiveBleedClearsBleedingMarker()
    {
        var medical = HumanMedicalLedger.CreateDefault();
        var transaction = new MedicalTransaction(BodyRegion.Chest);
        transaction.Add(MedicalEffect.AddBleedSource(
            BodyRegion.Chest,
            BleedKind.Internal,
            FixedPoint2.New(1)));
        HumanMedicalLedger.ApplyTransaction(medical, transaction);

        var bleed = medical.BleedSources[0];
        bleed.Treatment = TreatmentFlags.Closed;
        medical.BleedSources[0] = bleed;

        var activity = MedicalActivityClassifier.Classify(medical);
        var tick = HumanBleedingSystem.CalculateBleedingTick(medical);

        Assert.Multiple(() =>
        {
            Assert.That(activity.HasFlag(MedicalActivityFlags.ActiveBleeding), Is.False);
            Assert.That(tick.ActiveSources, Is.Zero);
            Assert.That(tick.TotalRate, Is.EqualTo(FixedPoint2.Zero));
        });
    }

    [Test]
    public void OrganDamageWithoutStasisRequestsOrganSymptomMarker()
    {
        var medical = HumanMedicalLedger.CreateDefault();
        var transaction = new MedicalTransaction(BodyRegion.Chest);
        transaction.Add(MedicalEffect.AddOrganDamage(
            OrganSlot.Heart,
            FixedPoint2.New(30)));
        HumanMedicalLedger.ApplyTransaction(medical, transaction);

        var activity = MedicalActivityClassifier.Classify(medical);
        var tick = HumanOrganSymptomSystem.CalculateOrganSymptomTick(medical);

        Assert.Multiple(() =>
        {
            Assert.That(activity.HasFlag(MedicalActivityFlags.ActiveOrganSymptoms), Is.True);
            Assert.That(tick.SymptomaticOrgans, Is.EqualTo(1));
            Assert.That(tick.WorstStatus, Is.EqualTo(OrganDamageStatus.Broken));
        });
    }

    [Test]
    public void OrganStasisSuppressesSymptomsWithoutDeletingDamage()
    {
        var medical = HumanMedicalLedger.CreateDefault();
        var transaction = new MedicalTransaction(BodyRegion.Chest);
        transaction.Add(MedicalEffect.AddOrganDamage(
            OrganSlot.Heart,
            FixedPoint2.New(30)));
        HumanMedicalLedger.ApplyTransaction(medical, transaction);

        var heart = medical.Organs[(int) OrganSlot.Heart];
        heart.Flags |= OrganFlags.Stasis;
        medical.Organs[(int) OrganSlot.Heart] = heart;

        var activity = MedicalActivityClassifier.Classify(medical);
        var tick = HumanOrganSymptomSystem.CalculateOrganSymptomTick(medical);

        Assert.Multiple(() =>
        {
            Assert.That(activity.HasFlag(MedicalActivityFlags.ActiveOrganSymptoms), Is.False);
            Assert.That(HumanMedicalLedger.GetOrgan(medical, OrganSlot.Heart).Damage, Is.EqualTo(FixedPoint2.New(30)));
            Assert.That(tick.SymptomaticOrgans, Is.Zero);
        });
    }

    [Test]
    public void KnittingBoneStateRequestsBoneKnittingMarkerAndAdvancesRecovery()
    {
        var medical = HumanMedicalLedger.CreateDefault();
        var region = medical.Regions[(int) BodyRegion.LeftArm];
        region.Skeletal.Broken = true;
        region.Skeletal.Knitting = true;
        region.Skeletal.KnittingSecondsRemaining = FixedPoint2.New(10);
        medical.Regions[(int) BodyRegion.LeftArm] = region;

        var before = MedicalActivityClassifier.Classify(medical);
        var result = HumanMedicalLedger.AdvanceBoneKnitting(medical, FixedPoint2.New(10));
        var after = MedicalActivityClassifier.Classify(medical);
        var healed = HumanMedicalLedger.GetRegion(medical, BodyRegion.LeftArm);

        Assert.Multiple(() =>
        {
            Assert.That(before.HasFlag(MedicalActivityFlags.ActiveBoneKnitting), Is.True);
            Assert.That(result.Applied, Is.True);
            Assert.That(healed.Skeletal.Broken, Is.False);
            Assert.That(healed.Skeletal.Knitting, Is.False);
            Assert.That(after.HasFlag(MedicalActivityFlags.ActiveBoneKnitting), Is.False);
        });
    }

    [Test]
    public void TourniquetStateRequestsMarkerUntilNecrotic()
    {
        var medical = HumanMedicalLedger.CreateDefault();

        var apply = HumanTreatmentSystem.TryApplyTreatment(
            medical,
            new TreatmentAttempt(
                TreatmentKind.ApplyTourniquet,
                BodyRegion.RightLeg,
                Amount: FixedPoint2.New(2)));
        var before = MedicalActivityClassifier.Classify(medical);
        var result = HumanMedicalLedger.AdvanceTourniquets(medical, FixedPoint2.New(2));
        var after = MedicalActivityClassifier.Classify(medical);
        var region = HumanMedicalLedger.GetRegion(medical, BodyRegion.RightLeg);

        Assert.Multiple(() =>
        {
            Assert.That(apply.Applied, Is.True);
            Assert.That(before.HasFlag(MedicalActivityFlags.ActiveTourniquet), Is.True);
            Assert.That(result.Applied, Is.True);
            Assert.That(region.Tourniquet.Applied, Is.True);
            Assert.That(region.Tourniquet.Necrotic, Is.True);
            Assert.That(after.HasFlag(MedicalActivityFlags.ActiveTourniquet), Is.False);
        });
    }

    [Test]
    public void TreatedWoundsRequestHealingMarkerUntilRecovered()
    {
        var medical = HumanMedicalLedger.CreateDefault();
        var transaction = new MedicalTransaction(BodyRegion.RightArm);
        transaction.Add(MedicalEffect.AddRegionDamage(
            BodyRegion.RightArm,
            FixedPoint2.New(8),
            FixedPoint2.Zero));
        transaction.Add(MedicalEffect.AddInjury(
            BodyRegion.RightArm,
            InjuryKind.Cut,
            InjuryStage.Small,
            FixedPoint2.New(8)));
        HumanMedicalLedger.ApplyTransaction(medical, transaction);

        HumanTreatmentSystem.TryApplyTreatment(
            medical,
            new TreatmentAttempt(TreatmentKind.Gauze, BodyRegion.RightArm, InjuryId: medical.Injuries[0].Id));

        var before = MedicalActivityClassifier.Classify(medical);
        var tick = HumanTreatedWoundHealingSystem.CalculateTreatedWoundHealingTick(medical);
        var result = HumanMedicalLedger.AdvanceTreatedWoundHealing(medical, FixedPoint2.New(60));
        var after = MedicalActivityClassifier.Classify(medical);

        Assert.Multiple(() =>
        {
            Assert.That(before.HasFlag(MedicalActivityFlags.ActiveTreatedWoundHealing), Is.True);
            Assert.That(tick.ActiveInjuries, Is.EqualTo(1));
            Assert.That(result.Applied, Is.True);
            Assert.That(HumanMedicalLedger.GetRegion(medical, BodyRegion.RightArm).BruteDamage, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(medical.Injuries, Is.Empty);
            Assert.That(after.HasFlag(MedicalActivityFlags.ActiveTreatedWoundHealing), Is.False);
        });
    }

    [Test]
    public void HealthyBodiesHaveNoActiveWorkerActivity()
    {
        var medical = HumanMedicalLedger.CreateDefault();

        var activity = MedicalActivityClassifier.Classify(medical);

        Assert.Multiple(() =>
        {
            Assert.That(activity, Is.EqualTo(MedicalActivityFlags.None));
            Assert.That(HumanBleedingSystem.CalculateBleedingTick(medical).ActiveSources, Is.Zero);
            Assert.That(HumanOrganSymptomSystem.CalculateOrganSymptomTick(medical).SymptomaticOrgans, Is.Zero);
            Assert.That(HumanBoneKnittingSystem.CalculateBoneKnittingTick(medical).ActiveRegions, Is.Zero);
            Assert.That(HumanTourniquetSystem.CalculateTourniquetTick(medical).ActiveRegions, Is.Zero);
            Assert.That(HumanTreatedWoundHealingSystem.CalculateTreatedWoundHealingTick(medical).ActiveInjuries, Is.Zero);
        });
    }

    [Test]
    public void SlowLedgerWorkerTimingCoalescesFrameTicks()
    {
        var lastUpdate = TimeSpan.Zero;
        var nextUpdate = TimeSpan.Zero;

        var first = HumanMedicalWorkerTiming.TryGetElapsed(
            TimeSpan.FromSeconds(100),
            ref lastUpdate,
            ref nextUpdate,
            out var firstElapsed);
        var early = HumanMedicalWorkerTiming.TryGetElapsed(
            TimeSpan.FromSeconds(100.5),
            ref lastUpdate,
            ref nextUpdate,
            out var earlyElapsed);
        var due = HumanMedicalWorkerTiming.TryGetElapsed(
            TimeSpan.FromSeconds(101.25),
            ref lastUpdate,
            ref nextUpdate,
            out var dueElapsed);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False);
            Assert.That(firstElapsed, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(early, Is.False);
            Assert.That(earlyElapsed, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(due, Is.True);
            Assert.That(dueElapsed, Is.EqualTo(FixedPoint2.New(1.25f)));
            Assert.That(lastUpdate, Is.EqualTo(TimeSpan.FromSeconds(101.25)));
            Assert.That(nextUpdate, Is.EqualTo(TimeSpan.FromSeconds(102.25)));
        });
    }

    [Test]
    public void SlowLedgerWorkerTimingCanStartAtZero()
    {
        var lastUpdate = TimeSpan.Zero;
        var nextUpdate = TimeSpan.Zero;

        var first = HumanMedicalWorkerTiming.TryGetElapsed(
            TimeSpan.Zero,
            ref lastUpdate,
            ref nextUpdate,
            out var firstElapsed);
        var due = HumanMedicalWorkerTiming.TryGetElapsed(
            TimeSpan.FromSeconds(1),
            ref lastUpdate,
            ref nextUpdate,
            out var dueElapsed);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False);
            Assert.That(firstElapsed, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(due, Is.True);
            Assert.That(dueElapsed, Is.EqualTo(FixedPoint2.New(1)));
            Assert.That(lastUpdate, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(nextUpdate, Is.EqualTo(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void SlowLedgerWorkersUseTimingGateBeforeApplyingRevisions()
    {
        AssertSlowWorkerTimingGate("HumanBoneKnittingSystem.cs");
        AssertSlowWorkerTimingGate("HumanTourniquetSystem.cs");
        AssertSlowWorkerTimingGate("HumanTreatedWoundHealingSystem.cs");
    }

    [Test]
    public void UnsplintedBrokenRegionRequestsMovementRiskMarker()
    {
        var medical = HumanMedicalLedger.CreateDefault();
        var leg = medical.Regions[(int) BodyRegion.LeftLeg];
        leg.Skeletal.Broken = true;
        medical.Regions[(int) BodyRegion.LeftLeg] = leg;

        var activity = MedicalActivityClassifier.Classify(medical);

        Assert.That(activity.HasFlag(MedicalActivityFlags.ActiveUnsplintedFractureRisk), Is.True);
    }

    [Test]
    public void ActiveWorkersUseMarkerOnlyQueriesAndNoLegacyComponents()
    {
        AssertWorkerQuery(
            "HumanBleedingSystem.cs",
            "EntityQueryEnumerator<HumanMedicalComponent, ActiveBleedingComponent>()",
            Legacy("Internal", "Bleeding", "Component"));
        AssertWorkerQuery(
            "HumanOrganSymptomSystem.cs",
            "EntityQueryEnumerator<HumanMedicalComponent, ActiveOrganSymptomsComponent>()",
            Legacy("Organ", "Health", "Component"));
        AssertWorkerQuery(
            "HumanBoneKnittingSystem.cs",
            "EntityQueryEnumerator<HumanMedicalComponent, ActiveBoneKnittingComponent>()",
            Legacy("Fracture", "Component"));
        AssertWorkerQuery(
            "HumanTourniquetSystem.cs",
            "EntityQueryEnumerator<HumanMedicalComponent, ActiveTourniquetComponent>()",
            "CMUTourniquetComponent");
        AssertWorkerQuery(
            "HumanTreatedWoundHealingSystem.cs",
            "EntityQueryEnumerator<HumanMedicalComponent, ActiveTreatedWoundHealingComponent>()",
            "WoundableComponent");
    }

    private static string Legacy(params string[] parts)
    {
        return string.Concat(parts);
    }

    [Test]
    public void HumanMedicalShutdownClearsActiveWorkerMarkers()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "Content.Shared",
            "_CMU14",
            "Medical",
            "Human",
            "Systems",
            "SharedHumanMedicalSystem.cs");

        Assert.That(File.Exists(path), Is.True);
        var text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("ClearActiveMarkers(body.Owner);"));
            Assert.That(text, Does.Contain("RemComp<ActiveBleedingComponent>"));
            Assert.That(text, Does.Contain("RemComp<ActiveOrganSymptomsComponent>"));
            Assert.That(text, Does.Contain("RemComp<ActiveBoneKnittingComponent>"));
            Assert.That(text, Does.Contain("RemComp<ActiveUnsplintedFractureRiskComponent>"));
            Assert.That(text, Does.Contain("RemComp<ActiveEmbeddedObjectMovementComponent>"));
            Assert.That(text, Does.Contain("RemComp<ActiveTourniquetComponent>"));
            Assert.That(text, Does.Contain("RemComp<ActiveTreatedWoundHealingComponent>"));
            Assert.That(text, Does.Contain("RemComp<ActiveMedicalSummaryDirtyComponent>"));
        });
    }

    [Test]
    public void ActiveBleedingWorkerHasServerBloodstreamSink()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "Content.Server",
            "_CMU14",
            "Medical",
            "Human",
            "Systems",
            "HumanBleedingBloodstreamSystem.cs");

        Assert.That(File.Exists(path), Is.True);
        var text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("SubscribeLocalEvent<HumanBleedingTickEvent>"));
            Assert.That(text, Does.Contain("SubscribeLocalEvent<HumanMedicalLedgerChangedEvent>"));
            Assert.That(text, Does.Contain("TrySetBleedAmount"));
        });
    }

    [Test]
    public void LedgerDamageableBridgeProjectsFromLedgerChangedEvent()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "Content.Shared",
            "_CMU14",
            "Medical",
            "Human",
            "Systems",
            "HumanMedicalDamageableBridgeSystem.cs");

        Assert.That(File.Exists(path), Is.True);
        var text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("SubscribeLocalEvent<HumanMedicalLedgerChangedEvent>"));
            Assert.That(text, Does.Contain("SetDamage"));
            Assert.That(text, Does.Contain("BuildProjectedDamage"));
            Assert.That(text, Does.Not.Contain("TryChangeDamage"));
        });
    }

    [Test]
    public void MedicalSummaryRefreshDoesNotDirtyUnchangedPresentationState()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "Content.Shared",
            "_CMU14",
            "Medical",
            "Human",
            "Systems",
            "SharedHumanMedicalSystem.cs");

        Assert.That(File.Exists(path), Is.True);
        var text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("summary.Summary != medical.Summary"));
            Assert.That(text, Does.Contain("var changed = false;"));
            Assert.That(text, Does.Not.Contain("var changed = visuals.Revision != medical.Revision;"));
            Assert.That(text, Does.Not.Contain("RefreshVisuals(body.Owner, body.Comp);\r\n\r\n        var ev = new HumanMedicalLedgerChangedEvent"));
        });
    }

    [Test]
    public void PainFeedbackActivatesFromPainShockStartupEventWithoutDuplicateComponentStartup()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "Content.Server",
            "_CMU14",
            "Medical",
            "Human",
            "Effects",
            "CMUPainFeedbackSystem.cs");

        Assert.That(File.Exists(path), Is.True);
        var text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("SubscribeLocalEvent<PainShockStartupEvent>(OnPainStartup);"));
            Assert.That(text, Does.Contain("RefreshPainFeedbackActivity(args.Body);"));
            Assert.That(text, Does.Not.Contain("SubscribeLocalEvent<PainShockComponent, ComponentStartup>"));
        });
    }

    private static void AssertSlowWorkerTimingGate(string fileName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "Content.Shared",
            "_CMU14",
            "Medical",
            "Human",
            "Systems",
            fileName);

        Assert.That(File.Exists(path), Is.True);
        var text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("HumanMedicalWorkerTiming.TryGetElapsed"));
            Assert.That(text, Does.Not.Contain("FixedPoint2.New(frameTime)"));
        });
    }

    private static void AssertWorkerQuery(
        string fileName,
        string requiredQuery,
        string forbiddenTerm)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "Content.Shared",
            "_CMU14",
            "Medical",
            "Human",
            "Systems",
            fileName);

        Assert.That(File.Exists(path), Is.True);
        var text = File.ReadAllText(path);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(requiredQuery));
            Assert.That(text, Does.Not.Contain(forbiddenTerm));
        });
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpaceStation14.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing SpaceStation14.slnx.");
    }
}
