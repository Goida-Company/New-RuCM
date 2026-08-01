using System.Linq;
using System.Text;
// RuMC edit start
using Robust.Shared.IoC;
using Robust.Shared.Localization;
// RuMC edit end
using Robust.Shared.Maths;

namespace Content.Shared._CMU14.CharacterDescription;

public static class NamedColorHelper
{
    private static readonly (string Id, Color Color)[] NamedColors = BuildPalette(); // RuMC edit

    private static (string Id, Color Color)[] BuildPalette() // RuMC edit
    {
        return Color.GetAllDefaultColors()
            .Where(pair => pair.Key != "transparent")
            .Select(pair => (Id: pair.Key, pair.Value)) // RuMC edit
            .ToArray();
    }

    private static string Capitalize(string name)
    {
        if (name.Length == 0)
            return name;

        var builder = new StringBuilder(name.Length);
        builder.Append(char.ToUpperInvariant(name[0]));
        builder.Append(name, 1, name.Length - 1);
        return builder.ToString();
    }

    public static string NearestColorName(Color color)
    {
        var bestId = "unknown"; // RuMC edit
        var bestDistance = float.MaxValue;

        foreach (var (id, named) in NamedColors) // RuMC edit
        {
            var dr = color.R - named.R;
            var dg = color.G - named.G;
            var db = color.B - named.B;
            var distance = dr * dr + dg * dg + db * db;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestId = id; // RuMC edit
            }
        }

        // RuMC edit start
        var loc = IoCManager.Resolve<ILocalizationManager>();
        return loc.TryGetString($"color-name-{bestId}", out var localized) ? localized : Capitalize(bestId);
        // RuMC edit end
    }
}
