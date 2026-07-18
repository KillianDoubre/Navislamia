using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Extensions;
using Navislamia.Game.Network.Extensions;
using Navislamia.Game.Network.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Auth;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Network.Packets.Interfaces;
using Navislamia.Game.Services;
using Serilog;

namespace Navislamia.Game.Network.Clients.Actions;

public class GameActions : IActions
{
    private readonly ILogger _logger = Log.ForContext<GameActions>();
    private readonly ICharacterService _characterService;
    private readonly IBannedWordsRepository _bannedWordsRepository;
    private readonly IStatService _statService;
    private readonly INpcSpawnService _npcSpawnService;
    private readonly IMonsterSpawnService _monsterSpawnService;
    private readonly NetworkService _networkService;

    private readonly Dictionary<ushort, Action<GameClient, IPacket>> _actions = new();

    public GameActions(NetworkService networkService)
    {
        _networkService = networkService;
        _bannedWordsRepository = networkService.BannedWordsRepository;
        _characterService = networkService.CharacterService;
        _statService = networkService.StatService;
        _npcSpawnService = networkService.NpcSpawnService;
        _monsterSpawnService = networkService.MonsterSpawnService;

        _actions.Add((ushort)GamePackets.TM_CS_VERSION, OnVersion);
        _actions.Add((ushort)GamePackets.TM_CS_LOGIN, OnLogin);
        _actions.Add((ushort)GamePackets.TM_CS_REPORT, OnReport);
        _actions.Add((ushort)GamePackets.TM_CS_CHARACTER_LIST, OnCharacterList);
        _actions.Add((ushort)GamePackets.TM_CS_CREATE_CHARACTER, OnCreateCharacter);
        _actions.Add((ushort)GamePackets.TM_CS_DELETE_CHARACTER, OnDeleteCharacter);
        _actions.Add((ushort)GamePackets.TM_CS_CHECK_CHARACTER_NAME, OnCheckCharacterName);
        _actions.Add((ushort)GamePackets.TM_CS_ACCOUNT_WITH_AUTH, OnAccountWithAuth);
    }

    public void Execute(Client client, IPacket packet)
    {
        if (_actions.TryGetValue(packet.Id, out var action))
        {
            action?.Invoke(client as GameClient, packet);
        }
    }

    private void OnVersion(GameClient client, IPacket packet)
    {
    }

    private static readonly int[] DefaultSpawn = { 94454, 126040, 0 };

    private async void OnLogin(GameClient client, IPacket packet)
    {
        var msg = packet.GetDataStruct<TS_CS_LOGIN>();

        var characters = await _characterService.GetCharactersByAccountNameAsync(client.ConnectionInfo.AccountName, true);
        var character = characters.FirstOrDefault(c => c.CharacterName == msg.Name);

        if (character == null)
        {
            _logger.Error("Enter game failed: character {name} not found for ({account}) {clientTag}", msg.Name,
                client.ConnectionInfo.AccountName, client.ClientTag);
            client.SendResult(packet.Id, (ushort)ResultCode.AccessDenied);
            return;
        }

        var position = character.Position ?? new[] { 0, 0, 0 };
        if (position.Length < 3 || (position[0] == 0 && position[1] == 0 && position[2] == 0))
        {
            position = DefaultSpawn;
        }

        var level = character.Lv > 0 ? character.Lv : 1;
        var statResult = _statService.Compute(character);
        var stats = statResult.Total;
        var hp = (int)stats.MaxHp;
        var mp = (int)stats.MaxMp;

        var info = client.ConnectionInfo;
        _statService.Seed(info, character);
        info.CharacterHandle = (uint)character.Id;
        info.CharacterName = character.CharacterName;
        info.CharacterHp = hp;
        info.CharacterMaxHp = hp;
        info.CharacterMp = mp;
        info.CharacterLevel = level;
        info.CharacterRace = character.Race;
        info.CharacterJob = (int)character.CurrentJob;
        info.CharacterJobLevel = character.Jlv;
        info.CharacterExp = character.Exp;
        info.CharacterJp = character.Jp;
        info.CharacterGold = character.Gold;
        info.CharacterChaos = character.Chaos;
        info.Layer = (byte)character.Layer;
        info.X = position[0];
        info.Y = position[1];
        info.Z = position[2];
        info.LearnedSkills.Clear();
        foreach (var skill in character.Skills ?? Array.Empty<CharacterSkillEntity>())
        {
            info.LearnedSkills[skill.SkillId] = skill.Level;
        }

        var result = new TS_SC_LOGIN_RESULT
        {
            Result = (ushort)ResultCode.Success,
            Handle = (uint)character.Id,
            X = position[0],
            Y = position[1],
            Z = position[2],
            Layer = (byte)character.Layer,
            FaceDirection = 0,
            RegionSize = WorldVisibility.RegionSize,
            Hp = hp,
            Mp = mp,
            MaxHp = hp,
            MaxMp = mp,
            Havoc = 0,
            MaxHavoc = 0,
            Sex = character.Sex,
            Race = character.Race,
            SkinColor = (uint)character.SkinColor,
            FaceId = (int)GameCharacterPackets.GetFaceId(character),
            HairId = (int)GameCharacterPackets.GetHairId(character),
            FaceTextureId = character.TextureId,
            Name = character.CharacterName,
            CellSize = 0,
            GuildId = 0
        };

        client.Connection.Send(new Packet<TS_SC_LOGIN_RESULT>((ushort)GamePackets.TM_SC_LOGIN_RESULT, result).Data);

        var enter = new TS_SC_ENTER_PLAYER
        {
            Type = 0,
            Handle = (uint)character.Id,
            X = result.X,
            Y = result.Y,
            Z = result.Z,
            Layer = (byte)character.Layer,
            ObjType = 0,
            Status = 0,
            FaceDirection = 0,
            Hp = hp,
            MaxHp = hp,
            Mp = mp,
            MaxMp = mp,
            Level = level,
            Race = (byte)character.Race,
            SkinColor = (uint)character.SkinColor,
            IsFirstEnter = 1,
            Energy = 0,
            Sex = (byte)character.Sex,
            FaceId = GameCharacterPackets.GetFaceId(character),
            FaceTextureId = (uint)character.TextureId,
            HairId = GameCharacterPackets.GetHairId(character),
            HairColorIndex = (uint)character.HairColorIndex,
            HairColorRGB = (uint)character.HairColorRgb,
            HideEquipFlag = (uint)character.HideEquipFlag,
            Name = character.CharacterName,
            JobId = (ushort)character.CurrentJob,
            RideHandle = 0,
            GuildId = 0
        };
        client.Connection.Send(new Packet<TS_SC_ENTER_PLAYER>((ushort)GamePackets.TM_SC_ENTER, enter).Data);

        var handle = (uint)character.Id;
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, stats, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, statResult.ByItem, StatInfoType.ByItem));
        foreach (var inventoryPacket in GameCharacterPackets.BuildInventory(character))
        {
            client.Connection.Send(inventoryPacket);
        }

        client.Connection.Send(GameCharacterPackets.BuildEquipSummon(character.SummonSlotItemIds));
        client.Connection.Send(GameCharacterPackets.BuildWearInfo(handle, character));
        client.Connection.Send(GameCharacterPackets.BuildHideEquipInfo(handle, character.HideEquipFlag));
        client.Connection.Send(GameCharacterPackets.BuildSkinInfo(handle, character.SkinColor));
        client.Connection.Send(GameCharacterPackets.BuildGoldUpdate(character.Gold, character.Chaos));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_chaos", character.Chaos));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_stamina", character.Stamina));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "tp", character.TalentPoint));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "chaos", character.Chaos));
        client.Connection.Send(GameCharacterPackets.BuildLevelUpdate(handle, character.Lv, character.Jlv));
        client.Connection.Send(GameCharacterPackets.BuildExpUpdate(handle, character.Exp, character.Jp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "job", (int)character.CurrentJob));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "job_level", character.Jlv));

        for (var i = 0; i < 3; i++)
        {
            client.Connection.Send(GameStatPackets.BuildProperty(handle, $"job_{i}", (int)character.PreviousJobs[i]));
            client.Connection.Send(GameStatPackets.BuildProperty(handle, $"jlv_{i}", character.JobLvs[i]));
        }

        if (client.ConnectionInfo.LearnedSkills.Count > 0)
        {
            var skills = client.ConnectionInfo.LearnedSkills.OrderBy(skill => skill.Key).ToArray();
            client.Connection.Send(GameCharacterPackets.BuildSkillList(handle, skills));
        }

        client.Connection.Send(GameCharacterPackets.BuildEmptyAddedSkillList(handle));
        client.Connection.Send(GameCharacterPackets.BuildBeltSlotInfo(character.BeltItemIds));
        _networkService.SkillCastService.Register(client);
        client.SendGameTime();
        client.SendTimeSync();
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "hp", hp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "mp", mp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_hp", (int)stats.MaxHp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_mp", (int)stats.MaxMp));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "stamina", character.Stamina));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "max_stamina", character.Stamina));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "permission", character.Permission));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "pk_count", character.PkCount));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "dk_count", character.DkCount));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "huntaholicpoint", character.HuntaholicPoint));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "huntaholic_ent", character.HuntaholicEnterCount));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "ethereal_stone", character.EtherealStoneDurability));
        client.Connection.Send(GameStatPackets.BuildProperty(handle, "immoral", decimal.ToInt64(character.ImmoralPoint)));
        client.Connection.Send(GameCharacterPackets.BuildStatusChange(handle));
        client.Connection.Send(GameStatPackets.BuildStringProperty(handle, "client_info", character.ClientInfo));

        _logger.Debug("{clientTag} entered game as {name} (lv {lv}) at ({x},{y},{z})", client.ClientTag,
            character.CharacterName, enter.Level, result.X, result.Y, result.Z);

        _npcSpawnService.Sync(client);
        _monsterSpawnService.Sync(client);
    }

    private void OnReport(GameClient client, IPacket packet)
    {
    }

    private async void OnCharacterList(GameClient client, IPacket packet)
    {
        var message = packet.GetDataStruct<TS_CS_CHARACTER_LIST>();
        try
        {
            await SendCharacterListForAccountAsync(client, message.Account);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not load the character list for {clientTag}", client.ClientTag);
        }
    }

    private async Task SendCharacterListForAccountAsync(GameClient client, string accountName)
    {
        var characters = await _characterService.GetCharactersByAccountNameAsync(accountName, true);
        var lobbyCharacters = new List<LobbyCharacterInfo>();

        client.ConnectionInfo.CharacterList.Clear();

        foreach (var character in characters)
        {
            var characterLobbyInfo = new LobbyCharacterInfo
            {
                Level = character.Lv,
                Job = (int)character.CurrentJob,
                JobLevel = character.Jlv,
                ExpPercentage = 0,
                HP = character.Hp,
                MP = character.Mp,
                Permission = character.Permission,
                IsBanned = 0,
                Name = character.CharacterName,
                SkinColor = (uint)character.SkinColor,
                Sex = character.Sex,
                Race = character.Race,
                ModelId = character.Models,
                HairColorIndex = character.HairColorIndex,
                HairColorRGB = (uint)character.HairColorRgb,
                HideEquipFlag = (uint)character.HideEquipFlag,
                TextureID = character.TextureId,
                CreateTime = character.CreatedOn.ToString("yyyy/MM/dd"),
                DeleteTime = character.DeletedOn?.ToString("yyyy/MM/dd") ?? "9999/12/01",
            };

            if (!character.Items.IsNullOrEmpty())
            {
                foreach (var item in character.Items.Where(i => i.WearInfo != ItemWearType.None))
                {
                    characterLobbyInfo.WearInfo[(int)item.WearInfo] = (int)item.ItemResourceId;
                    characterLobbyInfo.WearItemEnhanceInfo[(int)item.WearInfo] = (int)item.Enhance;
                    characterLobbyInfo.WearItemLevelInfo[(int)item.WearInfo] = (int)item.Level;
                    characterLobbyInfo.WearItemElementalType[(int)item.WearInfo] = (char)item.ElementalEffectType;
                }
            }

            lobbyCharacters.Add(characterLobbyInfo);

            client.ConnectionInfo.CharacterList.Add(character.CharacterName);
        }

        SendCharacterList(client, lobbyCharacters);
    }

    private void SendCharacterList(GameClient client, List<LobbyCharacterInfo> characterList)
    {
        var charCount = (ushort)characterList.Count;

        var packetStructLength = Marshal.SizeOf<TS_SC_CHARACTER_LIST>();
        var lobbyCharacterStructLength = Marshal.SizeOf<LobbyCharacterInfo>();
        var lobbyCharacterBufferLength = lobbyCharacterStructLength * characterList.Count;

        var data = new TS_SC_CHARACTER_LIST(0, 0, charCount);
        var packet = new Packet<TS_SC_CHARACTER_LIST>(2004, data, packetStructLength + lobbyCharacterBufferLength);

        var charInfoOffset = Marshal.SizeOf<Header>() + packetStructLength;

        foreach (var character in characterList)
        {
            Buffer.BlockCopy(character.StructToByte(), 0, packet.Data, charInfoOffset, lobbyCharacterStructLength);

            charInfoOffset += lobbyCharacterStructLength;
        }

        client.Connection.Send(packet.Data);

    }

    private async void OnCreateCharacter(GameClient client, IPacket packet)
    {
        var createMsg = packet.GetDataStruct<TS_CS_CREATE_CHARACTER>();
        var characterCount = _characterService.CharacterCount(client.ConnectionInfo.AccountId);

        if (characterCount >= 6)
        {
            _logger.Debug("Character create failed! Limit reached! for ({accountName}) {clientTag} !!!", client.ConnectionInfo.AccountName, client.ClientTag);

            client.SendResult(packet.Id, (ushort)ResultCode.LimitMax);

            return;
        }

        var selectedArmor = createMsg.Info.WearInfo[(int)ItemWearType.Armor];
        var startingStats = _statService.ComputeForNewCharacter(createMsg.Info.Race).Total;

        int defaultArmorId;
        int defaultWeaponId;

        switch ((Race)createMsg.Info.Race)
        {
            case Race.Deva:
                defaultArmorId = selectedArmor == 602 ? 220109 : 220100;
                defaultWeaponId = 106100;
                break;

            case Race.Gaia:
                defaultArmorId = selectedArmor == 602 ? 240109 : 240100;
                defaultWeaponId = 112100;
                break;

            case Race.Asura:
                defaultArmorId = selectedArmor == 602 ? 230109 : 230100;
                defaultWeaponId = 103100;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(createMsg.Info.Race));
        }

        var character = new CharacterEntity
        {
            AccountId = client.ConnectionInfo.AccountId,
            AccountName = client.ConnectionInfo.AccountName,
            CharacterName = createMsg.Info.Name.FormatName(),
            Slot = characterCount,
            Sex = createMsg.Info.Sex,
            Race = createMsg.Info.Race,
            Lv = 1,
            MaxReachedLv = 1,
            CurrentJob = CharacterDefaults.GetStarterJob(createMsg.Info.Race),
            JobDepth = JobDepth.Base,
            Jlv = 1,
            PreviousJobs = new Job[3],
            JobLvs = new int[3],
            Position = DefaultSpawn.ToArray(),
            Hp = (int)startingStats.MaxHp,
            Mp = (int)startingStats.MaxMp,
            Models = createMsg.Info.ModelId,
            HairColorIndex = createMsg.Info.HairColorIndex,
            HairColorRgb = unchecked((int)createMsg.Info.HairColorRGB),
            HideEquipFlag = unchecked((int)createMsg.Info.HideEquipFlag),
            TextureId = createMsg.Info.TextureID,
            SkinColor = (int)createMsg.Info.SkinColor,

            Items = new List<ItemEntity>
            {
                new() { Idx = 0, ItemResourceId = defaultArmorId, Level = 1, Amount = 1, Endurance = 50, WearInfo = ItemWearType.Armor, GenerateBySource = ItemGenerateSource.Basic },
                new() { Idx = 1, ItemResourceId = defaultWeaponId, Level = 1, Amount = 1, Endurance = 50, WearInfo = ItemWearType.Weapon, GenerateBySource = ItemGenerateSource.Basic },

                new() { Idx = 2, ItemResourceId = 490001, Level = 1, Amount = 1, Endurance = 50, WearInfo = ItemWearType.BagSlot, GenerateBySource = ItemGenerateSource.Basic}
            }
        };

        var createdEntity = await _characterService.CreateCharacterAsync(character);

        if (createdEntity == null)
        {
            _logger.Error("Character create failed! for ({accountName}) {clientTag} !!!", character.AccountName, client.ClientTag);

            client.SendResult(packet.Id, (ushort)ResultCode.DBError);
        }

        _logger.Debug("Character {characterName} successfully created for ({accountName}) {clientTag}", character.CharacterName, client.ConnectionInfo.AccountName, client.ClientTag);

        client.SendResult(packet.Id, (ushort)ResultCode.Success);
    }

    private void OnDeleteCharacter(GameClient client, IPacket packet)
    {
        if (client.ConnectionInfo.CharacterList.Count == 0)
        {
            client.SendDisconnectDesription(DisconnectType.AntiHack);

            client.Dispose();

            return;
        }

        var deleteMsg = packet.GetDataStruct<TS_CS_DELETE_CHARACTER>();

        _characterService.DeleteCharacterByNameAsync(deleteMsg.Name);

        _characterService.SaveChanges();
        client.SendResult(packet.Id, (ushort)ResultCode.Success);
    }

    private void OnCheckCharacterName(GameClient client, IPacket packet)
    {
        var nameMsg = packet.GetDataStruct<TS_CS_CHECK_CHARACTER_NAME>();

        if (string.IsNullOrEmpty(nameMsg.Name))
        {
            client.SendResult(packet.Id, (ushort)ResultCode.AccessDenied);

            _logger.Debug("Character Name Check Failed! Empty Name for ({accountName}) {clientTag} !!!", client.ConnectionInfo.AccountName, client.ClientTag);

            return;
        }

        if (!nameMsg.Name.IsValidName(4, 18))
        {
            client.SendResult(packet.Id, (ushort)ResultCode.InvalidText);

            _logger.Debug("Character Name Check Failed! Invalid Name ({name}) for ({accountName}) {clientTag} !!!", nameMsg.Name, client.ConnectionInfo.AccountName, client.ClientTag);

            return;
        }

        if (_bannedWordsRepository.ContainsBannedWord(nameMsg.Name))
        {
            client.SendResult(packet.Id, (ushort)ResultCode.InvalidText);

            _logger.Debug("Character Name Check Failed! Name ({name}) contains banned word! for ({accountName}) {clientTag} !!!", nameMsg.Name, client.ConnectionInfo.AccountName, client.ClientTag);

            return;
        }

        if (_characterService.CharacterExists(nameMsg.Name))
        {
            client.SendResult(packet.Id, (ushort)ResultCode.AlreadyExist);

            _logger.Debug("Character Name Check Failed! Name ({name}) already exists! for ({accountName}) {clientTag} !!!", nameMsg.Name, client.ConnectionInfo.AccountName, client.ClientTag);

            return;
        }

        _logger.Debug("Character Name Check Passed! for ({accountName}) {clientTag}", client.ConnectionInfo.AccountName, client.ClientTag);

        client.SendResult(packet.Id, (ushort)ResultCode.Success);
    }

    private void OnAccountWithAuth(GameClient client, IPacket packet)
    {
        _logger.Debug("{clientTag} verifying with Auth Server", client.ClientTag);

        var msg = packet.GetDataStruct<TM_CS_ACCOUNT_WITH_AUTH>();
        var loginInfo = new Packet<TS_GA_CLIENT_LOGIN>((ushort)AuthPackets.TS_GA_CLIENT_LOGIN,
            new TS_GA_CLIENT_LOGIN(msg.Account, msg.OneTimePassword));

        if (_networkService.AuthorizedGameClients.Count > _networkService.NetworkOptions.MaxConnections)
        {
            client.SendResult(packet.Id, (ushort)ResultCode.LimitMax);
        }

        if (string.IsNullOrEmpty(client.ConnectionInfo.AccountName))
        {
            if (_networkService.UnauthorizedGameClients.ContainsKey(msg.Account))
            {
                client.SendResult(packet.Id, (ushort)ResultCode.AccessDenied);
                return;
            }

            _networkService.UnauthorizedGameClients.Add(msg.Account, client);
        }

        _networkService.AuthClient.SendMessage(loginInfo);
    }
}
