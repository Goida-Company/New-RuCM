// Content.Client/_RMC14/Xenonids/Charge/CursorCharge/XenoCursorSteeringOverlay.cs

using System.Numerics;
using Content.Shared._RMC14.Xenonids.Charge.CursorCharge;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._RMC14.Xenonids.Charge.CursorCharge;

public sealed class XenoCursorSteeringOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private static readonly Color CurrentHeadingColor = Color.Green;
    private static readonly Color TargetHeadingColor = Color.Yellow;
    private const float VectorLength = 2f;

    private readonly IPlayerManager _player;
    private readonly SharedTransformSystem _transform;
    private readonly EntityQuery<XenoChargerStateComponent> _steeringQuery;

    public XenoCursorSteeringOverlay(IEntityManager ents)
    {
        _player = IoCManager.Resolve<IPlayerManager>();
        _transform = ents.System<SharedTransformSystem>();
        _steeringQuery = ents.GetEntityQuery<XenoChargerStateComponent>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player)
            return;

        if (!_steeringQuery.TryComp(player, out var steering))
            return;

        if (steering.MoveState != XenoChargerMoveState.Charging)
            return;

        var origin = _transform.GetMapCoordinates(player);
        if (origin.MapId != args.MapId)
            return;

        var currentVec = steering.CurrentHeading.ToVec() * VectorLength;
        var targetVec = steering.TargetHeading.ToVec() * VectorLength;

        DrawThickLine(args.WorldHandle, origin.Position, origin.Position + (System.Numerics.Vector2)currentVec, CurrentHeadingColor, 0.1f);
        DrawThickLine(args.WorldHandle, origin.Position, origin.Position + (System.Numerics.Vector2)targetVec, TargetHeadingColor, 0.1f);
    }

    private static void DrawThickLine(DrawingHandleWorld handle, Vector2 from, Vector2 to, Color color, float thickness)
    {
        var delta = to - from;
        if (delta.LengthSquared() <= 0f)
            return;

        var half = thickness * 0.5f;
        var length = delta.Length();
        var mid = (from + to) * 0.5f;

        // Calculate angle directly from delta components instead of ToWorldAngle()
        var angle = new Angle(Math.Atan2(delta.Y, delta.X));

        var rect = new Box2(-length / 2f, -half, length / 2f, half);
        var rotated = new Box2Rotated(rect.Translated(mid), angle, mid);
        handle.DrawRect(rotated, color);
    }

}


