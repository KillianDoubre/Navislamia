namespace Navislamia.Game.Network.Packets.Enums;

public enum GamePackets : ushort
{
    TM_SC_SHOW_CREATE_GUILD = 650,
    TM_SC_OPEN_GUILD_WINDOW = 651,
    TM_SC_UPDATE_GUILD_ICON = 652,
    TM_SC_UPDATE_GUILD_BANNER = 653,
    TM_SC_SHOW_CREATE_ALLIANCE = 660,
    TM_SC_RESULT = 0,

    TM_CS_LOGIN = 1,
    TM_TIMESYNC = 2,
    TM_SC_ENTER = 3,
    TM_SC_LOGIN_RESULT = 4,
    TM_CS_MOVE_REQUEST = 5,
    TM_CS_REGION_UPDATE = 7,
    TM_SC_MOVE = 8,
    TM_SC_LEAVE = 9,
    TM_SC_SET_TIME = 10,
    TM_SC_REGION_ACK = 11,
    TM_SC_WARP = 12,
    TM_CS_QUERY = 13,
    TM_CS_ENTER_EVENT_AREA = 15,
    TM_CS_LEAVE_EVENT_AREA = 16,
    TM_CS_CHAT_REQUEST = 20,
    TM_SC_CHAT_LOCAL = 21,
    TM_SC_CHAT = 22,
    TM_SC_CHAT_RESULT = 24,
    TM_CS_ATTACK_REQUEST = 100,
    TM_SC_ATTACK_EVENT = 101,
    TM_CS_CANCEL_ACTION = 150,
    TM_CS_PUTON_ITEM = 200,
    TM_CS_PUTOFF_ITEM = 201,
    /// <summary><c>TS_TRADE</c>: the player trade, the same 97-byte frame in both directions.</summary>
    TM_TRADE = 280,
    TM_CS_PUTON_ITEM_SET = 281,
    // TM_SC_ITEM_DROP_INFO (282): what a monster leaves on the ground, 15 bytes with the 7 byte header —
    // monster_handle @7, item_handle @11. Server to client only (the 7.3 client builds none of it), sent by
    // MonsterDropItemToWorld (0x140043cc0) for the loot and for the gold alike; rzu gates the id to 282
    // below EPIC_9_6_3, so no other name is declared here.
    // See docs/packet-specs/socle-recompenses-monstres.md §3.5.
    TM_SC_ITEM_DROP_INFO = 282,
    TM_SC_WEAR_INFO = 202,
    TM_CS_DROP_ITEM = 203,
    TM_SC_ITEM_WEAR_INFO = 287,
    TM_SC_INVENTORY = 207,
    TM_CS_ERASE_ITEM = 208,
    TM_SC_ERASE_ITEM = 209,
    TM_CS_TAKE_ITEM = 204,
    TM_SC_DROP_RESULT = 205,
    TM_SC_TAKE_ITEM_RESULT = 210,
    // TM_SC_GET_CHAOS (213): the chaos a character just gained, 25 bytes with the 7 byte header — hPlayer @7,
    // hCorpse @11, nChaos @15, then the two one-byte bonuses of 7.3 and nBonus @21. Server to client only,
    // broadcast to the region by procDropChaos (0x1400b6d08 writes the length 0x19 and the id 0xd5); rzu
    // gates the id to 213 below EPIC_9_6_3.
    // See docs/packet-specs/socle-recompenses-monstres.md §3.2.
    TM_SC_GET_CHAOS = 213,
    TM_CS_PUTON_CARD = 214,
    // The storage family, declared with the item ids rather than after TM_CS_VERSION, where the sibling
    // packet branches anchor their own members.
    TM_SC_OPEN_STORAGE = 211,
    TM_CS_STORAGE = 212,
    TM_SC_BELT_SLOT_INFO = 216,
    TM_SC_ITEM_COOL_TIME = 217,
    TM_CS_CHANGE_ITEM_POSITION = 218,
    TM_CS_ARRANGE_ITEM = 219,
    // TM_CS_PUTOFF_CARD (215): the client's card-removal request, 8 bytes — a 7-byte header plus a single
    // signed ordinal at offset 7 (0..5 in its own object table, 0xFF when the target is not in it). rzu
    // remaps the id to 1215 from EPIC_9_6_3 on, so 1215 must not be declared here. There is no server to
    // client answer in this family. See docs/packet-specs/215-putoff-card.md. The line sits after 219
    // rather than next to 214: the siblings 214, 211/212, 221, 223 and 281 all anchor their members
    // between TM_CS_PUTOFF_ITEM = 201 and TM_SC_HAIR_INFO = 220, and an isolated line stays out of it.
    TM_CS_PUTOFF_CARD = 215,
    TM_CS_USE_ITEM = 253,
    TM_CS_DONATE_ITEM = 258,
    TM_SC_DESTROY_ITEM = 254,
    TM_SC_UPDATE_ITEM_COUNT = 255,
    // The sell gesture (252), declared with the other item ids rather than in the trade block
    // (240-283), where the sibling packet branch anchors its own buy member.
    // See docs/packet-specs/252-sell-item.md.
    TM_CS_SELL_ITEM = 252,

    // The crafting and item-enchantment family. Epic 7.3 keeps the low ids (rzu remaps them to
    // +1000 from EPIC_9_6_3 on, which is above EPIC_7_3 = 0x070300). TM_SC_SHOW_SOULSTONE_CRAFT_WINDOW
    // has no established 7.3 id (rzu and NGemity both declare it on 259, where op_codes.md declares
    // TM_CS_DONATE_REWARD) and is therefore deliberately absent.
    // See docs/packet-specs/socle-artisanat-objets.md §1 and §5.1.
    TM_CS_MIX = 256,
    TM_SC_MIX_RESULT = 257,
    TM_CS_SOULSTONE_CRAFT = 260,
    TM_SC_SHOW_SOULSTONE_REPAIR_WINDOW = 261,
    TM_CS_REPAIR_SOULSTONE = 262,
    TM_CS_TRANSMIT_ETHEREAL_DURABILITY = 263,
    TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT = 264,

    TM_CS_DONATE_REWARD = 259,
    TM_SC_HAIR_INFO = 220,
    TM_CS_HIDE_EQUIP_INFO = 221,
    TM_SC_HIDE_EQUIP_INFO = 222,
    TM_CS_SWAP_EQUIP = 223,
    TM_SC_SKIN_INFO = 224,
    TM_SC_NPC_TRADE_INFO = 240,
    TM_SC_MARKET = 250,

    // TM_CS_BUY_ITEM (251): the client sends one frame per catalogue line the player validated, so the
    // open market, the gold and the item have to be judged frame by frame. rzu remaps the id to 1251 from
    // EPIC_9_6_3 on (TS_CS_BUY_ITEM.h:14-15), which is above EPIC_7_3: 1251 must never be declared here,
    // and 7.3 carries the uint16 buy_count (the uint8 variant died at EPIC_4_1).
    // See docs/packet-specs/251-buy-item.md.
    TM_CS_BUY_ITEM = 251,
    TM_SC_USE_ITEM_RESULT = 283,
    TM_CS_BIND_SKILLCARD = 284,
    TM_CS_UNBIND_SKILLCARD = 285,
    TM_SC_SKILLCARD_INFO = 286,
    TM_SC_ADD_SUMMON_INFO = 301,
    TM_SC_REMOVE_SUMMON_INFO = 302,
    TM_EQUIP_SUMMON = 303,

    // TM_CS_SUMMON (304): rzu declares the frame but the 7.3 client never builds it — summoning goes
    // through the summon creature skill, i.e. TM_CS_SKILL (400) — and NGemity has no handler for it
    // either ("Got unknown packet"). rzu also names 1304 from EPIC_9_6_3 on; 1304 must never be declared
    // as the summon request, since in 7.3 it is TM_CS_AUCTION_BIDDED_LIST — declared under that name
    // below, in the 13xx band, since the 1304 lot landed (docs/packet-specs/1304-auction-bidded-list.md).
    // See docs/packet-specs/304-summon.md.
    TM_CS_SUMMON = 304,

    TM_SC_UNSUMMON = 305,
    TM_SC_UNSUMMON_NOTICE = 306,
    TM_SC_SUMMON_EVOLUTION = 307,

    // TM_SC_TAMING_INFO (310): the taming attempt of a monster, 16 bytes with the 7 byte header — mode
    // @7, tamer_handle @8, target_handle @12. rzu gates the id: 310 below EPIC_9_6_3, 1310 from there on,
    // so only 310 is declared here (docs/packet-specs/socle-apprivoisement-invocation.md §3.1, §4.1).
    // Server to client only: the 7.3 client routes the frame and builds none of it, so an incoming one
    // is logged and dropped in GameClient.cs like the familier (pet) family below.
    TM_SC_TAMING_INFO = 310,
    // TM_CS_CHANGE_SUMMON_NAME = 323 is declared inside the summon block, between 307 and 320, and not
    // after TM_SC_UNMOUNT_SUMMON (321) where its numeric rank would put it: the two boundaries of this
    // block are exactly where the sibling summon branches anchor their own members (304 after
    // TM_EQUIP_SUMMON, 324 after 321), so a third insertion point keeps the family together without
    // sharing one with them. The 9.6.3 remap to 1323 is above EPIC_7_3 and must not be declared here.
    // See docs/packet-specs/323-change-summon-name.md.
    TM_CS_CHANGE_SUMMON_NAME = 323,

    TM_SC_MOUNT_SUMMON = 320,
    TM_SC_UNMOUNT_SUMMON = 321,
    TM_CS_GET_SUMMON_SETUP_INFO = 324,

    // The familier (pet) socle, server-to-client half (docs/packet-specs/socle-familier-pet.md §9.1).
    // The 7.3 ids are 350-352: the 9.6.3 remap to 1350-1354 is dated and does not concern this
    // repository (fiche §6.1). All three are emitted by the server only — the 7.3 client routes them as
    // incoming frames and builds none of them — but each one still gets a log-and-drop arm in
    // GameClient.cs so that no member of this enum reaches the throwing switch (§14 of the sheet).
    TM_SC_UNSUMMON_PET = 350,
    TM_SC_ADD_PET_INFO = 351,
    TM_SC_REMOVE_PET_INFO = 352,

    // Pet rename, the Epic 7.3 pair 353/354 (docs/packet-specs/354-set-pet-name.md): 353 opens the
    // client's name box on a handle the server chooses, 354 carries the name back with that very handle.
    // 353 is server to client only and gets a log-and-drop arm like 350-352.
    TM_SC_SHOW_SET_PET_NAME = 353,
    TM_CS_SET_PET_NAME = 354,

    // The pet pickup filter (PET_PICKUP_FILTER option), 15 bytes: handle @7, value @11. The meaning of
    // the value is not established, so it is kept and logged, never applied (socle-familier-pet.md §17).
    TM_CS_SET_PET_FILTER = 355,

    TM_CS_SKILL = 400,
    TM_SC_SKILL = 401,
    TM_CS_LEARN_SKILL = 402,
    TM_SC_SKILL_LIST = 403,
    TM_SC_ADDED_SKILL_LIST = 404,
    TM_SC_AURA = 407,
    TM_CS_REQUEST_REMOVE_STATE = 408,
    TM_CS_JOB_LEVEL_UP = 410,

    // TM_CS_SUMMON_CARD_SKILL_LIST (452): the client asks for the skill list of the summon tied to a
    // creature card, 11 bytes — a 7 byte header plus a single uint32 item_handle at offset 7. rzu gates
    // the id to 452 below EPIC_9_6_3 (1452 only from 9.6.3 on), so 1452 must not be declared here. The
    // server reads and logs the frame and answers nothing: no reference implements 452, and the
    // card -> summon resolution its hypothetical answer (TM_SC_SKILL_LIST, 403) would need is not
    // established. See docs/packet-specs/452-summon-card-skill-list.md.
    TM_CS_SUMMON_CARD_SKILL_LIST = 452,

    // Player booths (docs/packet-specs/socle-booths.md). Epic 7.3 ids: the 9.6.3 remap (1700/1701)
    // does not concern this repository. TM_CS_CHECK_BOOTH_STARTABLE (711) used to be called absent
    // here; it is declared since the 7.3 client does build and send that 7 byte frame (SFrame.exe
    // builder VA 0x48CFD0, one call site 0x49A176) — what stops at 710 is its *incoming* dispatcher,
    // not its emission. rzu gates the id to 1711 from EPIC_9_6_3 on, so 1711 must not be declared.
    // See docs/packet-specs/711-check-booth-startable.md. The observation trio (702/703/704)
    // is declared with the visibility socle (docs/packet-specs/socle-booths-visibilite.md §1, §4.1):
    // 703 is server to client and still needs a receive arm of its own, because a declared member
    // without one reaches the throwing switch of the receive loop.
    TM_CS_START_BOOTH = 700,
    TM_CS_STOP_BOOTH = 701,
    TM_CS_WATCH_BOOTH = 702,
    TM_SC_WATCH_BOOTH = 703,
    TM_CS_STOP_WATCH_BOOTH = 704,

    // The booth trade (docs/packet-specs/705-buy-from-booth.md): 705/706/707 come from the client and
    // are dispatched; 708/709/710 only go to it, and share one log-and-drop receive arm.
    TM_CS_BUY_FROM_BOOTH = 705,
    TM_CS_SELL_TO_BOOTH = 706,
    TM_CS_GET_BOOTHS_NAME = 707,
    TM_SC_GET_BOOTHS_NAME = 708,
    TM_SC_BOOTH_CLOSED = 709,
    TM_SC_BOOTH_TRADE_INFO = 710,
    TM_CS_CHECK_BOOTH_STARTABLE = 711,

    TM_SC_STATUS_CHANGE = 500,
    TM_SC_STATE = 505,
    TM_SC_HPMP = 509,
    TM_CS_UPDATE = 503,
    TM_SC_PROPERTY = 507,
    TM_CS_SET_PROPERTY = 508,
    TM_CS_TARGETING = 511,
    TM_CS_RESURRECTION = 513,
    TM_SC_REGEN_HPMP = 516,
    TM_CS_MONSTER_RECOGNIZE = 517,
    TM_CS_GET_REGION_INFO = 550,
    TM_SC_QUEST_LIST = 600,
    TM_SC_QUEST_STATUS = 601,
    TM_CS_DROP_QUEST = 603,
    TM_CS_QUEST_INFO = 604,
    TM_CS_END_QUEST = 605,

    // TM_CS_TURN_ON_PK_MODE (800): the 7.3 client's PK mode switch. A header-only frame (7 bytes, empty
    // body) built by the client itself (SFrame.exe+0x684bb0, id 0x320): rzu declares no field for it and
    // the server answers nothing — the state reaches the client through bit 11 of the status mask of
    // TM_SC_STATUS_CHANGE (500) only. rzu renames the id to 1800 from EPIC_9_6_3 on, above EPIC_7_3, so
    // 1800 must not be declared here; its twin 801 (turn off) belongs to its own branch. See
    // docs/packet-specs/800-turn-on-pk-mode.md.
    TM_CS_TURN_ON_PK_MODE = 800,

    // TM_CS_TURN_OFF_PK_MODE (801): the 7.3 client's PK mode switch, the extinguished twin of 800. A
    // header-only frame (7 bytes, empty body) built by the client itself (SFrame.exe+0x684c00, id 0x321):
    // rzu declares no field for it and the server answers nothing — the state reaches the client through
    // bit 11 of the status mask of TM_SC_STATUS_CHANGE (500) only, and that same bit is what the client
    // tests (SFrame.exe+0x68b006) to choose between 800 and 801. rzu renames the id to 1801 from
    // EPIC_9_6_3 on, above EPIC_7_3, so 1801 must not be declared here.
    // See docs/packet-specs/801-turn-off-pk-mode.md.
    TM_CS_TURN_OFF_PK_MODE = 801,

    TM_CS_CHANGE_LOCATION = 900,
    TM_SC_WEATHER_INFO = 902,
    TM_CS_GET_WEATHER_INFO = 903,
    TM_SC_STAT_INFO = 1000,
    TM_SC_GOLD_UPDATE = 1001,
    TM_SC_LEVEL_UPDATE = 1002,
    TM_SC_EXP_UPDATE = 1003,

    TM_CS_GAME_TIME = 1100,
    TM_SC_GAME_TIME = 1101,

    TM_SC_EMOTION = 1201,
    TM_CS_EMOTION = 1202,

    // TM_CS_AUCTION_SEARCH (1300): the auction house search request, 51 bytes. The 7.3 client builds
    // and sends it (SFrame.exe constructor 0x48CA20, sender 0x48DC80) and never receives it — its
    // incoming dispatcher leaves 1300 on the "unhandled" path, only 1201/1301/1303/1305 have a
    // handler. See docs/packet-specs/1300-auction-search.md.
    TM_CS_AUCTION_SEARCH = 1300,

    TM_SC_AUCTION_SEARCH = 1301,

    // TM_CS_AUCTION_SELLING_LIST (1302): the request for the character's own sale announcements, 11
    // bytes. The 7.3 client builds and sends it (SFrame.exe constructor-and-sender 0x48DD10, reached
    // from the single stub 0x49E37D) and never receives it — its incoming dispatcher leaves 1302 on the
    // "unhandled" path (index 101 of 0x67E67A). 1300 is TM_CS_AUCTION_SEARCH, 1304 in this version is
    // TM_CS_AUCTION_BIDDED_LIST.
    // See docs/packet-specs/1302-auction-selling-list.md.
    TM_CS_AUCTION_SELLING_LIST = 1302,

    TM_SC_AUCTION_SELLING_LIST = 1303,

    // TM_CS_AUCTION_BIDDED_LIST (1304): the request for the announcements the character has bid on, 11
    // bytes — the same frame as TM_CS_AUCTION_SELLING_LIST (1302), with another Id, built and sent by
    // the same client code (SFrame.exe constructor-and-sender 0x48DDA0, reached from the single stub
    // 0x49E387, which is the only caller in the whole .text). 1304 must never be declared as the summon
    // request either: rzu names 1304 from EPIC_9_6_3 on, but in 7.3 it is this auction request and
    // TM_CS_SUMMON stays 304 (see the TM_CS_SUMMON comment above and docs/packet-specs/304-summon.md).
    // See docs/packet-specs/1304-auction-bidded-list.md.
    TM_CS_AUCTION_BIDDED_LIST = 1304,

    TM_SC_AUCTION_BIDDED_LIST = 1305,

    // TM_CS_AUCTION_BID (1306): the "bid" request of the auction house, 19 bytes — the 7 byte header,
    // the int32 auction_uid at 7 and the int64 price at 11, the only 64-bit price among the family's
    // requests. The 7.3 client builds and sends it (SFrame.exe construction routine 0x48CA70 and sender
    // 0x48DE30, whose single caller is the stub 0x49E397) and never receives it. It is the one act of
    // the family with no dedicated TS_SC_AUCTION_* answer: the client reads a TM_SC_RESULT (id 0)
    // carrying request_msg_id = 1306, an explicit case of its result handler 0x66DB80. rzu declares the
    // id for version < EPIC_9_6_3 and remaps it to 2306 above, so 2306 must never be declared here.
    // See docs/packet-specs/1306-auction-bid.md.
    TM_CS_AUCTION_BID = 1306,
    // TM_CS_AUCTION_INSTANT_PURCHASE (1308): the "buy this announcement at its fixed price" request of
    // the auction house, 11 bytes — the 7 byte header plus a single auction_uid, read as a uint32 at
    // offset 7 (the collection's own choice, socle-encheres.md §3.5). The 7.3 client builds and sends it
    // (SFrame.exe construction routine 0x48DEA0, whose single caller is the stub 0x49E3A1, reached by the
    // internal key 1159 = 0x487) and never receives it. It has no dedicated answer: no
    // TS_SC_AUCTION_INSTANT_PURCHASE exists in any reference, and the client's result handler 0x66DB80
    // reads a TM_SC_RESULT (id 0) carrying request_msg_id = 1308, with a case of its own for 0x51C and
    // its own Korean label. rzu declares the id for version < EPIC_9_6_3 and remaps it to 2308 above, so
    // 2308 must never be declared here.
    // See docs/packet-specs/1308-auction-instant-purchase.md.
    TM_CS_AUCTION_INSTANT_PURCHASE = 1308,
    // TM_CS_AUCTION_REGISTER (1309): the "put this item up for auction" request of the auction house,
    // 32 bytes — the widest and the only two-price frame of the family: the 7 byte header, item_handle
    // (uint32) at 7, item_count (int32) at 11, start_price (int64) at 15, instant_purchase_price (int64)
    // at 23 and duration_type (uint8) at 31. The 7.3 client builds and sends it (SFrame.exe construction
    // routine 0x48DF30, whose single caller is the stub 0x49E3AE, reached by the internal key 1160 = 0x488
    // from the register button of SUIAuctionRegisterWnd) and never receives it. It has no dedicated
    // answer: no TS_SC_AUCTION_REGISTER exists in any reference, and the client reads a TM_SC_RESULT (id 0)
    // carrying request_msg_id = 1309, whose only dedicated treatment is a log line (0x66E07C) — it empties
    // its own item list after sending and never asks for the list again, so a refreshed listing is the
    // server's job. rzu declares the id for version < EPIC_9_6_3 and remaps it to 2309 above, so 2309 must
    // never be declared here.
    // See docs/packet-specs/1309-auction-register.md.
    TM_CS_AUCTION_REGISTER = 1309,
    // TM_CS_AUCTION_CANCEL (1310): the "withdraw this announcement" request of the auction house, 11
    // bytes — the 7 byte header plus a single uint32 auction_uid at 7, the exact shape of 1308 but for the
    // identifier. The 7.3 client builds and sends it (construction routine 0x48DFC0, whose single caller is
    // the stub 0x49E3BB, reached by the internal key 1161 = 0x489) and never receives it: it has no
    // dedicated TS_SC_AUCTION_* answer in any reference, and the window that emits it consumes no result
    // for 0x51E. rzu gates the id to 1310 below EPIC_9_6_3 and remaps it to 2310 above, so 2310 must never
    // be declared here — 1310 is even reassigned to TS_SC_TAMING_INFO from 9.6.3 on.
    // See docs/packet-specs/1310-auction-cancel.md.
    TM_CS_AUCTION_CANCEL = 1310,
    // The auction storage (keeping box) of the 7.3 client (SIMSG_REQ/RES_AUCTION_ITEM_KEEPING_LIST/TAKE): the list
    // request and its 3 859-byte answer, and the take request, X(<id>, version < EPIC_9_6_3) in rzu
    // (docs/packet-specs/socle-encheres-mecanique.md §3).
    TM_CS_ITEM_KEEPING_LIST = 1350,
    TM_SC_ITEM_KEEPING_LIST = 1351,
    TM_CS_ITEM_KEEPING_TAKE = 1352,

    TM_SC_DIALOG = 3000,
    TM_CS_DIALOG = 3001,
    TM_CS_CONTACT = 3002,

    TM_CS_RETURN_LOBBY = 23,
    TM_CS_REQUEST_RETURN_LOBBY = 25,
    TM_CS_REQUEST_LOGOUT = 26,
    TM_CS_LOGOUT = 27,
    TM_SC_DISCONNECT_DESC = 28,

    TM_CS_VERSION = 50,

    TM_CS_ANTI_HACK = 54,

    // TM_CS_CHECK_ILLEGAL_USER (57): the client's own security watch reports a suspected illegal program
    // here — never a player action. rzu gates the id to 57 below EPIC_9_6_3 (1057 only from 9.6.3 on), so
    // 1057 must not be declared. There is no server to client answer for it. See
    // docs/packet-specs/57-check-illegal-user.md.
    TM_CS_CHECK_ILLEGAL_USER = 57,

    // TM_CS_XTRAP_CHECK (59): the XTrap integrity check the client would send — 135 bytes, a 7 byte header
    // plus a fixed uint8[128] payload, with no length field. rzu gates the id to 59 below EPIC_9_6_3
    // (1059 only from 9.6.3 on), so 1059 must not be declared here. Its server to client counterpart is
    // 58 (TM_SC_XTRAP_CHECK): deliberately not declared, nothing in the server ever sends it and the 7.3
    // client parses it into an empty branch. See docs/packet-specs/59-xtrap-check.md.
    TM_CS_XTRAP_CHECK = 59,

    // TM_CS_REQUEST (60): the client's raw command channel, read and logged only (see
    // docs/packet-specs/60-request.md). rzu gates the id to 60 below EPIC_9_6_3 (1060 only from 9.6.3
    // on), so 1060 must not be declared.
    TM_CS_REQUEST = 60,

    // TM_CS_HUNTAHOLIC_BEGIN_HUNTING (4011): the "start the hunt" gesture of the HuntaHolic instance window,
    // 7 bytes — a bare header, no payload at all. rzu's DEF(_) is empty and its id is X(4011, true), a single
    // unconditional entry, so 7.3 keeps 4011 and no field has to be gated; the comment "Since EPIC_6_3" only
    // dates the family (EPIC_6_3 = 0x060300 < EPIC_7_3). The 7.3 client builds this frame from a single site
    // (0x564238, control button_entrance_01 of SUIHuntaHolicInstanceWnd) and never reads one back: its receive
    // dispatcher routes 4011 to the "unhandled message" branch, so there is no answer to write. The member sits
    // here, next to the other singletons of the 50s-60s, rather than at the end of the enum: the 4000-4012 block
    // and the insertion slot after TM_CS_SECURITY_NO are already claimed by the sibling HuntaHolic branches, and
    // GamePackets.cs is grouped by family rather than sorted by value. See
    // docs/packet-specs/4011-huntaholic-begin-hunting.md.
    TM_CS_HUNTAHOLIC_BEGIN_HUNTING = 4011,

    TM_CS_CHARACTER_LIST = 2001,

    TM_CS_CREATE_CHARACTER = 2002,

    TM_CS_DELETE_CHARACTER = 2003,

    TM_CS_ACCOUNT_WITH_AUTH = 2005,

    TM_CS_CHECK_CHARACTER_NAME = 2006,

    // TM_CS_HUNTAHOLIC_INSTANCE_LIST : the page request of the HuntaHolic lobby room list, X(4000, true) in rzu,
    // so no version gating and no gated payload field. Only this id of the 4000-4012 family is declared here:
    // its siblings (4001/4002 lobby list and info, 4003 create, 4004 join, and the rest) join with their own
    // lots, and no server to client id of the family is emitted yet. See
    // docs/packet-specs/4000-huntaholic-instance-list.md.
    TM_CS_HUNTAHOLIC_INSTANCE_LIST = 4000,

    // TM_CS/SC_INSTANCE_GAME_* : instance game socle, X(<id>, true) in rzu (EPIC_6_3 and later, hence valid
    // for EPIC_7_3). See docs/packet-specs/socle-instances-jeu.md.
    TM_CS_INSTANCE_GAME_ENTER = 4250,
    TM_CS_INSTANCE_GAME_EXIT = 4251,
    TM_CS_INSTANCE_GAME_SCORE_REQUEST = 4252,
    TM_SC_INSTANCE_GAME_SCORE_REQUEST = 4253,

    // TM_CS_HUNTAHOLIC_CREATE_INSTANCE : creation of a HuntaHolic lobby room, X(4003, true) in rzu, so no
    // version gating and no gated payload field. Only this id of the 4000-4012 family is declared: its
    // siblings (4000/4001/4002 lobby list and info, 4004 join, and the rest) join with their own lots, and no
    // server to client id of the family is emitted yet. See
    // docs/packet-specs/4003-huntaholic-create-instance.md.
    TM_CS_HUNTAHOLIC_CREATE_INSTANCE = 4003,

    // TM_CS_COMPETE_* : the client to server half of the player competition socle (4500-4506), X(<id>, true) in
    // rzu, so no version gating and no gated field. Only the two frames the server reads are declared; the five
    // server to client ids join with lots C2-C4. See docs/packet-specs/socle-competition-joueurs.md.
    TM_CS_COMPETE_REQUEST = 4500,
    TM_CS_COMPETE_ANSWER = 4502,
    TM_SC_COMPETE_REQUEST = 4501,
    TM_SC_COMPETE_ANSWER = 4503,
    TM_SC_COMPETE_COUNTDOWN = 4504,
    TM_SC_COMPETE_START = 4505,
    TM_SC_COMPETE_END = 4506,

    TM_CS_RANKING_TOP_RECORD = 5000,
    TM_SC_RANKING_TOP_RECORD = 5001,

    // TM_CS/SC_*FARM* / FOSTER / RETRIEVE / NURSE / 6000-6008 : the creature farm socle. All nine ids are
    // X(<id>, true) in rzu under a "// Since EPIC_7_3" marker, so 7.3 keeps the plain ids and no field of the
    // family is version gated. Only the six ids the server reads or emits are declared; the three result frames
    // 6003/6005/6007 stay undeclared until a lot emits them (their `result` values are not established).
    // See docs/packet-specs/socle-ferme-creatures.md.
    TM_CS_REQUEST_FARM_INFO = 6000,
    TM_SC_FARM_INFO = 6001,
    TM_CS_FOSTER_CREATURE = 6002,
    TM_CS_RETRIEVE_CREATURE = 6004,
    TM_CS_NURSE_CREATURE = 6006,
    TM_CS_REQUEST_FARM_MARKET = 6008,
    // TM_CS_HUNTAHOLIC_JOIN_INSTANCE : entering a HuntaHolic lobby room, X(4004, true) in rzu — a single
    // unconditional entry, so no version gating, no id variant and no gated payload field. Only this id of
    // the 4000-4012 family is declared here: its siblings (4000/4001/4002 lobby list and info, 4003 create,
    // and the rest) join with their own lots, and no server to client id of the family is emitted yet. It is
    // declared in this slot rather than next to the other socle ids so that the insertion point does not sit
    // in the region the sibling HuntaHolic branches already claim; the enum is grouped by family, not sorted
    // by value (TM_CS_RETURN_LOBBY = 23 already sits among the 3000s).
    // See docs/packet-specs/4004-huntaholic-join-instance.md.
    TM_CS_HUNTAHOLIC_JOIN_INSTANCE = 4004,

    TM_CS_REPORT = 8000,

    // TM_CS_SECURITY_NO (9005): the security password the client sends back once the server has asked for
    // it with TM_SC_REQUEST_SECURITY_NO (9004) — 30 bytes, read and bounded but never verified (see
    // docs/packet-specs/9005-security-no.md §5.4). rzu remaps the id to 8105 from EPIC_9_6_3 on, so 8105
    // must not be declared here, and account(64)/result/security_no_1/_2 only exist from EPIC_9_6_7.
    TM_CS_SECURITY_NO = 9005,

    // TM_CS_HUNTAHOLIC_LEAVE_INSTANCE (4005): 7 bytes, no payload, X(4005, true) in rzu so no version gating
    // and no gated field. The 7.3 client sends it from its HuntaHolic instance window (control
    // button_entrance_02) and from its scoreboard window, and never reads one back (its receive dispatcher
    // routes 4005 to the "unhandled message" branch), so there is no answer to write. It is declared in this
    // slot rather than next to the other socle ids so that the insertion point stays out of the region the
    // sibling HuntaHolic branches already claim; the enum is grouped by family, not sorted by value.
    // See docs/packet-specs/4005-huntaholic-leave-instance.md.
    TM_CS_HUNTAHOLIC_LEAVE_INSTANCE = 4005,

    // The HuntaHolic lobby and hunt (docs/packet-specs/socle-huntaholic.md): the server to client frames of the
    // family, X(<id>, true) in rzu, and 4008, which rzu declares but no constructor of the 7.3 client builds.
    TM_SC_HUNTAHOLIC_INSTANCE_LIST = 4001,
    TM_SC_HUNTAHOLIC_INSTANCE_INFO = 4002,
    TM_SC_HUNTAHOLIC_HUNTING_SCORE = 4006,
    TM_SC_HUNTAHOLIC_UPDATE_SCORE = 4007,
    TM_CS_HUNTAHOLIC_LEAVE_LOBBY = 4008,
    TM_SC_HUNTAHOLIC_BEGIN_HUNTING = 4009,
    TM_SC_HUNTAHOLIC_MAX_POINT_ACHIEVED = 4010,
    TM_SC_HUNTAHOLIC_BEGIN_COUNTDOWN = 4012,

    TM_SC_COMMERCIAL_STORAGE_INFO = 10003,
    TM_SC_COMMERCIAL_STORAGE_LIST = 10004,
    TM_CS_TAKEOUT_COMMERCIAL_ITEM = 10005,
    TM_CS_OPEN_ITEM_SHOP = 10000,

    TM_NONE = 9999
}
