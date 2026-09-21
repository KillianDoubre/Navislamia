using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.Services;

/// <summary>
/// Verdict of the gated bind of <c>TM_CS_BIND_SKILLCARD</c> (284). The three refusals follow
/// NGemity's <c>WorldSession::onBindSkillCard</c> (Chihiro/src/Network/GameNetwork/WorldSession.cpp:1683-1695)
/// in the order it judges them; <see cref="NotFound"/> covers the handle that resolves to none of the
/// character's items.
/// </summary>
public enum SkillCardBindOutcome
{
    Success,
    NotFound,
    NotActable,
    AccessDenied
}

public readonly record struct SkillCardBindResult(
    SkillCardBindOutcome Outcome,
    CharacterEntity Character,
    ItemEntity Item);
