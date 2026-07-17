using Navislamia.Game.DataAccess.Repositories.Interfaces;

namespace Navislamia.Game.Services.Buffs;

public interface IBuffCatalog
{
    int Count { get; }

    int CountOf(SkillCastKind kind);

    bool TryGet(int skillId, out CastableBuffFields fields);
}
