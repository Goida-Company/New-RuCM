using System;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Moq;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Tests.Server.Administration;

[TestFixture]
public sealed class AdminManagerTest : ContentUnitTest
{
    [Test]
    public async Task HostLoginKeepsAdminRankOocColor()
    {
        var userId = new NetUserId(Guid.Parse("00000000-0000-0000-0000-000000000123"));
        var channel = new Mock<INetChannel>();
        channel.SetupGet(value => value.RemoteEndPoint)
            .Returns(new IPEndPoint(IPAddress.Loopback, 1212));

        var session = new Mock<ICommonSession>();
        session.SetupGet(value => value.Channel).Returns(channel.Object);
        session.SetupGet(value => value.UserId).Returns(userId);
        session.SetupGet(value => value.Name).Returns("host");

        var config = new Mock<IConfigurationManager>();
        config.Setup(value => value.GetCVar(CCVars.ConsoleLoginLocal)).Returns(true);
        config.Setup(value => value.GetCVar(CCVars.ConsoleLoginHostUser)).Returns(string.Empty);

        var db = new Mock<IServerDbManager>();
        db.Setup(value => value.GetAdminDataForAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Admin
            {
                UserId = userId.UserId,
                AdminRankId = 42,
                AdminRank = new AdminRank
                {
                    Id = 42,
                    Name = "Host group",
                    OOCColor = "#FFD700FF",
                },
            });

        var manager = new AdminManager();
        SetPrivateField(manager, "_cfg", config.Object);
        SetPrivateField(manager, "_dbManager", db.Object);

        var method = typeof(AdminManager).GetMethod(
            "LoadAdminDataCore",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        var task = (Task) method!.Invoke(manager, new object[] { session.Object })!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task);
        Assert.That(result, Is.Not.Null);

        var data = (AdminData) result!.GetType().GetField("Item1")!.GetValue(result)!;
        var rankId = (int?) result.GetType().GetField("Item2")!.GetValue(result);
        var specialLogin = (bool) result.GetType().GetField("Item3")!.GetValue(result)!;

        Assert.Multiple(() =>
        {
            Assert.That(data.OOCColor, Is.EqualTo("#FFD700FF"));
            Assert.That(rankId, Is.EqualTo(42));
            Assert.That(specialLogin, Is.True);
        });
    }

    private static void SetPrivateField(object instance, string name, object value)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(instance, value);
    }
}
