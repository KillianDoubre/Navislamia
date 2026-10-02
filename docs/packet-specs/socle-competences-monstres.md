# Socle — compétences des monstres

**Mise à jour du 2 octobre 2026 :** les zones, multi-coups, indices à probabilité nulle et conditions
Lua sont implémentés dans [le socle complémentaire](socle-competences-zone-multi-coups.md).
Les exclusions et les nombres de l'étude initiale ci-dessous décrivent l'ancien périmètre.

| | |
|---|---|
| Paquets | `TM_SC_SKILL` (401), `TM_SC_STATE` (505), propriété `hp` — **aucun identifiant nouveau** |
| Données | `MonsterSkillResource` (Epic 7, `data/epic7`) → `DevConsole/monster-skills.73.json` (`tools/export_monster_skills.py`), clé `MonsterResource.monster_skill_link_id` ; définitions dans `SkillResources` (Arcadia) |
| Référence d'autorité | serveur officiel `CaptainHerlockServer.exe` 2012-11 + PDB : `onMonsterSkillInfo` (`0x1401c63a0`), `StructMonster::AI_processAttack` (`0x140166350`) |
| Référence secondaire | NGemity `Skill.cpp` pour les formules (`SINGLE_PHYSICAL_DAMAGE_T1`, `SINGLE_MAGICAL_DAMAGE_T1`) ; NGemity ne fait lancer aucune compétence aux monstres |
| Code | `Game/Services/MonsterSkills/` (`MonsterSkillCatalog`, `MonsterSkillRules`, `MonsterSkillService`), `MonsterAiService.Attack` |
| Tests | `Tests/Game/MonsterSkillTests.cs` |

## 1. Réponses courtes

1. **Les monstres lancent des compétences.** Au moment où un monstre peut frapper, il tire chacune de ses
   compétences dans l'ordre ; la première qui sort **remplace le coup**.
2. **Couverture** : 524 des 725 entrées tirables (probabilité > 0), soit 3 853 monstres ayant au moins une
   compétence — dégâts à une cible physiques et magiques, états (malus sur le joueur, bonus sur le monstre),
   soin du monstre sur lui-même.
3. **Hors lot** : les compétences de zone (111, 112, 113, 241, 261, 262, 30013…, ~150 entrées) et les
   déclencheurs, qui appellent du Lua.

## 2. Chargement (serveur officiel)

`onMonsterSkillInfo` lit `SELECT * FROM dbo.MonsterSkillResource ORDER BY id, sub_id`. Pour chaque ligne d'un
même `id`, dans l'ordre des `sub_id`, il ajoute chaque `skillN_id` non nul comme `{id, niveau, probabilité}`
(12 octets) à la liste de cet `id`. Chaque `trigger_N` dont la fonction n'est ni vide ni `"0"` et dont le
type n'est pas 0 devient un `MONSTER_TRIGGER` (type, deux valeurs, nom de fonction Lua).

L'export écrit les entrées de probabilité **strictement positive**, dans cet ordre : l'IA ne tire jamais les
autres (342 entrées à 0, que seuls les déclencheurs atteignent).

## 3. Choix de la compétence (`AI_processAttack`)

Quand le monstre peut attaquer (`prochaine attaque <= maintenant`) :

1. chaque déclencheur dont la condition (`CheckTriggerCondition`) est remplie exécute
   `fonction(handle, cible, index, x, y, …)` en Lua — **non porté** ;
2. chaque entrée, dans l'ordre, tire `XRandom() × 10000 >> 32` (0..9999) ; si `probabilité × 10000` le
   dépasse, le monstre lance la compétence au niveau de l'entrée. Un lancer refusé (recharge, ici) passe à
   l'entrée suivante ; le premier lancer réussi met fin au tour **sans coup normal** ;
3. sinon, le coup normal.

La cible est le monstre lui-même quand un octet de la compétence (`SkillBase+0xc`) est nul, sa cible sinon.
Lu ici comme `is_harmful` : c'est la seule colonne cohérente avec les données (tous les soins et bonus sont à
0, toutes les attaques à 1).

## 4. Effets

| Famille | Effet | Ici |
|---|---|---|
| 101 `PHYSICAL_SINGLE_DAMAGE_T1` | `attaque + var0 + var1 × niv` | dégâts physiques à la cible |
| 30001 | `attaque × (var0 + var1 × niv) + var2 + var3 × niv` | idem |
| 201 `MAGIC_SINGLE_DAMAGE_T1_OLD` | `magie + var0 + var1 × niv` | dégâts magiques |
| 231 | `magie × (var0 + var1 × niv) + var3 + var4 × niv` | idem |
| 301 / 302 nuisible | état sur le joueur | `ISkillCastService.ApplyState` : icône, décompte, expiration, stats |
| 301 / 302 non nuisible | état sur le monstre | `MonsterWorldState.AddState` : il entre dans les stats du monstre |
| 501 | soin | `HealCurve` avec la magie et les PV max du monstre |

Les dégâts passent par `ICombatService.RollMonsterHit` : toucher, blocage, critique et défense sont ceux
d'un coup (`socle-combat-reel.md`), avec `hit_bonus + écart de niveau × percentage` et
`critical_bonus + critical_bonus_per_skl × niv`. `/immortal` annule les dégâts et les malus.

Recharge : `delay_cooltime + delay_cooltime_per_skl × niv` secondes, par monstre et par compétence, oubliée
à la mort. Le prochain tour attend la plus longue de l'incantation et de l'intervalle d'attaque.

## 5. Sur le fil

La séquence d'un lancer de joueur, avec le monstre (son handle **pour ce client**) en lanceur :
`ST_Casting` (délai d'incantation), `ST_Fire` (le coup : dégâts avec drapeaux, ou `SHT_ADD_HP` pour un soin),
`ST_Complete`. Un état part en `TM_SC_STATE` sur le handle de qui le porte ; les PV du joueur en propriété
`hp`.

## 6. Écarts assumés

- **Le tir part tout de suite** après `ST_Casting`, comme pour les joueurs ; le délai d'incantation n'est
  qu'annoncé.
- **Un état de zone nuisible (302) ne touche que la cible.**
- **Pas de coût en PM** : les PM du monstre ne sont pas suivis.
- **Seule la cible voit le lancer**, comme le coup normal d'un monstre aujourd'hui.
- **Le lancer n'a lieu qu'à portée de mêlée**, au point où l'IA décide d'attaquer ; l'officiel tire avant le
  contrôle de distance et peut lancer une compétence à distance.
- La résistance aux états (`probability_on_hit`) n'est pas modélisée : un malus réussit toujours.

## 7. NON ÉTABLI

- Le champ exact de `SkillBase+0xc` (lu comme `is_harmful`).
- Les conditions des déclencheurs (types 2, 3, 4, 5 ; `CheckTriggerCondition` `0x14015c3b0`) et les fonctions
  Lua qu'ils appellent : aucune n'est dans le corpus.
- Que le client 7.3 affiche un état sur un monstre (déjà non vérifié pour les malus des joueurs).
