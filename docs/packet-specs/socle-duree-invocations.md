# Socle — durée des invocations : `TS_SC_UNSUMMON_NOTICE` (306) et `TS_SC_REMOVE_SUMMON_INFO` (302)

Livré le 2026-10-05. Carte Trello `SdPCfQhd`. Les deux trames étaient construites (`GameSummonPackets`,
11 et 15 octets, offsets vérifiés par `GameSummonPacketsTests`) mais n'avaient aucun appelant.

**La prémisse de la carte était à moitié juste.** La 306 est bien une durée, mais pas celle de toute
invocation : c'est celle de la **seconde invocation** (double invocation, `m_pSubSummon`). La 302 n'a **rien à
voir avec une durée** : c'est le retrait d'une créature de la fenêtre de créatures quand **sa carte quitte le
sac** (`StructPlayer::RemoveSummon`). Une invocation seule n'a aucune limite de temps.

## 1. Sources

Serveur officiel 2015 (`A:\Rappelz Kiff\Rappelz\Rappelz\program\server\GameServer`), et pour chaque point le
binaire Epic 7 `CaptainHerlockServer.exe` 2012-11 et sa PDB (`reference/epic7part4/server/2012-11`), qui
portent la même règle : les champs `m_bIsInfiniteSummonTime` (+0x2a95), `m_nDoubleSummonTime` (+0x2aa8) et
`m_nNextUnSummonTime` (+0x2aac) n'y sont lus et écrits que par les fonctions ci-dessous.

| Règle | Source 2015 | Binaire 2012 |
|---|---|---|
| Défaut 1000 ticks (10 s) | `GameRule.h:298`, `CalculateStat.cpp:5841` | `onBeforeCalculateStat` `0x140057168` (`0x3e8`) |
| + niveau × `var0` × 100 de Technical Creature Control (1881) | `CalculateStat.cpp:1580-1586` | `applyPassiveSkillEffect` `0x140071cd1` |
| + niveau × `value_4` × 100 par état `EF_CREATURE_PARAMETER_AMP` (112) | `CalculateStat.cpp:3445-3450` | `applyStatByState` `0x140067196` |
| Seconde invocation, 306, échéance | `StructPlayer::Summon`, `StructPlayer.cpp:10941-10980` | `0x1400ce4b0` (306 bâtie `0x1400ce5dc`-`0x1400ce608`) |
| Principale = la première rencontrée dans les `min(Creature Control, 6) − 1` premiers emplacements | `SetMainAndSubSummon`, `StructPlayer.cpp:11182-11232` | `0x1400c0f60` |
| Renvoi à l'échéance (`m_nNextUnSummonTime < t`), sauf état illimité | `OnUpdate`, `StructPlayer.cpp:3970-3974` | `OnUpdate` `0x1400e8cb8` |
| Renvoi de la principale : la seconde devient principale, plus d'échéance | `unSummon`, `StructPlayer.cpp:11059-11090` | `0x1400ebe02`, `0x1400ebe4a` |
| `EF_INFINITE_SUMMON_TIME` (3121) posé : 306 à 0 | `applyState`, `CalculateStat.cpp:3121-3135` | `applyState` `0x140080207` |
| … retiré : 306 à `value_0 × 100`, échéance = maintenant + autant | `onAfterRemoveState`, `StructPlayer.cpp:12008-12022` | `0x1400c84a7` |
| Sauvegarde `IN_SUB_SUMMON` / `IN_REMAIN_SUMMON_TIME` | `DB_UpdateCharacter.cpp:140-147` | `0x14033b91c` |
| Restauration et 306 du temps restant à la connexion | `DB_Login.cpp:2271-2281`, `SendCharacterInfo` `StructPlayer.cpp:7985-8006` | `0x14030b402`, `0x1400f0488` |
| Pas de nouveau sort d'invocation tant qu'une seconde est dehors | `StructSkill.cpp:1319-1325` | — |
| 302 : la carte quitte le sac | `onRemove` → `RemoveSummon`, `StructPlayer.cpp:8528-8549`, `4226-4244` ; `SendMessage.cpp:1318-1324` | `RemoveSummon` `0x1400c8a50`, `SendRemoveSummonMessage` `0x14014bac0` |
| 301 : une carte liée entre dans le sac | `onAdd` → `AddSummon` + `SendSkillMessage`, `StructPlayer.cpp:8389-8405` | — |

Le client 7.3 traite les deux trames (`SGameVM::OnUnsummonNotice`, `OnRemoveSummonInfo` dans toutes les PDB
client du dump). Données Epic 7 : 1881 a `var1 = 7` (7 s par niveau) ; l'état 314012 est le seul
`EF_INFINITE_SUMMON_TIME` (`value_0 = 3`) ; 12682, 214011 et 314011 sont `EF_CREATURE_PARAMETER_AMP`
(`value_4 = 10`).

## 2. Ce qui est porté

`DoubleSummonRules` (pur) et `CreatureService` :

- **4001 avec une principale dehors** : la nouvelle créature entre comme seconde. `Order` désigne la principale,
  `TS_SC_UNSUMMON_NOTICE` part au maître sur le handle de la seconde avec `m_nDoubleSummonTime` (0 sous un état
  illimité), et `ConnectionInfo.NextUnsummonTick` retient l'échéance. **Remplace** la règle NGemity qui renvoyait
  la principale avant d'invoquer. `CheckSummon` refuse (`NotActable`) tout 4001 tant qu'une seconde est dehors.
- **Échéance** : `ProcessSubSummons`, chaque seconde sur le tick des créatures, renvoie la seconde
  (`Unsummon` : 305 puis 9) quand l'échéance est passée et qu'aucun état illimité ne la tient.
- **États** : l'état illimité est relevé au même tick ; son apparition envoie 306 à 0, sa disparition 306 à
  `value_0 × 100` et repousse l'échéance d'autant.
- **Renvoi** : renvoyer la seconde efface l'échéance ; renvoyer la principale fait de la seconde la principale.
- **Connexion / sortie** : `Characters.SubSummonId` et `RemainSummonTime` (colonnes existantes, aucune
  migration) sont écrits à la sortie du monde (`ICharacterService.SaveSubSummonAsync`) ; à l'entrée, la seconde
  revient après la principale et reçoit une 306 du temps restant (0 s'il est épuisé, puis renvoi au tick suivant
  comme l'officiel). **Correction au passage** : la principale ramenée à la connexion n'était pas reprise dans
  `MainSummonCardId`.
- **302 / 301** : `CreatureService` écoute `IInventoryChangeFeed` (publié après chaque opération d'inventaire,
  y compris échange, étal, entrepôt, enchères) et relit les cartes du joueur en ligne (`RefreshCardsAsync`, une
  lecture à la fois par session). `ApplyCardChanges` : une carte liée disparue → **302 sur le handle de la
  carte** ; une carte liée apparue → 301, stats et compétences (`SendSummonInfo`) ; une pile de cartes vierges
  ne change que son nombre. Une carte détruite par l'artisanat (`CraftingSocleService`, enchantement raté)
  envoie aussi sa 302.
- **La ligne `Summons` suit sa carte** : `GetCreatureStateAsync` réattribue au personnage (sous son verrou) les
  invocations dont il tient la carte. Avant, une carte reçue par échange, étal ou entrepôt arrivait **sans sa
  créature** (seules les enchères déplaçaient la ligne).

## 3. Écarts assumés

1. Les états 3121 sont observés chaque seconde, pas à chaque passe de stats ; l'officiel renvoie une 306 à 0 à
   **chaque** recalcul pendant que l'état tient, ici une seule fois.
2. La 306 part après l'`ENTER` de la seconde (l'officiel la met en file avant) : rien n'est envoyé si l'entrée
   échoue. Le handle est connu du client depuis la 301 de la formation.
3. `SetMainAndSubSummon` sans emplacement trouvé : l'officiel `assert` (et viderait la principale en release) ;
   ici la paire est gardée.
4. La 302/301 suit la relecture en base après l'opération, pas l'opération elle-même.
5. ~~`IsErasable` n'est pas porté~~ : porté le 2026-10-05, voir §6. Le renvoi de la créature et le vidage de
   l'emplacement avant la 302 restent comme filet (une carte détruite par l'artisanat, une opération future).
6. La garde de niveau en donjon de `StructPlayer::Summon` 2012 (`bLimitDungeonEnterableLevel`) n'est pas portée.

## 4. Vérifications

`CreatureTests.DoubleSummon.cs` : règles pures (durée, ordre, échéance et repli d'horloge), seconde invocation
avec 306 (handle, 2400 ticks pour 1881 niveau 2) puis renvoi à l'échéance, ordre principale/seconde, renvoi de
la principale, état illimité (306 à 0, puis 300), état 112, connexion (306 à 500, sauvegarde à la sortie),
302/301 sur les cartes, réattribution de la ligne `Summons` et persistance (base en mémoire).

En jeu : invoquer deux créatures formées → le compte à rebours apparaît sur la seconde, elle repart après 10 s
(+ 7 s par niveau de 1881) ; échanger une carte liée → elle disparaît de la fenêtre de créatures du donneur et
apparaît, avec sa créature, chez le receveur.

## 5. NON ÉTABLI

- Le rendu exact de la 306 dans le client 7.3 (compte à rebours affiché, sens d'une durée 0).
- Ce que le client fait du compte à rebours d'une seconde devenue principale (l'officiel n'envoie rien).
- L'équipement porté par une créature cédée reste dans le sac de l'ancien propriétaire (`EquippedBySummonId`).

## 6. Ce qui ne quitte pas le sac — `IsErasable` (2026-10-05)

Source : `StructPlayer::IsErasable` (`StructPlayer.cpp:12498-12557`), dont partent `IsTradable` (`:12635`),
`IsSellable` (`:12622`), `IsDropable` (`:12656`), `IsMixable` (`:12465`), `MoveInventoryToStorage` (`:3458`) et
`EraseItem_` (`:2792`). La partie que seule la session connaît est `Creatures/HeldItemRules.IsErasable` :

- une **carte formée** (`m_aBindSummonCard`, `ConnectionInfo.SummonSlots`) ;
- une carte **dont la créature est dans le monde** (`GetSummonStruct()->IsInWorld()`) ;
- ce qui est sur une **ceinture** (`m_aBeltSlotCard`, cartes et équipement de ceinture, `BeltItemIds`) ;
- la **cage du familier dehors** (`IsPetCage() && GetPetStruct()->IsInWorld()`).

Le reste (porté, dans l'entrepôt, à quelqu'un d'autre) est déjà jugé à chaque site. Sites et réponses, ceux de
l'officiel :

| Site | Réponse |
|---|---|
| Échange 280 (`onAddItem`) | `NotActable` ; **une carte apprivoisée ni formée ni dehors s'échange** (la garde « carte liée » du dépôt, sans source, est retirée) ; la carte d'un apprivoisement en cours non plus (`ITEM_FLAG_TAMING`) |
| Vente 252 (`IsSellable`) | `NotExist`, comme les autres refus de ce site ; carte d'apprivoisement en cours aussi |
| Étal 705 (objets du propriétaire, `GameMessage.cpp:9181`) et 706 (objets offerts) | `NotActable` |
| Enchère (`AuctionManager.cpp:573`) | `NotActable` (remplace le seul test de formation) |
| Entrepôt 212 mode 0 | rien : `MoveInventoryToStorage` rend `false`, `onStorage` n'y répond pas |
| Destruction 208 | l'objet est sauté, comme `onEraseItem` ; `NotExist` si rien n'a été détruit |
| Lâcher 203 | `205 { handle, 0 }` |
| Artisanat 256 (cible et matériaux, `GameMessage.cpp:8075`) | `NotExist` avec le handle ; enchanter une carte formée exige donc de la retirer de la formation, comme l'officiel |

**L'équipement d'une créature cédée.** Les objets d'une invocation sont dans le sac du maître avec leur
emplacement ; `SetMaster` ne les déplace pas. À la connexion suivante de l'ancien maître,
`DB_Login::readEquipItemList` (`DB_Login.cpp:1623-1640`) ne trouve plus l'invocation (`GetSummon`, qui ne cherche
que dans le sac) et saute l'objet : il revient au sac, non porté. Ici, `CreatureService.OnWorldEntryAsync` fait de
même : un objet dont `EquippedBySummonId` n'est aucune des invocations du personnage est déséquipé
(`UnwearItemsAsync`, qui efface désormais aussi `EquippedBySummonId`). Il restait sinon « porté » pour toujours,
ni utilisable ni cessible.

**Écarts.** La garde NGemity du lâcher (carte liée refusée, `GroundItemDropRules`) est gardée : l'officiel
laisserait tomber une carte apprivoisée non formée. Une carte de compétence liée à une créature
(`GetBindedCreatureHandle`) et une carte de la ferme (`ITEM_FLAG_FARMED_SUMMON`) ne sont pas modélisées.
La consommation par quête (`EraseItem_`) ne lit pas encore la règle.

Tests : `HeldItemRulesTests` (règle, échange, entrepôt, destruction, déséquipement),
`CreatureTests.World_entry_unwears_the_items_of_a_summon_that_is_no_longer_the_characters`.

En jeu : former une créature puis la proposer à l'échange → refusée ; la retirer de la formation → acceptée ;
l'autre joueur la reçoit dans sa fenêtre de créatures. Si elle portait un objet, il revient au sac du donneur à sa
prochaine connexion.
