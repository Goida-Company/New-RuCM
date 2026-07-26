using System.Collections.Generic;
using Content.Shared.Administration;
using Content.Shared.Preferences;
using Content.Shared._AU14.Administration;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared.Administration;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class AdminOOCColorResolverTest
{
    [Test]
    public void GroupColorWinsOverPersonalColor()
    {
        var admin = new AdminData
        {
            Active = true,
            Flags = AdminFlags.None,
            OOCColor = "#12abef"
        };
        var preferences = CreatePreferences(Color.Red);

        var result = AdminOOCColorResolver.Resolve(admin, preferences);

        Assert.That(result?.ToHex(), Is.EqualTo("#12ABEFFF"));
    }

    [Test]
    public void PersonalColorIsFallbackForNameColorAdmins()
    {
        var admin = new AdminData
        {
            Active = true,
            Flags = AdminFlags.NameColor
        };
        var preferences = CreatePreferences(Color.Red);

        var result = AdminOOCColorResolver.Resolve(admin, preferences);

        Assert.That(result, Is.EqualTo(Color.Red));
    }

    [Test]
    public void NoColorIsReturnedWithoutGroupOrPersonalPermission()
    {
        var admin = new AdminData
        {
            Active = true,
            Flags = AdminFlags.None
        };
        var preferences = CreatePreferences(Color.Red);

        var result = AdminOOCColorResolver.Resolve(admin, preferences);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void InvalidGroupColorFallsBackToPersonalColor()
    {
        var admin = new AdminData
        {
            Active = true,
            Flags = AdminFlags.NameColor,
            OOCColor = "not-a-color"
        };
        var preferences = CreatePreferences(Color.Red);

        var result = AdminOOCColorResolver.Resolve(admin, preferences);

        Assert.That(result, Is.EqualTo(Color.Red));
    }

    private static PlayerPreferences CreatePreferences(Color color)
    {
        return new PlayerPreferences(
            new Dictionary<int, ICharacterProfile>(),
            0,
            color,
            []);
    }
}
