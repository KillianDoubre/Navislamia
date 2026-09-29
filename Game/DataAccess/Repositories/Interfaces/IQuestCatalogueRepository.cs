using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Repositories.Interfaces;

public interface IQuestCatalogueRepository
{
    /// <summary>
    /// Every quest definition of the <c>QuestResource</c> table, ordered by <c>id</c>: several quests are
    /// looked up by code, and a deterministic order is what makes an in-memory index reproducible.
    /// </summary>
    IReadOnlyList<QuestResourceEntity> GetResources();

    /// <summary>
    /// Every NPC ↔ quest link of the <c>QuestLinkResource</c> table, ordered by <c>npc_id</c> then
    /// <c>quest_id</c>: the trigger groups the links by NPC, so the order has to be deterministic instead of
    /// whatever the table returns.
    /// </summary>
    IReadOnlyList<QuestLinkResourceEntity> GetLinks();
}
