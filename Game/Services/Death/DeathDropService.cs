using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Navislamia.Configuration.Options;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Death;

public interface IDeathDropService
{
    /// <summary>A player killed by a monster may lose one worn item and one bag item on a PK server.</summary>
    Task DropOnDeathAsync(GameClient client);
}

/// <summary>
/// The item part of <c>StructPlayer::procDecreaseEXPAndDropItem</c> (2012-11 <c>0x1400e35f0</c>,
/// docs/packet-specs/socle-mort-joueur.md §4). Only a PK server drops anything (<c>GameRule::bIsPKServer</c>; on
/// any other server the chance is zero): two separate rolls of 3 % — <c>XFastRandom() % 999 + 1 &lt; 30</c> — one for
/// a worn item, one for a bag item, and none at all while the bag holds the protection item 920005. The worn item
/// is taken off and dropped; the bag item is dropped whole, by the same path as <c>TM_CS_DROP_ITEM</c>.
/// </summary>
public sealed class DeathDropService : IDeathDropService
{
    /// <summary>The item whose presence in the bag spares every drop (<c>StructInventory::Find(920005)</c>).</summary>
    public const int ProtectionItem = 920005;

    /// <summary>The chance per mille, <c>0.03 × 1000</c>.</summary>
    public const int ChancePerMille = 30;

    /// <summary>The wear slots the worn roll scans, from a random start upward (<c>0x1c</c>).</summary>
    public const int WearSlots = 28;

    private readonly ILogger _logger = Log.ForContext<DeathDropService>();
    private readonly ICharacterService _characters;
    private readonly IEquipmentService _equipment;
    private readonly IGroundItemService _ground;
    private readonly IOptionsMonitor<GameRuleOptions> _rules;
    private readonly ICombatRandom _random;

    public DeathDropService(ICharacterService characters, IEquipmentService equipment, IGroundItemService ground,
        IOptionsMonitor<GameRuleOptions> rules, ICombatRandom random = null)
    {
        _characters = characters;
        _equipment = equipment;
        _ground = ground;
        _rules = rules;
        _random = random ?? CombatRandom.Shared;
    }

    /// <summary>The roll of the reference: a value in <c>[1, 999]</c> below the chance per mille.</summary>
    public static bool Rolls(int roll) => roll < ChancePerMille;

    public async Task DropOnDeathAsync(GameClient client)
    {
        if (_rules?.CurrentValue?.PkServer != true)
        {
            return;
        }

        var info = client.ConnectionInfo;
        try
        {
            var items = await _characters.GetCarriedItemsAsync(info.CharacterName);
            if (items.Any(item => item.ItemResourceId == ProtectionItem))
            {
                return;
            }

            if (Rolls(_random.Next(999) + 1))
            {
                var start = _random.Next(WearSlots);
                var worn = items.Where(item => ItemWearRules.IsWornByPlayer(item) && (int)item.WearInfo >= start
                                               && (int)item.WearInfo < WearSlots)
                    .OrderBy(item => (int)item.WearInfo).FirstOrDefault();
                if (worn is not null)
                {
                    await _equipment.UnequipAsync(client,
                        new GameActionPackets.PutoffItemRequest((sbyte)worn.WearInfo, 0));
                    await _ground.DropFromInventoryAsync(client, (uint)worn.Id, (int)Math.Min(worn.Amount, int.MaxValue));
                    _logger.Debug("{clientTag} dropped worn item {itemId} on death", client.ClientTag, worn.ItemResourceId);
                }
            }

            if (Rolls(_random.Next(999) + 1))
            {
                var bag = items.Where(item => item.WearInfo == ItemWearType.None).ToArray();
                if (bag.Length > 0)
                {
                    var picked = bag[_random.Next(bag.Length)];
                    await _ground.DropFromInventoryAsync(client, (uint)picked.Id,
                        (int)Math.Min(picked.Amount, int.MaxValue));
                    _logger.Debug("{clientTag} dropped bag item {itemId} on death", client.ClientTag,
                        picked.ItemResourceId);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not drop items on the death of {clientTag}", client.ClientTag);
        }
    }
}
