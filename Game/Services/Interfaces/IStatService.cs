namespace Navislamia.Game.Services;

public record CharacterStats(
    int StatId,
    short Strength, short Vitality, short Dexterity, short Agility,
    short Intelligence, short Mentality, short Luck,
    int MaxHp, int MaxMp);

public interface IStatService
{
    CharacterStats Compute(int race, int level);
}
