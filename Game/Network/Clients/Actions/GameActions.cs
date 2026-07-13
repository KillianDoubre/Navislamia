using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
    private readonly NetworkService _networkService;

    private readonly Dictionary<ushort, Action<GameClient, IPacket>> _actions = new();

    public GameActions(NetworkService networkService)
    {
        _networkService = networkService;
        _bannedWordsRepository = networkService.BannedWordsRepository;
        _characterService = networkService.CharacterService;
        _statService = networkService.StatService;
        _npcSpawnService = networkService.NpcSpawnService;

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

    private static readonly int[] DefaultSpawn = { 83950, 115980, 0 };

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

        var stats = _statService.Compute(character.Race, character.Lv > 0 ? character.Lv : 1);
        var hp = character.Hp > 0 ? character.Hp : stats.MaxHp;
        var mp = character.Mp > 0 ? character.Mp : stats.MaxMp;

        client.ConnectionInfo.CharacterHandle = (uint)character.Id;
        client.ConnectionInfo.CharacterName = character.CharacterName;
        client.ConnectionInfo.Layer = (byte)character.Layer;
        client.ConnectionInfo.X = position[0];
        client.ConnectionInfo.Y = position[1];
        client.ConnectionInfo.Z = position[2];

        var result = new TS_SC_LOGIN_RESULT
        {
            Result = (ushort)ResultCode.Success,
            Handle = (uint)character.Id,
            X = position[0],
            Y = position[1],
            Z = position[2],
            Layer = (byte)character.Layer,
            FaceDirection = 0,
            RegionSize = 180,
            Hp = hp,
            Mp = mp,
            MaxHp = hp,
            MaxMp = mp,
            Havoc = 0,
            MaxHavoc = 0,
            Sex = character.Sex,
            Race = character.Race,
            SkinColor = (uint)character.SkinColor,
            FaceId = character.Models is { Length: > 0 } ? character.Models[0] : 0,
            HairId = character.Models is { Length: > 1 } ? character.Models[1] : 0,
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
            Level = character.Lv > 0 ? character.Lv : 1,
            Race = (byte)character.Race,
            SkinColor = (uint)character.SkinColor,
            IsFirstEnter = 1,
            Energy = 0,
            Sex = (byte)character.Sex,
            FaceId = character.Models is { Length: > 0 } ? (uint)character.Models[0] : 0,
            FaceTextureId = (uint)character.TextureId,
            HairId = character.Models is { Length: > 1 } ? (uint)character.Models[1] : 0,
            HairColorIndex = (uint)character.HairColorIndex,
            HairColorRGB = (uint)character.HairColorRgb,
            HideEquipFlag = 0,
            Name = character.CharacterName,
            JobId = (ushort)character.CurrentJob,
            RideHandle = 0,
            GuildId = 0
        };
        client.Connection.Send(new Packet<TS_SC_ENTER_PLAYER>((ushort)GamePackets.TM_SC_ENTER, enter).Data);
        client.Connection.Send(BuildWearInfo((uint)character.Id, character));

        client.Connection.Send(GameStatPackets.BuildStatInfo((uint)character.Id, stats));

        var level = character.Lv > 0 ? character.Lv : 1;
        client.Connection.Send(GameStatPackets.BuildProperty((uint)character.Id, "level", level));
        client.Connection.Send(GameStatPackets.BuildProperty((uint)character.Id, "hp", hp));
        client.Connection.Send(GameStatPackets.BuildProperty((uint)character.Id, "mp", mp));
        client.Connection.Send(GameStatPackets.BuildProperty((uint)character.Id, "max_hp", stats.MaxHp));
        client.Connection.Send(GameStatPackets.BuildProperty((uint)character.Id, "max_mp", stats.MaxMp));

        _logger.Debug("{clientTag} entered game as {name} (lv {lv}) at ({x},{y},{z})", client.ClientTag,
            character.CharacterName, enter.Level, result.X, result.Y, result.Z);

        _npcSpawnService.Sync(client);
    }

    private static byte[] BuildWearInfo(uint handle, CharacterEntity character)
    {
        const int slots = 24;
        var total = 7 + 4 + slots * 4 * 3 + slots + slots * 4;
        var packet = new byte[total];
        var s = packet.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(0, 4), (uint)total);
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(4, 2), (ushort)GamePackets.TM_SC_WEAR_INFO);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(7, 4), handle);

        var codeBase = 11;
        var enhanceBase = codeBase + slots * 4;
        var levelBase = enhanceBase + slots * 4;
        var elemBase = levelBase + slots * 4;

        if (character.Items != null)
        {
            foreach (var item in character.Items)
            {
                var slot = (int)item.WearInfo;
                if (item.WearInfo == ItemWearType.None || slot < 0 || slot >= slots)
                {
                    continue;
                }
                BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(codeBase + slot * 4, 4), (uint)item.ItemResourceId);
                BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(enhanceBase + slot * 4, 4), (uint)item.Enhance);
                BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(levelBase + slot * 4, 4), (uint)item.Level);
                s[elemBase + slot] = (byte)item.ElementalEffectType;
            }
        }

        if (character.Models is { Length: > 0 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                s.Slice(codeBase + (int)ItemWearType.Face * 4, 4), (uint)character.Models[0]);
        }
        if (character.Models is { Length: > 1 })
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                s.Slice(codeBase + (int)ItemWearType.Hair * 4, 4), (uint)character.Models[1]);
        }

        InjectBaseModelIfEmpty(packet, codeBase, ItemWearType.Armor, character.Models, 2);
        InjectBaseModelIfEmpty(packet, codeBase, ItemWearType.Glove, character.Models, 3);
        InjectBaseModelIfEmpty(packet, codeBase, ItemWearType.Boots, character.Models, 4);

        byte checksum = 0;
        for (var i = 0; i < 6; i++) checksum += packet[i];
        packet[6] = checksum;

        return packet;
    }

    private static void InjectBaseModelIfEmpty(byte[] packet, int codeBase, ItemWearType slot, int[] models, int modelIndex)
    {
        if (models == null || models.Length <= modelIndex)
        {
            return;
        }

        var offset = codeBase + (int)slot * 4;
        if (BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(offset, 4)) != 0)
        {
            return;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset, 4), (uint)models[modelIndex]);
    }

    private void OnReport(GameClient client, IPacket packet)
    {
    }

    private void OnCharacterList(GameClient client, IPacket packet)
    {
        var message = packet.GetDataStruct<TS_CS_CHARACTER_LIST>();
        SendCharacterListForAccount(client, message.Account);
    }

    public async void SendCharacterListForAccount(GameClient client, string accountName)
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

        if (_characterService.CharacterCount(client.ConnectionInfo.AccountId) >= 6)
        {
            _logger.Debug("Character create failed! Limit reached! for ({accountName}) {clientTag} !!!", client.ConnectionInfo.AccountName, client.ClientTag);

            client.SendResult(packet.Id, (ushort)ResultCode.LimitMax);

            return;
        }

        var selectedArmor = createMsg.Info.WearInfo[(int)ItemWearType.Armor];

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
            Sex = createMsg.Info.Sex,
            Race = createMsg.Info.Race,
            Models = createMsg.Info.ModelId,
            HairColorIndex = createMsg.Info.HairColorIndex,
            TextureId = createMsg.Info.TextureID,
            SkinColor = (int)createMsg.Info.SkinColor,

            Items = new List<ItemEntity>
            {
                new() { ItemResourceId = defaultArmorId, Level = 1, Amount = 1, Endurance = 50, WearInfo = ItemWearType.Armor, GenerateBySource = ItemGenerateSource.Basic },
                new() { ItemResourceId = defaultWeaponId, Level = 1, Amount = 1, Endurance = 50, WearInfo = ItemWearType.Weapon, GenerateBySource = ItemGenerateSource.Basic },

                new() { ItemResourceId = 490001, Level = 1, Amount = 1, Endurance = 50, WearInfo = ItemWearType.BagSlot, GenerateBySource = ItemGenerateSource.Basic}
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
