# Changement de métier — Epic 7.3

Implémenté le 2 octobre 2026. Aucun opcode nouveau : le changement de métier passe par les dialogues PNJ
(`TS_SC_DIALOG` 3000 / `TS_CS_DIALOG` 3001) et se publie par des propriétés (`TS_SC_PROPERTY` 507), des
statistiques (`TS_SC_STAT_INFO` 1000) et une ligne de chat (`TS_SC_CHAT`).

## 1. Sources

| source | ce qu'elle établit |
|---|---|
| `A:\Rappelz Kiff\Epic 7 Part 4\trunk\NPC_JobChange.lua` (cp949) | `NPC_JobChange_change_job` (l. 159-693), `NPC_master_partdevil_contact` (696-1034), `goto_hactor` (1037), `Run_JobChange_check_common` (1199), `Run_JobChange_common` (1319) |
| `…\Epic 7 Part 4\trunk\NPC_Tutorial.lua` | `NPC_Tutorial_Change_job` (1812), `Quest_Link_6_3` (1859), `Run_JobChange_Tutorial_check` (1931), `Run_JobChange_Tutorial` (1967) |
| `A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer` (source officielle, 2015) | `StructPlayer::onChangeProperty` (`StructPlayer.cpp:716`), `onAfterResetJob` (`:5199`), `SendJobInfo` (`SendMessage.cpp:3063`), `StructPlayer::IsLearnableSkill` (`:10280`), `SCRIPT_Conv` (`ScriptMisc.cpp:400`), `SCRIPT_Message` (`ScriptPlayer.cpp:1216`), `SCRIPT_Announce` (`ScriptCommon`, `CaptainHerlockServer.cpp:715`), `GameRule.h:33-36`, `is_able_to_jobchange` (`Resource/Script/ETC_Script_Functions.lua:93`) |
| `data/sqlserver/Arcadia/LevelResource.csv` (9.4) | coûts de JLv `jp_0..jp_3` ; le dump Epic 7 n'a pas de `LevelResource` |

## 2. Ce que le joueur fait

1. Il parle à un PNJ de métier (1004 Deva, 2004 Asura, 4004 Gaia, 6004 Rondo, 7004 Secroute, 7032 ville en ruine) et choisit
   « changer de métier » (`NPC_JobChange_change_job()`, déjà dans le catalogue des dialogues).
2. La page dépend de son état : profondeur 0 → `@90100404` et les trois métiers de sa race ; profondeur 1 → `@90700410` et les
   métiers de son métier courant ; conditions non remplies → texte de refus du PNJ (`@90<pnj>03` ou `@90<pnj>02`) ;
   profondeur 2 → `@91002563` (« va voir Hector ») ; profondeur 3 → `@90604793` (texte de réinitialisation, non portée).
3. Un métier ouvre `Run_JobChange_check_common( '@10xxx' , xxx )` : description du métier et « confirmer » (`@90010007`).
4. La confirmation `Run_JobChange_common(...)` montre `sconv("@90<pnj>06", "#@job_name@#", "@10xxx")` puis change le métier.

La classe maître se prend auprès du PNJ 11555 (`NPC_master_partdevil_contact`, absent de la liste de contacts du catalogue,
reconnu par son id), au niveau 147 et JLv 49, quête 3322 terminée. Le tutoriel (PNJ 3019) change le métier de base quand la
quête 1027 (6-3) est terminée, par `Quest_Link_6_3()` → `Run_JobChange_Tutorial_check` → `Run_JobChange_Tutorial`.

## 3. Règles

| profondeur | conditions (Lua Epic 7) | métiers proposés |
|---|---|---|
| 0 (base 100/200/300) | niveau ≥ 10, JLv ≥ 10 | Deva 201-203, Asura 301-303, Gaia 101-103 |
| 1 | niveau ≥ 50, JLv ≥ 40 | 201→210/211, 202→212/213, 203→214 ; 301→310/311, 302→312/313, 303→314 ; 101→110/111, 102→112/113, 103→114 |
| 2 | niveau ≥ 147, JLv ≥ 49, quête 3322 = 255 | métier + 10 (210→220 … 314→324), au PNJ 11555 seulement |

`is_able_to_jobchange()` (145 à la profondeur 2) ne sert qu'à choisir le texte de refus. Les menus sont **codés en dur dans
le Lua** (« la table ne se lit pas, on fixe ici ») : `JobChangeRules.NextJobs` les reprend tels quels, `JobResource.up_lv`
(148 en profondeur 2) n'est pas lu.

**Tout est rejugé à l'engagement** : le déclencheur prouve seulement que la page l'a proposé. Métier hors menu, conditions
perdues, mauvais PNJ (maître ailleurs qu'au 11555, tutoriel ailleurs qu'au 3019) → refus journalisé, rien n'est écrit. Un
seul changement à la fois par session (`ConnectionInfo.JobChangeInProgress`).

## 4. Effet (`Run_JobChange_common`, `onChangeProperty`)

- `job_N` / `jlv_N` (N = profondeur quittée) gardent le métier et le JLv laissés ; le nouveau métier part au JLv 1 ; la
  profondeur augmente d'un. Persistance : `ICharacterService.ChangeJobAsync` (`PreviousJobs`, `JobLvs`, `CurrentJob`, `Jlv`,
  `JobDepth` = bit 1/2/4/8, `TalentPoint`).
- Classe maître : **2 points de talent** (`DEFAULT_TALENT_POINT`, `MIN_TALENT_POINT_JOB_DEPTH` = 3), propriété `tp`.
- Trames, dans l'ordre de `set_value` : `job_N`, `jlv_N`, `job` (au joueur **et à ses observateurs**, `BroadcastPropertyMessage`),
  `job_level`, puis `SendJobInfo` (`job_0..2`, `jlv_0..2`), les deux `TS_SC_STAT_INFO` et `max_hp`/`max_mp` (les stats de base
  suivent le métier et `JobLevelBonus`), `job_level`, `job_depth`, `tp` (maître).
- Le groupe rediffuse la fiche du membre (`PartyService.OnJobChanged`, `PartyManager::OnChangeCharacterJob`), les quêtes se
  recalculent (objectif 501). Ligne de chat `message()` : type 40 (`CHAT_NPC`), émetteur `@SCRIPT`,
  `sconv("@90010017", "#@job_name@#", "@10xxx")` (tutoriel : `@90300408`). Classe maître : `announce()`, type 21, `@ANNOUNCE`,
  `@246` avec le nom du joueur, à tous.
- `sconv` joint ses arguments par `\v` (`SCRIPT_Conv`) : c'est le client qui substitue.

## 5. Ce qui en dépendait et a changé

- **Coût d'un JLv par profondeur** : il lisait toujours `jp_0`, donc un premier métier aurait plafonné au JLv 10. Le catalogue
  `DevConsole/job-level-costs.73.json` (`tools/export_job_level_costs.py`) porte `jp_0..jp_3` en 64 bits — `jp_3` déborde un
  `int` sur six niveaux, ce qui avait empêché leur import dans `LevelResources.JLvs` (`integer[]`). Plafonds : JLv 10, 50, 50, 60.
  `ILevelingService.TryGetNextJobLevelCost(depth, level, out cost)` ; `/joblevel` suit.
- **Apprentissage** : `SkillCatalog.EvaluateAcrossJobs` porte `StructPlayer::IsLearnableSkill` — arbres des métiers quittés
  d'abord (chacun avec son JLv), puis l'arbre courant ; la recherche continue tant que la réponse est « pas dans l'arbre »,
  « au-delà du maximum » ou « JLv insuffisant », et c'est le dernier arbre essayé qui répond.
- `IQuestService.GetQuestProgressAsync` : `get_quest_progress` (255 terminée, 2 terminable, 1 en cours, 0 non prise, -1 inconnue).

## 6. Non porté

- Changement de race (`NPC_JobChange_change_race`, objet 601100284, `set_race`) : remise au métier 0, réinitialisation des
  compétences.
- Réinitialisation des compétences de classe maître par or ou JP (`gold_skill_reset_check`, `jp_skill_reset_check`).
- Les compétences de talent (TP) elles-mêmes : seuls les points sont accordés.
- `NPC_JobChange_Beginner_contact` : marqué inutilisé dans le Lua.

## 7. NON ÉTABLI

- Le client 7.3 n'a pas été observé : rendu du texte `sconv` dans la fenêtre de dialogue, rafraîchissement de la fenêtre de
  compétences sur `job`/`job_depth`, nécessité de `job_depth` (le serveur ne l'envoie pas à la connexion).
- Les coûts de JLv viennent de la 9.4 : la table du client 7.3 peut différer.
- `get_quest_progress == 0` (« acceptable ») est rendu pour toute quête non prise, sans juger si elle peut être acceptée.
