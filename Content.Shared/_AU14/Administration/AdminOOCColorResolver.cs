using Content.Shared.Administration;
using Content.Shared.Preferences;

namespace Content.Shared._AU14.Administration;

public static class AdminOOCColorResolver
{
    public static Color? Resolve(AdminData? admin, PlayerPreferences? preferences)
    {
        if (admin?.OOCColor is { } groupColor && Color.TryFromHex(groupColor) is { } parsedGroupColor)
            return parsedGroupColor;

        if (admin?.HasFlag(AdminFlags.NameColor) == true)
            return preferences?.AdminOOCColor;

        return null;
    }
}
