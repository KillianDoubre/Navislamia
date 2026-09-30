using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services;

/// <summary>
/// What a skill card judgement decided. The reference answers every refusal with its own
/// <see cref="ResultCode"/> and success with nothing but the <c>TM_SC_SKILLCARD_INFO</c> echo, so the
/// outcome has to survive the call to the database gate.
/// </summary>
public enum SkillCardBindOutcome
{
    Success,
    NotFound,
    NotActable,
    AccessDenied
}

/// <summary>
/// The verdict of one skill card judgement: the outcome, the code the client is answered with and the
/// value the reference echoes back (the item or the target handle, depending on the refusal — NGemity
/// <c>WorldSession.cpp:1706-1717</c>).
/// </summary>
public readonly record struct SkillCardBindResult(SkillCardBindOutcome Outcome, ushort ResponseCode, int Value)
{
    public static SkillCardBindResult Success { get; } =
        new(SkillCardBindOutcome.Success, (ushort)ResultCode.Success, 0);

    public bool Succeeded => Outcome == SkillCardBindOutcome.Success;

    public static SkillCardBindResult Refuse(SkillCardBindOutcome outcome, ResultCode code, int value)
    {
        return new SkillCardBindResult(outcome, (ushort)code, value);
    }
}
