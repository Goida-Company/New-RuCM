using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client.Lobby.UI;

/// <summary>
/// A texture-free, procedural operations display used behind the lobby controls.
/// </summary>
public sealed partial class LobbyTerminalBackground : Control
{
    private const int HighQualityStarCount = 72;
    private const int LowQualityStarCount = 28;
    private const float CursorParallaxPixels = 60f;
    private const float MotionSmoothing = 8f;
    private const float DefaultLeftConsoleWidth = 360f;

    private static readonly Color CountdownColor = Color.FromHex("#F3C969");
    private static readonly Color ImminentColor = Color.FromHex("#FF6B62");
    private static readonly Color InProgressColor = Color.FromHex("#72D7FF");

    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Star[] _stars = new Star[HighQualityStarCount];
    private TimeSpan _bootStartedAt;

    private Vector2 _parallaxOffset;
    private LobbyTerminalMode _mode;

    public float ReservedLeftWidth { get; set; } = DefaultLeftConsoleWidth;
    public float ReservedRightWidth { get; set; }
    public LobbyTerminalMode Mode => _mode;
    public Color AccentColor => GetAccentColor();

    private const int MaxEllipseSegments = 48;
    private readonly Vector2[] _ellipsePoints = new Vector2[MaxEllipseSegments + 1];

    public LobbyTerminalBackground()
    {
        IoCManager.InjectDependencies(this);

        RectClipContent = true;
        MouseFilter = MouseFilterMode.Ignore;
        CanKeyboardFocus = false;
        _bootStartedAt = _timing.RealTime;

        GenerateStars();
    }

    public void RestartBootSequence()
    {
        _bootStartedAt = _timing.RealTime;
        _parallaxOffset = Vector2.Zero;
    }

    public void SetLobbyState(bool gameStarted, bool paused, bool ready, TimeSpan? timeRemaining)
    {
        var remainingSeconds = timeRemaining is { } remaining
            ? MathF.Max(0f, (float) remaining.TotalSeconds)
            : float.PositiveInfinity;

        _mode = ResolveMode(gameStarted, paused, ready, remainingSeconds);
    }

    public static LobbyTerminalMode ResolveMode(bool gameStarted, bool paused, bool ready, float remainingSeconds)
    {
        if (gameStarted)
            return LobbyTerminalMode.InProgress;

        if (paused)
            return LobbyTerminalMode.Paused;

        if (remainingSeconds <= 10f)
            return LobbyTerminalMode.Imminent;

        if (remainingSeconds <= 30f)
            return LobbyTerminalMode.Countdown;

        return ready ? LobbyTerminalMode.Ready : LobbyTerminalMode.Waiting;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!VisibleInTree)
            return;

        var motionEnabled = !_configuration.GetCVar(CCVars.ReducedMotion)
            && _configuration.GetCVar(CCVars.ParallaxEnabled);

        var target = Vector2.Zero;
        if (motionEnabled && Size.X > 0f && Size.Y > 0f)
        {
            var localMouse = UserInterfaceManager.MousePositionScaled.Position * UIScale - GlobalPixelPosition;
            var normalized = localMouse / new Vector2(PixelWidth, PixelHeight) * 2f - Vector2.One;
            normalized = Vector2.Clamp(normalized, -Vector2.One, Vector2.One);
            target = normalized * CursorParallaxPixels * UIScale;
        }

        var smoothing = 1f - MathF.Exp(-MotionSmoothing * args.DeltaSeconds);
        _parallaxOffset = Vector2.Lerp(_parallaxOffset, target, smoothing);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (PixelWidth <= 0 || PixelHeight <= 0)
            return;

        var reducedMotion = _configuration.GetCVar(CCVars.ReducedMotion);
        var motionEnabled = !reducedMotion && _configuration.GetCVar(CCVars.ParallaxEnabled);
        var lowQuality = _configuration.GetCVar(CCVars.ParallaxLowQuality);
        var crtEnabled = StyleNano.CrtUiEnabled;
        var now = (float) _timing.RealTime.TotalSeconds;
        var bootSeconds = (float) (_timing.RealTime - _bootStartedAt).TotalSeconds;
        var bootProgress = reducedMotion || !crtEnabled ? 1f : Math.Clamp(bootSeconds / 2.2f, 0f, 1f);
        var accent = GetAccentColor();
        var pulse = motionEnabled && crtEnabled ? 0.88f + MathF.Sin(now * 1.35f) * 0.12f : 1f;

        handle.DrawRect(PixelSizeBox, StyleNano.CrtBackground);
        DrawBackdropGlow(handle, accent, bootProgress);
        DrawStars(handle, accent, now, bootProgress, motionEnabled, lowQuality);
        DrawPerspectiveGrid(handle, accent, bootProgress, lowQuality);
        DrawPlanetAndRadar(handle, accent, now, bootProgress, pulse, motionEnabled, lowQuality);
        DrawIntegratedConsoles(handle, accent, bootProgress, pulse, crtEnabled);
        if (crtEnabled)
        {
            DrawViewfinder(handle, accent, bootProgress);
            DrawScanlines(handle, accent, now, motionEnabled, lowQuality);
            DrawVignette(handle);
        }
    }

    private void DrawBackdropGlow(DrawingHandleScreen handle, Color accent, float bootProgress)
    {
        var contentWidth = GetContentPixelWidth();
        var center = new Vector2(contentWidth * 0.68f, PixelHeight * 0.42f) + _parallaxOffset * 0.35f;
        var maxRadius = MathF.Min(PixelWidth, PixelHeight) * 0.42f;

        for (var i = 5; i >= 1; i--)
        {
            var radius = maxRadius * i / 5f;
            var alpha = 0.008f * (6 - i) * bootProgress;
            handle.DrawCircle(center, radius, accent.WithAlpha(alpha));
        }
    }

    private void DrawStars(
        DrawingHandleScreen handle,
        Color accent,
        float now,
        float bootProgress,
        bool motionEnabled,
        bool lowQuality)
    {
        var count = lowQuality ? LowQualityStarCount : HighQualityStarCount;
        var drift = motionEnabled ? now * 1.3f : 0f;

        for (var i = 0; i < count; i++)
        {
            var star = _stars[i];
            var x = star.Position.X * PixelWidth + _parallaxOffset.X * star.Depth + drift * star.Depth;
            var y = star.Position.Y * PixelHeight + _parallaxOffset.Y * star.Depth * 0.45f;

            x = PositiveModulo(x, PixelWidth);
            y = PositiveModulo(y, PixelHeight);

            var size = star.Size * UIScale;
            var rect = UIBox2.FromDimensions(new Vector2(x, y), new Vector2(size, size));
            handle.DrawRect(rect, accent.WithAlpha(star.Brightness * bootProgress));
        }
    }

    private void DrawPerspectiveGrid(
        DrawingHandleScreen handle,
        Color accent,
        float bootProgress,
        bool lowQuality)
    {
        var horizon = PixelHeight * 0.68f + _parallaxOffset.Y * 0.2f;
        var contentWidth = GetContentPixelWidth();
        var vanishingPoint = new Vector2(contentWidth * 0.56f + _parallaxOffset.X * 0.25f, horizon);
        var color = accent.WithAlpha(0.1f * bootProgress);
        var radialLines = lowQuality ? 9 : 15;
        var depthLines = lowQuality ? 6 : 11;

        for (var i = 0; i < radialLines; i++)
        {
            var fraction = i / (float) (radialLines - 1);
            var bottomX = MathHelper.Lerp(-PixelWidth * 0.2f, PixelWidth * 1.2f, fraction);
            handle.DrawLine(vanishingPoint, new Vector2(bottomX, PixelHeight), color);
        }

        for (var i = 1; i <= depthLines; i++)
        {
            var fraction = i / (float) depthLines;
            var y = horizon + (PixelHeight - horizon) * fraction * fraction;
            handle.DrawLine(new Vector2(0f, y), new Vector2(PixelWidth, y), color);
        }

        handle.DrawLine(
            new Vector2(0f, horizon),
            new Vector2(PixelWidth, horizon),
            accent.WithAlpha(0.22f * bootProgress));
    }

    private void DrawPlanetAndRadar(
        DrawingHandleScreen handle,
        Color accent,
        float now,
        float bootProgress,
        float pulse,
        bool motionEnabled,
        bool lowQuality)
    {
        var contentWidth = GetContentPixelWidth();
        var center = new Vector2(contentWidth * 0.68f, PixelHeight * 0.4f) + _parallaxOffset * 0.65f;
        var radius = Math.Clamp(MathF.Min(PixelWidth, PixelHeight) * 0.18f, 88f * UIScale, 230f * UIScale);
        radius *= 0.65f + bootProgress * 0.35f;

        var faint = accent.WithAlpha(0.16f * bootProgress);
        var medium = accent.WithAlpha(0.34f * bootProgress);
        var bright = accent.WithAlpha(0.72f * bootProgress * pulse);

        handle.DrawCircle(center, radius, medium, filled: false);
        handle.DrawCircle(center, radius * 0.68f, faint, filled: false);
        handle.DrawLine(center - new Vector2(radius * 1.2f, 0f), center + new Vector2(radius * 1.2f, 0f), faint);
        handle.DrawLine(center - new Vector2(0f, radius * 1.2f), center + new Vector2(0f, radius * 1.2f), faint);

        var sphereSegments = lowQuality ? 18 : 30;
        for (var latitude = -2; latitude <= 2; latitude++)
        {
            var offset = latitude / 3f;
            var width = radius * MathF.Sqrt(1f - offset * offset);
            DrawEllipse(
                handle,
                center + new Vector2(0f, offset * radius),
                width,
                MathF.Max(2f, width * 0.11f),
                0f,
                faint,
                sphereSegments);
        }

        var rotationPhase = motionEnabled ? now * 0.16f : 0.7f;
        var meridians = lowQuality ? 2 : 4;
        for (var i = 0; i < meridians; i++)
        {
            var phase = rotationPhase + i * MathF.PI / meridians;
            var width = MathF.Max(radius * 0.08f, MathF.Abs(MathF.Cos(phase)) * radius);
            DrawEllipse(handle, center, width, radius, 0f, faint, sphereSegments);
        }

        var orbitRotation = -0.28f;
        var orbitRadiusX = radius * 1.55f;
        var orbitRadiusY = radius * 0.48f;
        DrawEllipse(handle, center, orbitRadiusX, orbitRadiusY, orbitRotation, medium, lowQuality ? 24 : 42);

        var orbitAngle = motionEnabled ? now * 0.2f : 1.2f;
        var marker = PointOnEllipse(center, orbitRadiusX, orbitRadiusY, orbitRotation, orbitAngle);
        DrawMarker(handle, marker, orbitAngle + orbitRotation, bright, 8f * UIScale);

        var sweepAngle = motionEnabled ? now * 0.42f : -0.8f;
        var sweepDirection = new Vector2(MathF.Cos(sweepAngle), MathF.Sin(sweepAngle));
        handle.DrawLine(center, center + sweepDirection * radius, accent.WithAlpha(0.48f * bootProgress));
        handle.DrawCircle(marker, 3f * UIScale, bright, filled: false);

        if (_mode is LobbyTerminalMode.Countdown or LobbyTerminalMode.Imminent)
        {
            var countdownPulse = motionEnabled ? 0.5f + MathF.Sin(now * 3f) * 0.5f : 0.75f;
            handle.DrawCircle(
                center,
                radius * (1.12f + countdownPulse * 0.08f),
                accent.WithAlpha(0.18f * bootProgress),
                filled: false);
        }
    }

    private void DrawViewfinder(DrawingHandleScreen handle, Color accent, float bootProgress)
    {
        var margin = 24f * UIScale;
        var length = 34f * UIScale;
        var color = accent.WithAlpha(0.55f * bootProgress);
        var width = PixelWidth;
        var height = PixelHeight;

        DrawCorner(handle, new Vector2(margin, margin), new Vector2(1f, 1f), length, color);
        DrawCorner(handle, new Vector2(width - margin, margin), new Vector2(-1f, 1f), length, color);
        DrawCorner(handle, new Vector2(margin, height - margin), new Vector2(1f, -1f), length, color);
        DrawCorner(handle, new Vector2(width - margin, height - margin), new Vector2(-1f, -1f), length, color);

        var tickY = height * 0.12f;
        handle.DrawDottedLine(
            new Vector2(margin + length * 1.5f, tickY),
            new Vector2(width - margin - length * 1.5f, tickY),
            accent.WithAlpha(0.12f * bootProgress),
            dashSize: 9f * UIScale,
            gapSize: 7f * UIScale);
    }

    private void DrawIntegratedConsoles(
        DrawingHandleScreen handle,
        Color accent,
        float bootProgress,
        float pulse,
        bool crtEnabled)
    {
        var leftWidth = Math.Clamp(
            MathF.Max(0f, ReservedLeftWidth) * UIScale,
            0f,
            PixelWidth * 0.45f);
        var rightWidth = Math.Clamp(
            MathF.Max(0f, ReservedRightWidth) * UIScale,
            0f,
            PixelWidth * 0.55f);

        if (leftWidth > 0f)
        {
            DrawConsoleShell(
                handle,
                new UIBox2(0f, 0f, leftWidth, PixelHeight),
                accent,
                bootProgress,
                pulse,
                false,
                crtEnabled);
        }

        if (rightWidth > 0f)
            DrawConsoleShell(
                handle,
                new UIBox2(PixelWidth - rightWidth, 0f, PixelWidth, PixelHeight),
                accent,
                bootProgress,
                pulse,
                true,
                crtEnabled);
    }

    private void DrawConsoleShell(
        DrawingHandleScreen handle,
        UIBox2 box,
        Color accent,
        float bootProgress,
        float pulse,
        bool rightSide,
        bool crtEnabled)
    {
        var seamX = rightSide ? box.Left : box.Right;
        var direction = rightSide ? -1f : 1f;
        var line = MathF.Max(1f, UIScale);
        var railWidth = 14f * UIScale;
        var connectorLength = MathF.Min(72f * UIScale, PixelWidth * 0.06f);
        var backgroundAlpha = crtEnabled ? 0.36f : 0.82f;

        handle.DrawRect(box, StyleNano.CrtInsetBackground.WithAlpha(backgroundAlpha));

        // Layered edge bands create a recessed seam while leaving the scene visible below it.
        for (var i = 4; i >= 1; i--)
        {
            var offset = i * railWidth / 4f;
            var band = rightSide
                ? new UIBox2(seamX, box.Top, seamX + offset, box.Bottom)
                : new UIBox2(seamX - offset, box.Top, seamX, box.Bottom);
            handle.DrawRect(band, accent.WithAlpha(0.012f * (5 - i) * bootProgress));
        }

        handle.DrawRect(
            new UIBox2(seamX - line, box.Top, seamX + line, box.Bottom),
            accent.WithAlpha(0.42f * bootProgress));

        var guideColor = accent.WithAlpha(0.17f * bootProgress);
        var connectorColor = accent.WithAlpha(0.34f * bootProgress * pulse);
        var guideCount = PixelHeight < 650f * UIScale ? 4 : 6;

        for (var i = 1; i < guideCount; i++)
        {
            var y = box.Top + box.Height * i / guideCount;
            var inset = (i % 2 == 0 ? 24f : 42f) * UIScale;
            var startX = rightSide ? box.Right - inset : box.Left + inset;
            var endX = seamX - direction * 8f * UIScale;
            handle.DrawLine(new Vector2(startX, y), new Vector2(endX, y), guideColor);

            var connectorEnd = new Vector2(seamX + direction * connectorLength, y + direction * 10f * UIScale);
            handle.DrawLine(new Vector2(seamX, y), connectorEnd, connectorColor);
            handle.DrawRect(
                UIBox2.FromDimensions(
                    connectorEnd - new Vector2(2f * UIScale),
                    new Vector2(4f * UIScale)),
                connectorColor);
        }

        // Small hardware notches make the shell feel fixed to the viewport.
        var notchLength = 28f * UIScale;
        var notchColor = accent.WithAlpha(0.55f * bootProgress);
        var topNotchY = 32f * UIScale;
        var bottomNotchY = PixelHeight - 33f * UIScale;
        handle.DrawLine(
            new Vector2(seamX, topNotchY),
            new Vector2(seamX + direction * notchLength, topNotchY),
            notchColor);
        handle.DrawLine(
            new Vector2(seamX, bottomNotchY),
            new Vector2(seamX + direction * notchLength, bottomNotchY),
            notchColor);
    }

    private void DrawScanlines(
        DrawingHandleScreen handle,
        Color accent,
        float now,
        bool motionEnabled,
        bool lowQuality)
    {
        var lineCount = lowQuality ? 22 : 44;
        var step = PixelHeight / (float) lineCount;
        var lineHeight = MathF.Max(1f, UIScale);

        for (var i = 0; i < lineCount; i++)
        {
            var y = i * step;
            handle.DrawRect(
                UIBox2.FromDimensions(new Vector2(0f, y), new Vector2(PixelWidth, lineHeight)),
                accent.WithAlpha(0.012f));
        }

        var sweepY = motionEnabled
            ? PositiveModulo(now * 38f, PixelHeight)
            : PixelHeight * 0.46f;
        handle.DrawRect(
            UIBox2.FromDimensions(new Vector2(0f, sweepY), new Vector2(PixelWidth, 2f * UIScale)),
            accent.WithAlpha(0.055f));
    }

    private void DrawVignette(DrawingHandleScreen handle)
    {
        var edge = MathF.Max(24f * UIScale, MathF.Min(PixelWidth, PixelHeight) * 0.045f);
        var color = StyleNano.CrtBackground.WithAlpha(0.62f);

        handle.DrawRect(new UIBox2(0f, 0f, PixelWidth, edge), color);
        handle.DrawRect(new UIBox2(0f, PixelHeight - edge, PixelWidth, PixelHeight), color);
        handle.DrawRect(new UIBox2(0f, 0f, edge, PixelHeight), color);
        handle.DrawRect(new UIBox2(PixelWidth - edge, 0f, PixelWidth, PixelHeight), color);
    }

    private Color GetAccentColor()
    {
        return _mode switch
        {
            LobbyTerminalMode.Ready => StyleNano.CrtGreenSoft,
            LobbyTerminalMode.Countdown => CountdownColor,
            LobbyTerminalMode.Imminent => ImminentColor,
            LobbyTerminalMode.Paused => StyleNano.CrtGreenDisabled,
            LobbyTerminalMode.InProgress => InProgressColor,
            _ => StyleNano.CrtGreen,
        };
    }

    private void GenerateStars()
    {
        var seed = 0xC0FFEEu;
        for (var i = 0; i < _stars.Length; i++)
        {
            var x = NextFloat(ref seed);
            var y = NextFloat(ref seed);
            var depth = 0.25f + NextFloat(ref seed) * 0.75f;
            var brightness = 0.08f + NextFloat(ref seed) * 0.32f;
            var size = NextFloat(ref seed) > 0.88f ? 2f : 1f;
            _stars[i] = new Star(new Vector2(x, y), depth, brightness, size);
        }
    }

    private static float NextFloat(ref uint seed)
    {
        seed = seed * 1_664_525u + 1_013_904_223u;
        return (seed & 0x00FF_FFFFu) / 16_777_216f;
    }

    private static float PositiveModulo(float value, float divisor)
    {
        var result = value % divisor;
        return result < 0f ? result + divisor : result;
    }

    private float GetContentPixelWidth()
    {
        var reservedPixels = MathF.Max(0f, ReservedRightWidth) * UIScale;
        return Math.Clamp(PixelWidth - reservedPixels, PixelWidth * 0.35f, PixelWidth);
    }

    private void DrawEllipse(
    DrawingHandleScreen handle,
    Vector2 center,
    float radiusX,
    float radiusY,
    float rotation,
    Color color,
    int segments)
{
    segments = Math.Clamp(segments, 8, MaxEllipseSegments);

    var points = _ellipsePoints.AsSpan(0, segments + 1);
    var cosRotation = MathF.Cos(rotation);
    var sinRotation = MathF.Sin(rotation);

    for (var i = 0; i <= segments; i++)
    {
        var angle = i / (float) segments * MathHelper.TwoPi;
        var x = MathF.Cos(angle) * radiusX;
        var y = MathF.Sin(angle) * radiusY;

        points[i] = center + new Vector2(
            x * cosRotation - y * sinRotation,
            x * sinRotation + y * cosRotation);
    }

    handle.DrawPrimitives(
        DrawPrimitiveTopology.LineStrip,
        points,
        color);
}

    private static Vector2 PointOnEllipse(
        Vector2 center,
        float radiusX,
        float radiusY,
        float rotation,
        float angle)
    {
        var x = MathF.Cos(angle) * radiusX;
        var y = MathF.Sin(angle) * radiusY;
        var cosRotation = MathF.Cos(rotation);
        var sinRotation = MathF.Sin(rotation);
        return center + new Vector2(
            x * cosRotation - y * sinRotation,
            x * sinRotation + y * cosRotation);
    }

    private static void DrawMarker(
        DrawingHandleScreen handle,
        Vector2 center,
        float angle,
        Color color,
        float size)
    {
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var normal = new Vector2(-direction.Y, direction.X);
        var nose = center + direction * size;
        var left = center - direction * size * 0.65f + normal * size * 0.65f;
        var right = center - direction * size * 0.65f - normal * size * 0.65f;

        handle.DrawLine(nose, left, color);
        handle.DrawLine(left, right, color);
        handle.DrawLine(right, nose, color);
    }

    private static void DrawCorner(
        DrawingHandleScreen handle,
        Vector2 origin,
        Vector2 direction,
        float length,
        Color color)
    {
        handle.DrawLine(origin, origin + new Vector2(direction.X * length, 0f), color);
        handle.DrawLine(origin, origin + new Vector2(0f, direction.Y * length), color);
    }

    private readonly record struct Star(Vector2 Position, float Depth, float Brightness, float Size);
}

public enum LobbyTerminalMode : byte
{
    Waiting,
    Ready,
    Countdown,
    Imminent,
    Paused,
    InProgress,
}
