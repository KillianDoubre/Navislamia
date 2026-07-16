using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Services.Stats;

namespace Navislamia.Game.Services;

public interface IStatService
{
    CharacterStatResult Compute(CharacterEntity character);

    CharacterStatResult Compute(ConnectionInfo info);

    CharacterStatResult ComputeForNewCharacter(int race);

    void Seed(ConnectionInfo info, CharacterEntity character);
}
