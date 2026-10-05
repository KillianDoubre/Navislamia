# 451 — TS_SC_SKILL_LEVEL_LIST

Livré le 2026-10-05. Sources officielles 2015 sous le chemin indiqué dans
[322](322-show-summon-name-change.md), mêmes références SFrame et rzu épinglées.

## Trame Epic 7.3

Serveur → client, **9 + 5 × count octets**. En-tête commun de 7 octets.

| Offset | Type | Champ | Source |
|---|---|---|---|
| 7 | uint16 | nombre de compétences | `Game/Message/GameMessage.h:2097` ; `TS_SC_SKILL_LEVEL_LIST.h:15-16` |
| 9 + 5 × i | int32 | id de compétence | `GameMessage.h:2093` ; rzu `:8` |
| 13 + 5 × i | octet | niveau de base | `GameMessage.h:2094` ; rzu `:9` ; `SendMessage.cpp:2306` |

**Aucun handle, cooldown ou modification_type**. L'officiel stocke le niveau comme
`unsigned char`, rzu comme `int8_t` ; ils ont la même largeur sur le fil. Les compétences
importées utilisent des niveaux positifs compatibles.

SFrame : branche `0x67E3B5`, gestionnaire `0x672340`, count en `0x672388`, stride **5** en
`0x672397`, données à **+9** en `0x6723B3`. Mesure reproduite avec `tools/client73_packets.py`
et Capstone. rzu `TS_SC_SKILL_LEVEL_LIST.h:18-21` : disponible depuis 7.3, id 451, remap
1451 seulement en 9.6.3.

## Traitement porté

`GameMessage.cpp:9919-9945` répond à **452**, qui porte le handle de carte :
invocation liée → 451 avec ses compétences ; carte sans invocation → 451 vide ; objet absent
→ `TS_SC_RESULT(452, RESULT_NOT_EXIST)`. `CreatureService` utilise les cartes détenues dans
`ConnectionInfo.CreatureCards` et leurs compétences réellement chargées dans `SummonSkills`.
La résolution est globale comme celle du C++ (section suivante).

`SendMessage.cpp:2290-2335` écrit les **niveaux de base**, sans bonus de carte de compétence,
et borne les entrées au tampon officiel de 4096 octets : au plus 817 entrées, trame de 4094 octets.
403 reste utilisé au chargement et lors de l'apprentissage ; il n'est plus la réponse du flip.
451 présenté par un client est consommé sans effet. Voir la réserve distincte sur
[450](450-skill-level-list.md).

## Vérifications

`SmallPacketsTests` : en-tête, count, offsets des deux entrées, absence de handle, liste vide,
tampon officiel. `SummonSkillTests` et `CreatureTests.SmallPackets` : données réelles de la carte,
liste vide et refus de handle absent. En jeu : retourner une carte apprivoisée avec des compétences
apprises, puis une carte vide ; vérifier le contenu et les niveaux affichés.

## Carte d'un autre joueur (relecture du 2026-10-05)

`onSummonCardSkillList` trouve l'objet par `StructItem::FindItem`, qui est global : une carte montrée dans une fenêtre
d'échange ou un étal se retourne comme une carte possédée. `CreatureService.SendCardSkillListAsync` cherche donc la carte
chez le joueur, puis chez les joueurs en ligne.

**Carte hors ligne (correction du 2026-10-05, carte Trello `4Q0cCCsj`).** L'officiel garde en mémoire les objets des
enchères, si bien que `FindItem` les trouve ; ici, une carte qu'aucune session ne tient (lot d'enchère : ligne `Items` sans
personnage ni compte ; carte d'un joueur hors ligne) est lue en base par `ICharacterService.GetCardSkillsAsync` :
`Items.Id == handle` avec `Amount > 0`, puis `SummonSkills` de l'invocation dont `CardItemId` est cette carte (lecture
sans suivi, sans verrou de personnage). Mêmes trois issues que la mémoire : liste des niveaux de base, liste vide pour une
carte sans invocation, `TS_SC_RESULT(452, NotExist)` pour un objet absent ou une pile épuisée. Une erreur de lecture est
journalisée et répond `NotExist`. Le handle d'un objet est `(uint)ItemEntity.Id` partout (`ItemFixedInfoWriter`), donc le
handle montré par la 1301 est celui que la 452 renvoie. Tests : `CreatureTests.A_card_on_sale_reads_its_summon_skills_from_the_database`
(base en mémoire : carte liée, carte vide, pile épuisée, id absent) et
`Card_flip_reads_the_card_of_an_offline_owner_from_the_database` (service).

## NON ÉTABLI

- Aucun constructeur ni traitement actif de 450 n'a été établi : le lien fonctionnel prouvé
  est **452 → 451**, pas 450 → 451.
- Les infobulles exactes de la fenêtre ne sont pas établies par la trame. Une capture en jeu
  confirmera le rendu de la liste.
