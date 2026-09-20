using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// What the character service made of one socketing request.
/// <para>
/// <see cref="Code"/> is the verdict to answer with. <see cref="Target"/> is the socketed equipment
/// (the item sheet carrying its four sockets is built from it) and <see cref="Card"/> the soul stone,
/// both <c>null</c> when the database gate refused before resolving them.
/// <see cref="Remaining"/> is what is left on the card's stack, <c>0</c> when the last unit went.
/// </para>
/// </summary>
public readonly record struct CardSocketResult(ResultCode Code, ItemEntity Target, ItemEntity Card,
    long Remaining);
