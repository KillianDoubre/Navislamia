using System.Collections.Generic;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services;

/// <summary>
/// Which handles a crafting request actually names. The five request frames of the socle carry fixed
/// handle arrays whose empty slots are written as zero — zero is a sentinel, not an item: the reference
/// server refuses a material slot only when its handle is not zero
/// (NGemity <c>WorldSession.cpp:1521</c> <c>pRecvPct-&gt;soulstone_handle[i] != 0</c>) and tests the main
/// slot the same way (<c>:1451</c> <c>main_item.handle != 0</c>). Resolving a zero handle would report
/// <c>NotExist</c> for a slot the client left deliberately empty.
/// See docs/packet-specs/socle-artisanat-objets.md §3.1, §3.3, §3.4, §6.1 and §9.2.
/// </summary>
public static class CraftingSocleRules
{
    /// <summary>The target slot first, then the material slots in declaration order.</summary>
    public static uint[] ReferencedHandles(in GameActionPackets.MixRequest request)
    {
        var handles = new List<uint>(1 + (request.SubItems?.Length ?? 0));
        AddIfSet(handles, request.MainItemHandle);

        if (request.SubItems is not null)
        {
            foreach (var subItem in request.SubItems)
            {
                AddIfSet(handles, subItem.Handle);
            }
        }

        return handles.ToArray();
    }

    /// <summary>The item being socketed first, then its four soul stone slots.</summary>
    public static uint[] ReferencedHandles(in GameActionPackets.SoulstoneCraftRequest request)
    {
        var handles = new List<uint>(1 + (request.SoulstoneHandles?.Length ?? 0));
        AddIfSet(handles, request.CraftItemHandle);

        if (request.SoulstoneHandles is not null)
        {
            foreach (var handle in request.SoulstoneHandles)
            {
                AddIfSet(handles, handle);
            }
        }

        return handles.ToArray();
    }

    /// <summary>
    /// The six handles of the frame, in order. What they designate is not established (spec §7 NON
    /// ÉTABLI 2): they are only resolved, never interpreted.
    /// </summary>
    public static uint[] ReferencedHandles(in GameActionPackets.RepairSoulstoneRequest request)
    {
        var handles = new List<uint>(request.ItemHandles?.Length ?? 0);

        if (request.ItemHandles is not null)
        {
            foreach (var handle in request.ItemHandles)
            {
                AddIfSet(handles, handle);
            }
        }

        return handles.ToArray();
    }

    /// <summary>The single handle of <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY</c>.</summary>
    public static uint[] ReferencedHandles(in GameActionPackets.TransmitEtherealDurabilityRequest request)
    {
        var handles = new List<uint>(1);
        AddIfSet(handles, request.Handle);
        return handles.ToArray();
    }

    /// <summary>
    /// Whether the <c>rate</c> of <c>TM_CS_TRANSMIT_ETHEREAL_DURABILITY_TO_EQUIPMENT</c> (264) is one the
    /// Epic 7.3 client can produce. Both emitters of <c>SFrame.exe</c> write a share of the restitution
    /// asked for: <c>1.0f</c> when the Ethereal Stone charge covers the whole request (<c>0x5EAE8A</c>
    /// <c>fld1</c>) and <c>part / 100.0</c> — strictly positive and strictly below 1, since that path is
    /// only reached when the request exceeds the charge (<c>0x5EAE57</c> <c>jg</c>) — when it covers it in
    /// part (<c>0x5EB570</c> <c>fdiv</c>). The admissible domain is therefore (0,1].
    ///
    /// The conjunction rejects <c>NaN</c> (<c>NaN &gt; 0f</c> is false) and both infinities
    /// (<c>±∞ &lt;= 1f</c> is false): a <c>NaN</c> that crossed this rule would enter every later item
    /// computation silently. The reader stays permissive and keeps the float as it is on the wire — the
    /// bound lives here, not in <c>GameActionPackets</c>.
    /// See docs/packet-specs/264-transmit-ethereal-durability-to-equipment.md §2.3 and §5.5.
    /// </summary>
    public static bool IsRestorableRate(float rate) => rate > 0f && rate <= 1f;

    private static void AddIfSet(ICollection<uint> handles, uint handle)
    {
        if (handle != 0)
        {
            handles.Add(handle);
        }
    }
}
