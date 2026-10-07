# GM and admin commands

Every command is typed in the in-game chat. A line whose first character is `/`, on any channel except a whisper,
is a command: the server runs it and never relays it as a chat message. Answers come back on the system chat line,
sender `@SYSTEM`.

Code: `Game/Services/GmCommands/` (`GmCommandCatalog` lists every command).

## Permissions

There is a single privileged tier: **GM/admin commands need `Characters.Permission >= 100`**. The official server
split them into grades (D, C, B); those grades are not modelled, so a permission of 100 or more unlocks every GM
command. The value is read when the character enters the world, so a change takes effect at the next login:

```sql
UPDATE "Characters" SET "Permission" = 100 WHERE "CharacterName" = 'MyGameMaster';
```

Without the permission, a GM command answers exactly like an unknown one (`Unknown command: /warp. Type /help.`),
so its existence is not revealed. The attempt is logged as a warning, and every GM command that runs is logged with
its author and arguments.

`/help` lists the commands the caller is allowed to use.

## GM and admin commands (permission 100)

### Moderation

| Command | Description |
|---|---|
| `/notice <text>` | Broadcasts an announcement to every player in the world (notice line). |
| `/kick <name>` | Disconnects a player through the normal disconnection path (their progress is saved). |
| `/block_chat <name> [minutes]` | Mutes a player's chat for 0 to 144,000 minutes; `0` lifts the block. Without a duration, shows the time left. The block is saved and survives a reconnection; a muted player's chat is refused. |
| `/check_auto_user <name>` | Shows whether the player carries the anti-bot flag. |
| `/invisible [1\|2]` | `1` makes you invisible (status flag, monsters stop chasing you), `2` makes you visible again. Without an argument, shows your current state. |

### Movement

| Command | Description |
|---|---|
| `/warp <x> <y>` | Teleports you to the given world coordinates (decimals accepted, `94454.5`). |
| `/force_warp <name>` | Teleports you next to a player, on their layer. |
| `/force_warp <x> <y> [name]` | Teleports you, or the named player, to the given coordinates. |
| `/home` | Teleports you to your return point (the place a town respawn would send you). |
| `/speed [value]` | Sets your movement speed for the session (120 is the base, 1,785 at most). Without a value, restores the normal speed. |

### Character

| Command | Description |
|---|---|
| `/level <level>` | Raises your level to the given level through the normal level-up (packets, stats, HP refill). Upward only. |
| `/lv <level>` | Sets your level to any level, up or down, by setting your experience to that level's threshold. |
| `/exp <amount>` | Adds experience (positive only); every level it reaches is gained normally. |
| `/jp <amount>` | Adds or removes job points (never below 0). |
| `/joblevel <level>` | Raises your job level to the given level without spending JP; stops where the job tier is capped. |
| `/learn <skill> [level]` | Learns a skill without spending JP, at its maximum level by default. Ignores the job restriction; a skill outside your job tree may not show in the skill window. |
| `/job <job id>` | Changes your job to any job of your race's tree, at any depth (`/job 220` makes a Deva a master class), without the NPC, the level or the quest. The jobs on the way are recorded as left behind at job level 10, 40 and 49 (or the level you already had there). The new job starts at job level 1; skills and JP are kept. Becoming a master class grants its 2 talent points, like the real job change. A job of another race is refused: change the race first. |
| `/race <deva\|asura\|gaia>` | Changes your race, like the official race change without its stone: you go back to the new race's base job, every skill is removed and its JP is given back, with the JP of the job levels. Reconnect to see the new body. The race id also works (Gaia 3, Deva 4, Asura 5). |
| `/maxskills` | Raises every skill of your job trees (the current job and the jobs left behind) to the highest level those trees allow, in one go, without spending JP and whatever the level, job level or prerequisites. Run it again after `/job` to fill the new tree. |
| `/buff <state> [level] [seconds]` | Applies a state (buff or debuff) to you: level 1 and 300 seconds by default, one day at most. The state id must exist. |
| `/gold <amount>` | Adds or removes gold (`/gold -500`), never below 0. |
| `/chaos <amount>` | Adds or removes chaos, between 0 and the maximum. |
| `/item <code> [count]` | Adds a stack of an item to your bag (1 by default, 10,000 at most). Unknown item codes are refused. |
| `/heal` | Restores your HP and MP to their maximum. Refused while dead. |
| `/die` | Sets your HP to 0, to test death and resurrection. |
| `/rebirth` | Adds your maximum HP to your HP and to your MP, on the spot (the official script's behaviour). |
| `/immortal [on\|off]` | Monsters still hit you but deal no damage. Without an argument, toggles. |
| `/pk [on\|off]` | Turns PK mode on or off immediately, without the countdown. Without an argument, toggles. |
| `/save` | Saves your progress without disconnecting. |

### World and combat

| Command | Description |
|---|---|
| `/doit` | Kills every monster visible to you, with the normal corpse, loot, reward and respawn. |
| `/target` | Shows the id, level and HP of the monster you are targeting. Changes nothing. |
| `/gametime [hours]` | Shifts the world clock for everyone by the given number of hours, to test day and night. `0` restores the real time; up to a year either way; without an argument, shows the current world time. Not saved. |

### Rates

| Command | Description |
|---|---|
| `/rate` | Shows the effective rates (base × event), the time left on each event and the other rate settings. |
| `/rate <type> <multiplier> <duration>` | Starts a rate event announced to every player. `type` is `exp`, `jp`, `gold`, `drop`, `card`, `chaos` or `all`. The multiplier goes from 0 to `MaxEventMultiplier` (100 by default; `1.5` and `x2` are accepted). The duration is required: seconds, or a suffix `s`, `m`/`min`, `h`, `d`, up to 30 days (`/rate exp 2 1h`). A new event on a type replaces the one already running (x2 then x3 gives x3). |
| `/rate reset [type]` | Ends one event early, or all of them, and announces it. |

Events are saved with their end time and resume after a server restart. Base rates live in the `Rates` section of
`DevConsole/appsettings.{Dev,Prod}.json` and are reloaded live:

| Key | Effect |
|---|---|
| `Exp` | Experience per kill. |
| `Jp` | Job points per kill; when absent, follows `Exp`. |
| `Gold` | Chance that a monster drops gold (capped at 100 %). |
| `ChaosDrop` | Chance to gain chaos from a kill (capped at 100 %). |
| `ItemDrop` | Chance of every item drop slot (capped at 100 %). |
| `CreatureCardDrop` | Extra factor on a drop slot whose item is a creature card. |
| `MonsterRespawnSeconds` | Delay before a killed monster respawns (10). |
| `GroundItemLifetimeSeconds` | Lifetime of an item on the ground (120). |
| `SkillJpCost` | Factor on the JP cost of a skill level. |
| `JobLevelJpCost` | Factor on the JP cost of a job level. |
| `MaxEventMultiplier` | Highest multiplier `/rate` accepts (100). |
| `EventReminderMinutes` | Reminder announced before an event ends (5; 0 for none). |
| `EventStatePath` | File the running events are saved to. |

## Commands every player can use

These go through the same command handler but need no permission.

| Command | Description |
|---|---|
| `/help` | Lists the commands you can use. |
| `/position` | Shows your X, Y, Z coordinates and layer. |
| `/rates` | Shows the effective rates, read-only. |
| `/sitdown` | Sits down (stops your attack). Refused while dead. |
| `/standup` | Stands up. |
| `/battle <handle>` | Battle stance on for the character or one of its summons (`onBattleMode`); the client sends it itself (`/battle %u`) when it draws. |
| `/normal <handle>` | Battle stance off (`onNormalMode`); the client sends `/normal %u`. Both stay silent on a bad handle. |
| `/walk [on\|off]` | Walks instead of running; without an argument, toggles. |
| `/ride <summon handle>` | Mounts your summon (Creature Riding); if already mounted, dismounts. |
| `/unride` | Dismounts. |
| `/hold [summon handle] [on\|off]` | Stops your summon and stops it following you; `off` lets it follow again. Without a handle, applies to all your summons. |
| `/titles` | Lists your titles, your main title and your secondary titles. |
| `/title <id\|0>` | Sets your main title (`0` removes it); 5 minutes between two changes. |
| `/subtitle <slot 1-5> <id\|0>` | Sets a secondary title in a slot (up to your rank); it grants 10 % of the title's bonuses. |
| `/change_name <name>` | Renames your character, once: 4 to 18 letters or digits, no banned word, capitalised like the rules require. You leave every other player's friend and block list. |

Party (`/pcreate`, `/pinvite`…; `/passist <handle>` takes the target of the party member clicked in the party window), guild (`/g…`, `/ga…`, `/rp…`) and friend (`/add_friend`, `/add_denial`…)
commands are sent by the client's own windows and are documented with their systems
(`docs/packet-specs/socle-groupe.md`, `socle-guildes-alliances-sieges.md`, `socle-amis.md`).

## Not available, on purpose

- `/run <lua>`: would execute arbitrary Lua; the server never does.
- `/suicide`: in NGemity it shuts the whole server down. Use `/die` to test death.
- `/regenerate <id> [count]`: spawning monsters at runtime is not supported by the monster infrastructure.
- `&` commands found online (`&summonplayer`, `&immortal`…): they belong to another client; this 7.3 client has none.
