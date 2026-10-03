using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;

namespace Navislamia.Game.Services.Huntaholic;

/// <summary>
/// What the world tells HuntaHolic, and what HuntaHolic answers, without depending on it: combat, warp, casting
/// and resurrection are all things <see cref="HuntaholicService"/> itself needs, so they reach it through this
/// mediator (the <c>CreatureEvents</c> pattern) or the dependency graph would close a cycle that only fails at startup.
/// </summary>
public interface IHuntaholicEvents
{
    /// <summary>A monster died; <paramref name="topDealer"/> is its max-damage dealer (<c>GetMaxDamageDealer</c>).</summary>
    void MonsterKilled(long instanceId, GameClient topDealer);

    /// <summary><c>StructPlayer::ProcessWarp</c>: the player is about to leave for (x, y).</summary>
    void BeforeWarp(GameClient client, float x, float y);

    /// <summary>The 513 inside HuntaHolic; true when HuntaHolic resurrected the player itself.</summary>
    bool TryResurrect(GameClient client);

    /// <summary><c>huntaholic_lobby_menu()</c> on a prop: the lobby window.</summary>
    void ShowLobbyWindow(GameClient client, uint contactHandle);

    /// <summary><c>StructSkill</c>'s checks of 64818/64827 before the cast and again when it fires.</summary>
    ResultCode CheckInstanceSkill(GameClient client, int skillId);

    ResultCode FireInstanceSkill(GameClient client, int skillId);
}

public interface IHuntaholicEventListener
{
    void OnMonsterKilled(long instanceId, GameClient topDealer);
    void OnBeforeWarp(GameClient client, float x, float y);
    bool OnTryResurrect(GameClient client);
    void OnShowLobbyWindow(GameClient client, uint contactHandle);
    ResultCode OnCheckInstanceSkill(GameClient client, int skillId);
    ResultCode OnFireInstanceSkill(GameClient client, int skillId);
}

public sealed class HuntaholicEvents : IHuntaholicEvents
{
    private volatile IHuntaholicEventListener _listener;

    public void Attach(IHuntaholicEventListener listener) => _listener = listener;

    public void MonsterKilled(long instanceId, GameClient topDealer) => _listener?.OnMonsterKilled(instanceId, topDealer);

    public void BeforeWarp(GameClient client, float x, float y) => _listener?.OnBeforeWarp(client, x, y);

    public bool TryResurrect(GameClient client) => _listener?.OnTryResurrect(client) == true;

    public void ShowLobbyWindow(GameClient client, uint contactHandle) => _listener?.OnShowLobbyWindow(client, contactHandle);

    public ResultCode CheckInstanceSkill(GameClient client, int skillId) =>
        _listener?.OnCheckInstanceSkill(client, skillId) ?? ResultCode.NotExist;

    public ResultCode FireInstanceSkill(GameClient client, int skillId) =>
        _listener?.OnFireInstanceSkill(client, skillId) ?? ResultCode.NotExist;
}
