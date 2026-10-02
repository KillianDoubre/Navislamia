using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets.Game;

namespace Navislamia.Game.Services.Casting;

/// <summary>
/// A cast between its <c>ST_Casting</c> and its fire: the effect lands at <see cref="FireTick"/>, which a hit
/// can push back, and the cast can be cancelled until then (docs/packet-specs/socle-lancer-competences.md §5).
/// Guarded by <c>ConnectionInfo.CastLock</c>.
/// </summary>
public sealed class PendingCast
{
    public PendingCast(GameActionPackets.SkillRequest request, CastableBuffFields fields, byte skillLevel,
        long targetInstanceId, uint startTick, uint fireTick)
    {
        Request = request;
        Fields = fields;
        SkillLevel = skillLevel;
        TargetInstanceId = targetInstanceId;
        StartTick = startTick;
        FireTick = fireTick;
    }

    public GameActionPackets.SkillRequest Request { get; }
    public CastableBuffFields Fields { get; }
    public byte SkillLevel { get; }
    public long TargetInstanceId { get; }
    public uint StartTick { get; }
    public uint FireTick { get; set; }
    public Navislamia.Game.Network.Clients.GameClient PlayerTarget { get; init; }
}
