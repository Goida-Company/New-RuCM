using System.Linq;
using System.Numerics;
using Content.Shared._CMU14.Medical.Anatomy.BodyParts;
using Content.Shared._CMU14.Medical.Core;
using Content.Shared._CMU14.Medical.Injuries.Pain;
using Content.Shared._CMU14.Medical.Injuries.Wounds;
using Content.Shared._CMU14.Medical.Treatment.FirstAid;
using Content.Shared._CMU14.Yautja;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared.Body.Part;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._CMU14.Yautja;

public sealed partial class YautjaThrowSystem
{
    [Dependency] private SharedBodyPartHealthSystem _partHealth = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SkillsSystem _skills = default!;
    [Dependency] private SharedPainShockSystem _pain = default!;
    [Dependency] private SharedCMUWoundsSystem _wounds = default!;
    private static readonly EntProtoId<SkillDefinitionComponent> Surgery = "RMCSkillSurgery";
    private readonly Dictionary<EntityUid, (EntityUid Body, EntityUid Part, Vector2 Position)> _embeddedWeapons = new();

    private void OnEmbeddedActivate(Entity<YautjaThrowComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !TryComp<EmbeddableProjectileComponent>(ent, out var embedded) || embedded.EmbeddedIntoUid == null)
            return;
        args.Handled = true;
        TryBeginExtraction(ent, args.User);
    }

    private void OnExtractionVerbs(Entity<YautjaEmbeddedWeaponHolderComponent> body, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        foreach (var (weapon, entry) in _embeddedWeapons)
        {
            if (entry.Body != body.Owner || TerminatingOrDeleted(weapon))
                continue;
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString("cmu-yautja-extract-weapon", ("item", weapon)),
                Act = () => TryBeginExtraction(weapon, user),
            });
        }
    }

    public bool TryBeginExtraction(EntityUid weapon, EntityUid user)
    {
        if (!TryComp<YautjaThrowComponent>(weapon, out var profile) ||
            !TryComp<EmbeddableProjectileComponent>(weapon, out var embedded) ||
            !TryComp<BodyPartComponent>(embedded.EmbeddedIntoUid, out var part) || part.Body is not { } body)
            return false;
        if (!_hands.TryGetHand(user, _hands.GetActiveHand(user), out _) || !_hands.ActiveHandIsEmpty(user))
        {
            _popup.PopupEntity(Loc.GetString("cmu-yautja-extract-empty-hand"), user, user);
            return false;
        }
        var multiplier = _skills.GetSkillDelayMultiplier(user, Surgery, [1f, 1.2f, 1f, 0.6f]);
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user,
            TimeSpan.FromSeconds(2 * profile.Weight * multiplier), new YautjaExtractWeaponDoAfterEvent(), weapon, body)
        {
            NeedHand = true,
            BreakOnHandChange = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnExtractionFinished(Entity<YautjaThrowComponent> ent, ref YautjaExtractWeaponDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !TryComp<EmbeddableProjectileComponent>(ent, out var embedded) ||
            embedded.EmbeddedIntoUid == null || !_hands.TryGetHand(args.User, _hands.GetActiveHand(args.User), out _) || !_hands.ActiveHandIsEmpty(args.User))
            return;
        args.Handled = true;
        _projectile.EmbedDetach(ent, embedded, args.User);
        _hands.TryPickupAnyHand(args.User, ent);
    }

    private void OnEmbeddedRemoved(Entity<YautjaThrowComponent> ent, ref EmbedRemovedEvent args)
    {
        _embeddedWeapons.Remove(ent);
        if (args.User is not { } user || !TryComp<BodyPartComponent>(args.Target, out var part) || part.Body is not { } body)
            return;
        // mob/yank_out_object: pulling a weapon out inflicts weight * 3 on that limb.
        _partHealth.TryApplyPartDamage(body, args.Target, WoundDamage(ent.Comp.Weight * 3),
            origin: user, ignoreResistance: true);
        _pain.AddPainPulse(body, FixedPoint2.New(ent.Comp.Weight * 3));
        if (_random.Prob(ent.Comp.Weight * 0.05f))
            _wounds.SeedSurgicalInternalBleed(args.Target);
    }

    public override void Update(float frameTime)
    {
        foreach (var (weapon, entry) in _embeddedWeapons.ToArray())
        {
            if (TerminatingOrDeleted(weapon) || TerminatingOrDeleted(entry.Body) || TerminatingOrDeleted(entry.Part) ||
                !TryComp<EmbeddableProjectileComponent>(weapon, out var embedded) || embedded.EmbeddedIntoUid != entry.Part)
            {
                _embeddedWeapons.Remove(weapon);
                continue;
            }
            var position = _transform.GetWorldPosition(entry.Body);
            var delta = position - entry.Position;
            var distance = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
            if (distance < 1)
                continue;
            _embeddedWeapons[weapon] = (entry.Body, entry.Part, position);
            if (distance > 1.5f || _mobState.IsDead(entry.Body) ||
                (TryComp<StandingStateComponent>(entry.Body, out var standing) && !standing.Standing) ||
                (TryComp<BuckleComponent>(entry.Body, out var buckle) && buckle.Buckled) ||
                !TryComp<YautjaThrowComponent>(weapon, out var profile))
                continue;
            var sharp = profile.Sharp && profile.Edge;
            if (sharp && HasComp<CMUSplintedComponent>(entry.Part))
                continue;
            // human/handle_embedded_objects: one 20% roll per movement step, 1–2 damage.
            if (sharp && _random.Prob(0.2f))
                _partHealth.TryApplyPartDamage(entry.Body, entry.Part, WoundDamage(_random.Next(1, 3)), ignoreResistance: true);
            if (_random.Prob(0.3f))
                _popup.PopupEntity(Loc.GetString("cmu-yautja-embedded-movement", ("item", weapon)), entry.Body, entry.Body);
        }
    }

    private static DamageSpecifier WoundDamage(int amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict["Slash"] = FixedPoint2.New(amount);
        return damage;
    }
}
