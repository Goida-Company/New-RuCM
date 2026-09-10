namespace Content.Shared.Projectiles;

[ByRefEvent]
public readonly record struct EmbedRemovedEvent(EntityUid? User, EntityUid Target);
