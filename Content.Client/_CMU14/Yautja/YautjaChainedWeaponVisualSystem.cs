using Content.Shared._CMU14.Yautja;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._CMU14.Yautja;

public sealed partial class YautjaChainedWeaponVisualSystem : EntitySystem
{
    private const string ShaderKey = "YautjaBloodCharge";
    private static readonly ProtoId<ShaderPrototype> Outline = "RMCAuraOutline";
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<YautjaChainedWeaponComponent, SpriteComponent>();
        while (query.MoveNext(out _, out var weapon, out var sprite))
        {
            if (!weapon.Charged)
            {
                _sprite.RemovePostShader(sprite, ShaderKey);
                continue;
            }
            if (!_sprite.TryGetPostShader(sprite, ShaderKey, out var shader))
            {
                _sprite.SetPostShader(sprite, new SpriteComponent.PostShaderArgs(ShaderKey, _prototypes.Index(Outline).InstanceUnique()));
                _sprite.TryGetPostShader(sprite, ShaderKey, out shader);
            }
            shader.Shader.SetParameter("outline_color", weapon.ChargeColor.WithAlpha(70f / 255));
            shader.Shader.SetParameter("outline_width", 2f);
        }
    }
}
