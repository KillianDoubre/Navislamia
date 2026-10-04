using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets.Enums;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services.Auction;
using Navislamia.Game.Services.Interfaces;

namespace Navislamia.Game.Services;

/// <summary>
/// The "item obtained" line of the official server (<c>MixManager</c>, <c>HuntaholicManager</c>): <c>@254</c> with the
/// count for a joinable item, <c>@253</c> with the name alone otherwise, on <c>CHAT_ITEM</c> from <c>@SYSTEM</c>.
/// </summary>
public static class ItemObtainedNotice
{
    public static string Message(int nameId, long count, bool joinable) => joinable
        ? $"@254\v#@item_name@#\v@{nameId}\v#@item_num@#\v{count}"
        : $"@253\v#@item_name@#\v@{nameId}";

    /// <summary>
    /// <c>StructItem::IsJoinable</c>: the item's <c>FLAG_JOIN</c> (bit 6 of <c>item_use_flag</c>) and no bound creature
    /// (<c>ITEM_FLAG_SUMMON</c>). The skill card's binding is not modelled.
    /// </summary>
    public static bool IsJoinable(int useFlags, ItemFlag flag) => ItemUseRules.HasFlag(useFlags, ItemUseFlag.Join)
        && (flag == ItemFlag.None || (unchecked((uint)flag) & GroundItemDropRules.SummonFlagMask) == 0);

    public static void Send(GameClient client, IAuctionCatalog names, IItemUseCatalog flags, ItemEntity item, long count)
    {
        if (item is null || count <= 0 || names?.TryGetItem((int)item.ItemResourceId, out var row) != true || row.NameId <= 0)
        {
            return;
        }

        var joinable = IsJoinable(flags?.UseFlags((int)item.ItemResourceId) ?? 0, item.Flag);
        client.Connection.Send(GameChatPackets.BuildChat("@SYSTEM", (byte)ChatType.Item, Message(row.NameId, count, joinable)));
    }
}
