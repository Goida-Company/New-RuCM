using Content.Client.Lobby.UI;
using NUnit.Framework;

namespace Content.Tests.Client.Lobby;

[TestFixture]
public sealed class LobbyTerminalBackgroundTest
{
    [TestCase(true, true, true, 0f, LobbyTerminalMode.InProgress)]
    [TestCase(false, true, true, 5f, LobbyTerminalMode.Paused)]
    [TestCase(false, false, false, -1f, LobbyTerminalMode.Imminent)]
    [TestCase(false, false, false, 10f, LobbyTerminalMode.Imminent)]
    [TestCase(false, false, false, 10.01f, LobbyTerminalMode.Countdown)]
    [TestCase(false, false, false, 30f, LobbyTerminalMode.Countdown)]
    [TestCase(false, false, false, 30.01f, LobbyTerminalMode.Waiting)]
    [TestCase(false, false, true, 45f, LobbyTerminalMode.Ready)]
    public void ResolveModeUsesExpectedPriority(
        bool gameStarted,
        bool paused,
        bool ready,
        float remainingSeconds,
        LobbyTerminalMode expected)
    {
        var actual = LobbyTerminalBackground.ResolveMode(gameStarted, paused, ready, remainingSeconds);

        Assert.That(actual, Is.EqualTo(expected));
    }
}
