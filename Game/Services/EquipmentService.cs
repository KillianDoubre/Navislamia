using System;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Serilog;

namespace Navislamia.Game.Services;

public class EquipmentService : IEquipmentService
{
    private const ushort EquipRequestId = (ushort)GamePackets.TM_CS_PUTON_ITEM;
    private const ushort UnequipRequestId = (ushort)GamePackets.TM_CS_PUTOFF_ITEM;

    private readonly ILogger _logger = Log.ForContext<EquipmentService>();
    private readonly ICharacterService _characterService;
    private readonly IStatService _statService;

    public EquipmentService(ICharacterService characterService, IStatService statService)
    {
        _characterService = characterService;
        _statService = statService;
    }

    public async Task EquipAsync(GameClient client, GameActionPackets.PutonItemRequest request)
    {
        var info = client.ConnectionInfo;
        if (request.TargetHandle != 0 && request.TargetHandle != info.CharacterHandle)
        {
            client.SendResult(EquipRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        if (!IsWearableSlot(request.Position))
        {
            client.SendResult(EquipRequestId, (ushort)ResultCode.InvalidArgument, 0);
            return;
        }

        try
        {
            var result = await _characterService.EquipItemAsync(info.CharacterName, request.ItemHandle,
                (ItemWearType)request.Position);

            switch (result.Outcome)
            {
                case EquipItemOutcome.NotFound:
                    client.SendResult(EquipRequestId, (ushort)ResultCode.AccessDenied, 0);
                    return;
                case EquipItemOutcome.AlreadyWorn:
                    client.SendResult(EquipRequestId, (ushort)ResultCode.NotActable, 0);
                    return;
            }

            var handle = info.CharacterHandle;
            if (result.Displaced is not null)
            {
                SendItemWear(client, handle, result.Displaced);
            }

            SendItemWear(client, handle, result.Equipped);
            SendStatInfo(client, info, handle, result.Character);
            client.SendResult(EquipRequestId, (ushort)ResultCode.Success, 0);
            client.Connection.Send(GameCharacterPackets.BuildWearInfo(handle, result.Character));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not equip item {itemHandle} at slot {position} for {clientTag}",
                request.ItemHandle, request.Position, client.ClientTag);
            client.SendResult(EquipRequestId, (ushort)ResultCode.DBError, 0);
        }
    }

    public async Task UnequipAsync(GameClient client, GameActionPackets.PutoffItemRequest request)
    {
        var info = client.ConnectionInfo;
        if (request.TargetHandle != 0 && request.TargetHandle != info.CharacterHandle)
        {
            client.SendResult(UnequipRequestId, (ushort)ResultCode.NotExist, 0);
            return;
        }

        if (!IsWearableSlot(request.Position))
        {
            client.SendResult(UnequipRequestId, (ushort)ResultCode.InvalidArgument, 0);
            return;
        }

        try
        {
            var item = await _characterService.UnequipItemAsync(info.CharacterName, (ItemWearType)request.Position);
            if (item is null)
            {
                client.SendResult(UnequipRequestId, (ushort)ResultCode.NotExist, 0);
                return;
            }

            var handle = info.CharacterHandle;
            SendItemWear(client, handle, item);
            SendStatInfo(client, info, handle, item.Character);
            client.SendResult(UnequipRequestId, (ushort)ResultCode.Success, 0);
            client.Connection.Send(GameCharacterPackets.BuildWearInfo(handle, item.Character));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not unequip slot {position} for {clientTag}", request.Position,
                client.ClientTag);
            client.SendResult(UnequipRequestId, (ushort)ResultCode.DBError, 0);
        }
    }

    private static bool IsWearableSlot(sbyte position)
    {
        return position >= 0 && position < GameCharacterPackets.WearSlots;
    }

    private void SendStatInfo(GameClient client, ConnectionInfo info, uint handle, CharacterEntity character)
    {
        _statService.Seed(info, character);
        var result = _statService.Compute(character);
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.Total, StatInfoType.Total));
        client.Connection.Send(GameStatPackets.BuildStatInfo(handle, result.ByItem, StatInfoType.ByItem));
    }

    private static void SendItemWear(GameClient client, uint targetHandle, ItemEntity item)
    {
        client.Connection.Send(GameCharacterPackets.BuildItemWearInfo((uint)item.Id, (short)item.WearInfo, targetHandle,
            (int)item.Enhance, (byte)item.ElementalEffectType));
    }
}
