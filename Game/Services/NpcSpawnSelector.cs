using System;
using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.Services;

public static class NpcSpawnSelector
{
    public static IEnumerable<NpcResourceEntity> WithinRange(IEnumerable<NpcResourceEntity> npcs,
        float x, float y, float range)
    {
        foreach (var npc in npcs)
        {
            if (Math.Abs(npc.X - x) <= range && Math.Abs(npc.Y - y) <= range)
            {
                yield return npc;
            }
        }
    }
}
