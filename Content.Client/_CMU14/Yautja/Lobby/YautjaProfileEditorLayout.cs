using System.Collections.Generic;
using Content.Shared._CMU14.Yautja;

namespace Content.Client._CMU14.Yautja.Lobby;

public enum YautjaProfileEditorCategory
{
    Appearance,
    Equipment,
    Sets,
    Technology,
    Description,
}

public sealed record YautjaProfileEditorCategoryInfo(
    YautjaProfileEditorCategory Id,
    string LocalizationKey);

public sealed record YautjaProfileEditorSummary(
    string Set,
    string Armor,
    string Mask,
    string Greaves,
    string Cape,
    string Bracer,
    string Caster);

public static class YautjaProfileEditorLayout
{
    public const int TechOptionSpacing = 6;
    public const int TechOptionBottomMargin = 12;

    public static IReadOnlyList<YautjaProfileEditorCategoryInfo> Categories { get; } =
    [
        new(YautjaProfileEditorCategory.Appearance, "cmu-yautja-lobby-category-appearance"),
        new(YautjaProfileEditorCategory.Equipment, "cmu-yautja-lobby-category-equipment"),
        new(YautjaProfileEditorCategory.Sets, "cmu-yautja-lobby-category-sets"),
        new(YautjaProfileEditorCategory.Technology, "cmu-yautja-lobby-category-technology"),
        new(YautjaProfileEditorCategory.Description, "cmu-yautja-lobby-category-description"),
    ];

    public static bool IsUniqueSetLocked(YautjaCharacterProfile profile, YautjaUniqueSet unique)
    {
        return unique != YautjaUniqueSet.None && !YautjaRankResolver.CanUseUnique(profile);
    }

    public static bool IsUniqueSetLocked(YautjaProfileCapabilities capabilities, YautjaUniqueSet unique)
    {
        return unique != YautjaUniqueSet.None && !capabilities.CanUseUnique;
    }

    public static bool IsLegacySetLocked(YautjaProfileCapabilities capabilities, YautjaLegacySet legacy)
    {
        return !capabilities.CanUseLegacySet(legacy);
    }

    public static bool IsCapeLocked(YautjaProfileCapabilities capabilities, YautjaCapeStyle cape)
    {
        return !capabilities.CanUseCape(cape);
    }

    public static bool IsBracerLocked(YautjaProfileCapabilities capabilities, YautjaBracerMaterial bracer)
    {
        return !capabilities.CanUseBracer(bracer);
    }

    public static YautjaProfileEditorSummary BuildSummary(YautjaCharacterProfile profile)
    {
        var set = profile.Unique != YautjaUniqueSet.None
            ? YautjaCharacterProfile.GetUniqueDisplayName(profile.Unique)
            : profile.Legacy != YautjaLegacySet.None
                ? YautjaCharacterProfile.GetLegacyDisplayName(profile.Legacy)
                : "—";

        return new YautjaProfileEditorSummary(
            set,
            YautjaCharacterProfile.GetArmorStyleDisplayName(profile.ArmorMaterial, profile.ArmorStyle),
            YautjaCharacterProfile.GetMaskStyleDisplayName(profile.MaskMaterial, profile.MaskStyle),
            YautjaCharacterProfile.GetGreavesStyleDisplayName(profile.GreavesMaterial, profile.GreavesStyle),
            YautjaCharacterProfile.GetCapeDisplayName(profile.CapeStyle),
            YautjaCharacterProfile.GetBracerDisplayName(profile.BracerMaterial),
            YautjaCharacterProfile.GetCasterDisplayName(profile.CasterMaterial));
    }

    public static bool IsCategoryActive(
        YautjaProfileEditorCategory active,
        YautjaProfileEditorCategory candidate)
    {
        return active == candidate;
    }

    public static int GetResponsiveColumnCount(float availableWidth, int preferredColumns)
    {
        const float cardWidth = 108;
        const float separation = 8;

        if (preferredColumns <= 0 || availableWidth <= 0)
            return 1;

        var columns = (int) MathF.Floor((availableWidth + separation) / (cardWidth + separation));
        return Math.Clamp(columns, 1, preferredColumns);
    }
}
