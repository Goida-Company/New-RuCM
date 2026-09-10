using System.Numerics;
using Content.Shared._CMU14.Medical.Anatomy.BodyParts;
using Content.Shared._CMU14.Medical.Anatomy.BodyParts.Events;
using Content.Shared._CMU14.Medical.Core;
using Content.Shared._CMU14.Yautja;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._CMU14.Yautja;

/// <summary>Weapon-specific launch impacts, without changing ordinary item throwing.</summary>
public sealed partial class YautjaThrowSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private ThrownItemSystem _thrown = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedProjectileSystem _projectile = default!;
    [Dependency] private SharedBodyZoneTargetingSystem _targeting = default!;
    [Dependency] private CMUMedicalBodyIndexSystem _medical = default!;
    [Dependency] private IRobustRandom _random = default!;

    private EntityUid? _impactTarget;
    private EntityUid? _impactPart;

    public override void Initialize()
    {
        SubscribeLocalEvent<CMUHumanMedicalComponent, HitLocationResolveEvent>(OnResolveHit);
        SubscribeLocalEvent<YautjaThrowComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<YautjaThrowComponent, EmbedRemovedEvent>(OnEmbeddedRemoved);
        SubscribeLocalEvent<YautjaThrowComponent, Content.Shared.Interaction.ActivateInWorldEvent>(OnEmbeddedActivate,
            before: [typeof(SharedProjectileSystem)]);
        SubscribeLocalEvent<YautjaThrowComponent, YautjaExtractWeaponDoAfterEvent>(OnExtractionFinished);
        SubscribeLocalEvent<YautjaEmbeddedWeaponHolderComponent, Content.Shared.Verbs.GetVerbsEvent<Content.Shared.Verbs.InteractionVerb>>(OnExtractionVerbs);
    }

    private void OnLand(Entity<YautjaThrowComponent> ent, ref LandEvent args)
    {
        if (TryComp<YautjaSmartDiscComponent>(ent, out var disc) && disc.Active)
            return;
        if (TryComp<PhysicsComponent>(ent, out var physics))
        {
            _physics.SetLinearVelocity(ent, Vector2.Zero, body: physics);
            _physics.SetAngularVelocity(ent, 0, body: physics);
        }
    }

    private void OnResolveHit(Entity<CMUHumanMedicalComponent> ent, ref HitLocationResolveEvent args)
    {
        if (_impactTarget != ent.Owner)
            return;

        // CMSS13 hitby uses check_zone(thrower.zone_selected), not melee aim accuracy.
        var zone = args.Attacker is { } attacker
            ? _targeting.TryGetSelection(attacker) ?? TargetBodyZone.Chest
            : TargetBodyZone.Chest;
        var (type, symmetry) = SharedBodyZoneTargetingSystem.ToBodyPart(zone);
        if (!_medical.TryGetBodyPart(ent, new(type, symmetry), out var part))
            return;

        args.ResolvedPart = type;
        args.ResolvedPartEntity = part;
        args.ResolvedZone = zone;
        args.Handled = true;
        _impactPart = part;
    }

    public DamageSpecifier? ApplyHit(Entity<YautjaThrowComponent> weapon, EntityUid target,
        DamageOtherOnHitComponent hit, ThrownItemComponent thrown, DamageImpact impact)
    {
        var speed = TryComp<PhysicsComponent>(weapon, out var physics) ? physics.LinearVelocity.Length() : 0;
        var raw = weapon.Comp.ImpactDamage(speed);
        // living/hitby uses xeno_melee (1.5 initial damage), while humans use marine_melee.
        if (HasComp<Content.Shared._RMC14.Xenonids.XenoComponent>(target))
            raw *= 1.5f;
        var baseDamage = new DamageSpecifier(hit.Damage);
        var total = baseDamage.GetTotal().Float();
        var damage = total > 0 ? baseDamage * (raw / total) : new DamageSpecifier();
        var previousTarget = _impactTarget;
        var previousPart = _impactPart;
        _impactTarget = target;
        _impactPart = null;
        try
        {
            var dealt = _damage.TryChangeDamage(target, damage * _damage.UniversalThrownDamageModifier,
                hit.IgnoreResistances, origin: thrown.Thrower, tool: weapon, impact: impact);

            // A hit ends an ordinary throw. Active smart-discs use their own flight system.
            if (HasComp<HumanoidAppearanceComponent>(target) || HasComp<Content.Shared.Mobs.Components.MobStateComponent>(target))
            {
                if (TryComp<ThrownItemComponent>(weapon, out var current))
                    _thrown.StopThrow(weapon, current);
                if (physics != null)
                {
                    _physics.SetLinearVelocity(weapon, Vector2.Zero, body: physics);
                    _physics.SetAngularVelocity(weapon, 0, body: physics);
                }
            }

            if (weapon.Comp.Embeddable && _impactPart is { } part &&
                !HasComp<YautjaComponent>(target) && dealt != null &&
                _random.Prob(weapon.Comp.EmbedChance(dealt.GetTotal().Float())))
            {
                var embedded = EnsureComp<EmbeddableProjectileComponent>(weapon);
                embedded.EmbedOnThrow = false; // The chance is evaluated here after armor, once.
                embedded.RemovalTime = 2 * weapon.Comp.Weight;
                _projectile.EmbedAttach(weapon, part, thrown.Thrower, embedded);
                // Anatomy containers hide their children; display the weapon on the body.
                _transform.SetParent(weapon, target);
                _embeddedWeapons[weapon] = (target, part, _transform.GetWorldPosition(target));
                EnsureComp<YautjaEmbeddedWeaponHolderComponent>(target);
            }
            return dealt;
        }
        finally
        {
            _impactTarget = previousTarget;
            _impactPart = previousPart;
        }
    }
}
