using System;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

public static class CharacterDefaults
{
    public static Job GetStarterJob(int race)
    {
        return (Race)race switch
        {
            Race.Gaia => Job.Rogue,
            Race.Deva => Job.Guide,
            Race.Asura => Job.Stepper,
            _ => throw new ArgumentOutOfRangeException(nameof(race), race, null)
        };
    }

    public static bool Apply(CharacterEntity character)
    {
        var changed = false;

        if (character.Lv <= 0)
        {
            character.Lv = 1;
            changed = true;
        }

        if (character.MaxReachedLv < character.Lv)
        {
            character.MaxReachedLv = character.Lv;
            changed = true;
        }

        if ((int)character.CurrentJob == 0)
        {
            character.CurrentJob = GetStarterJob(character.Race);
            changed = true;
        }

        if (character.Jlv <= 0)
        {
            character.Jlv = 1;
            changed = true;
        }

        if (character.JobDepth == 0)
        {
            character.JobDepth = JobDepth.Base;
            changed = true;
        }

        if (character.PreviousJobs is not { Length: 3 })
        {
            character.PreviousJobs = new Job[3];
            changed = true;
        }

        if (character.JobLvs is not { Length: 3 })
        {
            character.JobLvs = new int[3];
            changed = true;
        }

        return changed;
    }
}
