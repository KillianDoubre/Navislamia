using System;
using System.Linq;
using System.Threading.Tasks;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Interfaces;
using Serilog;

namespace Navislamia.Game.Services.Props;

/// <summary>
/// Using a world prop the official way (docs/packet-specs/socle-props.md): the conditions of
/// <c>StructFieldProp::IsUsable</c> that need the character's items and quests, the cast that holds the prop, and
/// <c>UseProp</c> — one use, the drops into the bag, the script.
/// </summary>
public interface IFieldPropUse
{
    /// <summary>Whether a prop has conditions the cast must await (items, quests, worn items, nearby monsters).</summary>
    bool NeedsConditions(FieldPropTemplate template);

    /// <summary>The awaited half of <c>IsUsable</c>.</summary>
    Task<bool> CheckConditionsAsync(GameClient client, FieldPropTemplate template);

    bool IsPresent(long instanceId);

    bool TryBeginCast(long instanceId, GameClient caster, uint until);

    void EndCast(long instanceId, GameClient caster);

    /// <summary>
    /// <c>UseProp</c>: false when the prop is gone or used up. Otherwise the use is taken, a used-up prop leaves the
    /// view, and the drops and the script follow.
    /// </summary>
    bool Use(GameClient client, long instanceId, FieldPropTemplate template);

    /// <summary>Whether the prop does something this server carries out besides its action: drops or a script.</summary>
    bool HasEffects(FieldPropTemplate template);
}

public sealed class FieldPropUse : IFieldPropUse
{
    /// <summary><c>FieldPropBase::CHECK_TYPE_*</c>.</summary>
    public const int ConditionItem = 1, ConditionQuest = 2, ConditionSkill = 3, ConditionWorn = 4,
        ConditionNearMonster = 11, ConditionNoNearMonster = 12;

    /// <summary><c>GameRule::DEFAULT_UNIT_SIZE</c>: a condition's range is in units of 12.</summary>
    private const float UnitSize = 12f;

    /// <summary><c>drop_info.ratio</c> is out of 100 000 000 (<c>XRandom(1, 100000000)</c>).</summary>
    public const int DropScale = 100_000_000;

    private readonly ILogger _logger = Log.ForContext<FieldPropUse>();
    private readonly IFieldPropStates _states;
    private readonly IFieldPropCatalog _catalog;
    private readonly IFieldPropService _fieldProps;
    private readonly ICharacterService _characters;
    private readonly IQuestService _quests;
    private readonly MonsterWorldState _monsters;
    private readonly INpcScriptService _scripts;
    private readonly Random _random;

    public FieldPropUse(IFieldPropStates states, IFieldPropCatalog catalog, ICharacterService characters,
        IFieldPropService fieldProps = null, IQuestService quests = null, MonsterWorldState monsters = null,
        INpcScriptService scripts = null, Random random = null)
    {
        _states = states;
        _catalog = catalog;
        _characters = characters;
        _fieldProps = fieldProps;
        _quests = quests;
        _monsters = monsters;
        _scripts = scripts;
        _random = random ?? Random.Shared;
    }

    public bool NeedsConditions(FieldPropTemplate template) => template.Activations?.Any(activation =>
        activation.Condition is ConditionItem or ConditionQuest or ConditionWorn or ConditionNearMonster
            or ConditionNoNearMonster) == true;

    public async Task<bool> CheckConditionsAsync(GameClient client, FieldPropTemplate template)
    {
        var info = client.ConnectionInfo;
        foreach (var activation in template.Activations ?? Array.Empty<PropActivation>())
        {
            switch (activation.Condition)
            {
                case ConditionItem:
                {
                    // FindItem(code)->GetCount() >= value2: the first stack of the item.
                    var stack = (await _characters.GetCarriedItemsAsync(info.CharacterName))
                        .Where(item => item.ItemResourceId == activation.Value1 && item.Amount > 0)
                        .OrderBy(item => item.Idx).FirstOrDefault();
                    if (stack is null || stack.Amount < activation.Value2) return false;
                    break;
                }
                case ConditionQuest:
                    if (_quests is null || await _quests.GetQuestProgressAsync(client, activation.Value1) != activation.Value2)
                        return false;
                    break;
                case ConditionWorn:
                {
                    var worn = (await _characters.GetCarriedItemsAsync(info.CharacterName)).Any(item =>
                        item.ItemResourceId == activation.Value1 && ItemWearRules.IsWornByPlayer(item));
                    // value2 non-zero: it must be worn; zero: it must not.
                    if (worn != (activation.Value2 != 0)) return false;
                    break;
                }
                case ConditionNearMonster:
                case ConditionNoNearMonster:
                    if (NearMonster(info, activation.Value1, activation.Value2 * UnitSize) != (activation.Condition == ConditionNearMonster))
                        return false;
                    break;
            }
        }

        return true;
    }

    /// <summary>A living monster (of <paramref name="monsterId"/>, any when 0) within range on the player's layer.</summary>
    private bool NearMonster(ConnectionInfo info, int monsterId, float range)
    {
        if (_monsters is null) return false;
        var (x, y) = info.PositionAt(ServerClock.Now);
        return _monsters.WithinRange(x, y, range).Any(monster => monster.Layer == info.Layer
            && (monsterId == 0 || monster.MonsterId == monsterId) && _monsters.IsAlive(monster.InstanceId)
            && CombatRange.Distance(x, y, _monsters.GetPosition(monster.InstanceId).X, _monsters.GetPosition(monster.InstanceId).Y) <= range);
    }

    public bool IsPresent(long instanceId) => _states.IsPresent(instanceId);

    public bool TryBeginCast(long instanceId, GameClient caster, uint until) => _states.TryBeginCast(instanceId, caster, until);

    public void EndCast(long instanceId, GameClient caster) => _states.EndCast(instanceId, caster);

    public bool HasEffects(FieldPropTemplate template) =>
        template.RulesOrNone.Drops.Length > 0 || !string.IsNullOrWhiteSpace(template.RulesOrNone.LuaScript);

    public bool Use(GameClient client, long instanceId, FieldPropTemplate template)
    {
        if (!_states.TryUse(instanceId, out var removed))
        {
            return false;
        }

        if (removed && _catalog.TryGetInstance(instanceId, out var instance))
        {
            _fieldProps?.Refresh(instance.X, instance.Y);
        }

        var rules = template.RulesOrNone;
        if (rules.Drops.Length > 0)
        {
            _ = GiveDropsAsync(client, rules.Drops);
        }

        if (!string.IsNullOrWhiteSpace(rules.LuaScript))
        {
            _ = RunScriptAsync(client, rules.LuaScript);
        }

        return true;
    }

    /// <summary>
    /// The two drop slots: each rolls its ratio, then a count between its bounds goes into the bag
    /// (<c>PushItem</c>), announced by the result of 204 like a pick-up.
    /// </summary>
    private async Task GiveDropsAsync(GameClient client, PropDrop[] drops)
    {
        var info = client.ConnectionInfo;
        foreach (var drop in drops)
        {
            if (drop.ItemId <= 0 || _random.Next(1, DropScale + 1) > drop.Ratio)
            {
                continue;
            }

            var count = _random.Next(Math.Min(drop.CountMin, drop.CountMax), Math.Max(drop.CountMin, drop.CountMax) + 1);
            if (count <= 0)
            {
                continue;
            }

            try
            {
                var item = await _characters.AddItemAsync(info.CharacterName, drop.ItemId, count);
                if (item is null)
                {
                    continue;
                }

                foreach (var frame in GameCharacterPackets.BuildInventory(new[] { item }))
                {
                    client.Connection.Send(frame);
                }

                client.SendResult((ushort)GamePackets.TM_CS_TAKE_ITEM, (ushort)ResultCode.Success, unchecked((int)item.Id));
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Could not give the prop drop {item} to {clientTag}", drop.ItemId, client.ClientTag);
            }
        }
    }

    private async Task RunScriptAsync(GameClient client, string script)
    {
        if (_scripts is null)
        {
            return;
        }

        try
        {
            await _scripts.RunPropScriptAsync(client, script);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Prop script {script} failed for {clientTag}", script, client.ClientTag);
        }
    }
}
