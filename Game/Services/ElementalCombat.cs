using Navislamia.Game.Network.Clients;

namespace Navislamia.Game.Services;

public static class ElementalCombat
{
    public static HitResult RollElementalHit(this ICombatService combat, GameClient client, long instanceId,
        float damage, DamageKind kind, int accuracy, int critical, int element) => element == 0
        ? combat.RollHit(client, instanceId, damage, kind, accuracy, critical)
        : combat.RollHit(client, instanceId, damage, kind, accuracy, critical, element);

    public static HitResult RollElementalMonsterHit(this ICombatService combat, long instanceId, GameClient target,
        float damage, DamageKind kind, int accuracy, int critical, int element) => element == 0
        ? combat.RollMonsterHit(instanceId, target, damage, kind, accuracy, critical)
        : combat.RollMonsterHit(instanceId, target, damage, kind, accuracy, critical, element);
}
