using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Content.Client._RMC14.Dialog;
using NUnit.Framework;

namespace Content.Tests.Client._RMC14.Dialog;

[TestFixture]
[TestOf(typeof(RMCDialogOptionsContainer))]
public sealed class RMCDialogOptionsContainerTest
{
    [Test]
    public void OptionsUseBoundedVerticalScrollContainer()
    {
        var markupPath = FindRepositoryFile("Content.Client", "_RMC14", "Dialog", "RMCDialogOptionsContainer.xaml");
        var document = XDocument.Load(markupPath);
        var options = document.Descendants().Single(element => element.Attribute("Name")?.Value == "Options");
        var scroll = options.Parent;

        Assert.That(scroll?.Name.LocalName, Is.EqualTo("ScrollContainer"));
        Assert.That(scroll?.Attribute("HScrollEnabled")?.Value, Is.EqualTo("False"));
        Assert.That(scroll?.Attribute("VScrollEnabled")?.Value, Is.EqualTo("True"));
        Assert.That(scroll?.Attribute("ReturnMeasure")?.Value, Is.EqualTo("True"));
        Assert.That(
            float.Parse(scroll?.Attribute("MaxHeight")?.Value ?? "0", CultureInfo.InvariantCulture),
            Is.EqualTo(250));
    }

    private static string FindRepositoryFile(params string[] path)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpaceStation14.slnx")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "Could not find the repository root from the test directory.");
        return Path.Combine(directory!.FullName, Path.Combine(path));
    }
}
