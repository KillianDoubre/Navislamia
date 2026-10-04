using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;

namespace Navislamia.Game.Services;

/// <summary>
/// The one-off fill that comes with <c>Version0021_EtherealDurabilityBackfill</c>: before the wear existed nothing set an
/// item's ethereal durability (<c>StructItem::AllocItem</c> starts it at the maximum), so every stored 0 on an item whose
/// resource has a maximum is an item never initialised, not an exhausted one. It must run only when that migration is
/// applied: afterwards a 0 is a real exhaustion.
/// </summary>
public static class EtherealDurabilityBackfill
{
    public const string Migration = "20261004132139_Version0021_EtherealDurabilityBackfill";

    public static async Task<int> RunAsync(TelecasterContext telecaster, IItemMatchCatalog catalog)
    {
        var items = await telecaster.Items.Where(item => item.EtherealDurability == 0).ToListAsync();
        var filled = 0;
        foreach (var item in items)
        {
            if (!catalog.TryGetFields(item.ItemResourceId, out var fields) || fields.Mix is not { MaxEtherealDurability: > 0 } mix)
            {
                continue;
            }

            item.EtherealDurability = mix.MaxEtherealDurability;
            filled++;
        }

        if (filled > 0)
        {
            await telecaster.SaveChangesAsync();
        }

        return filled;
    }
}
