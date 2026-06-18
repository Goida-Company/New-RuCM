using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Server._CMU14.Medical.Human;

[TestFixture]
public sealed class CMUMedicalPerfServerCommandTest
{
    [Test]
    public void MedicalPerfServerCommandReportsAuthoritativeLedgerHotspots()
    {
        var text = ReadCommand();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("[AdminCommand(AdminFlags.Debug)]"));
            Assert.That(text, Does.Contain("Command => \"cmu_medical_perf_server\""));
            Assert.That(text, Does.Contain("deep"));
            Assert.That(text, Does.Contain("Top medical performance bodies"));
            Assert.That(text, Does.Contain("MedicalDirtyFlags.Summary"));
            Assert.That(text, Does.Contain("MedicalSummaryBuilder.BuildForCurrentRevision"));
            Assert.That(text, Does.Contain("MedicalSummaryBuilder.ProjectionEquals"));
            Assert.That(text, Does.Contain("HumanBleedingSystem.CalculateBleedingTick"));
            Assert.That(text, Does.Contain("HumanOrganSymptomSystem.CalculateOrganSymptomTick"));
            Assert.That(text, Does.Contain("HumanBoneKnittingSystem.CalculateBoneKnittingTick"));
            Assert.That(text, Does.Contain("HumanTourniquetSystem.CalculateTourniquetTick"));
            Assert.That(text, Does.Contain("HumanTreatedWoundHealingSystem.CalculateTreatedWoundHealingTick"));
            Assert.That(text, Does.Contain("ActiveBleedingComponent"));
            Assert.That(text, Does.Contain("ActiveOrganSymptomsComponent"));
            Assert.That(text, Does.Contain("ActiveBoneKnittingComponent"));
            Assert.That(text, Does.Contain("ActiveTourniquetComponent"));
            Assert.That(text, Does.Contain("ActiveTreatedWoundHealingComponent"));
            Assert.That(text, Does.Contain("ActiveMedicalSummaryDirtyComponent"));
        });
    }

    private static string ReadCommand()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "Content.Server",
            "_CMU14",
            "Medical",
            "Foundation",
            "Telemetry",
            "CMUMedicalPerfServerCommand.cs");

        Assert.That(File.Exists(path), Is.True);
        return File.ReadAllText(path);
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
