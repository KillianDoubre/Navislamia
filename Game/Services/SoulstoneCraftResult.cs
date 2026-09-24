using System.Collections.Generic;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>
/// One chassis a <c>TM_CS_SOULSTONE_CRAFT</c> (260) request fills: the slot of the frame, the handle of the
/// stone the client named and the code written into the chassis. The code — and not the handle — is what an
/// item carries: NGemity writes <c>GetItemInstance().GetCode()</c> (<c>WorldSession.cpp:1572</c>) and reads
/// the chassis back through a table indexed by code (<c>:1536</c>), which is the only thing that still
/// resolves once the stone itself has been erased (<c>:1573</c>).
/// </summary>
public readonly record struct SoulstoneSlotAssignment(int Slot, uint StoneHandle, long StoneCode);

public enum SoulstoneCraftOutcome
{
    Success,

    /// <summary>The crafted item vanished between the reading of the request and the write.</summary>
    ItemNotFound,

    /// <summary>One of the named stones vanished between the reading of the request and the write.</summary>
    StoneNotFound,

    NotEnoughMoney
}

/// <summary>
/// The outcome of <see cref="ICharacterService.SocketSoulstonesAsync"/>: the outcome, the crafted item as it
/// now stands, the handle that could not be resolved (zero when none), the character's gold and chaos after
/// the write, and the units left on every consumed stone (zero when the stack ran out and was deleted).
/// </summary>
public readonly record struct SoulstoneCraftResult(
    SoulstoneCraftOutcome Outcome,
    ItemEntity Item,
    uint MissingHandle,
    long Gold,
    int Chaos,
    IReadOnlyList<(uint Handle, long Remaining)> Consumed);
