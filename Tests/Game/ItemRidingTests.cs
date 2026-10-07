using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.Game.DataAccess.Entities.Enums;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Clients;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Network.Packets.Game;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Casting;
using Navislamia.Game.Services.Pets;
using Navislamia.Game.Services.Riding;
using Navislamia.Game.Services.Stats;

namespace Tests.Game;

/// <summary>
/// Riding by item (<c>ITEM_EFFECT_INSTANT::TOGGLE_STATE</c>, <c>EF_RIDING</c>): docs/packet-specs/socle-monture-objet.md.
/// The rental mount 550021 carries <c>opt_type_0 = 8</c>, state 7022 at level 10, and is reusable (type Use).
/// </summary>
[TestFixture]
public class ItemRidingTests
{
    private const int MountItem = 550021;
    private const int RidingState = 7022;
    private const uint Handle = 78;

    /// <summary>State 7022 as the database holds it: speed 270, falls 20/50, no faster-speed flag.</summary>
    private static readonly decimal[] RidingValues = { 270, 1010, 18015, 3, 18015, 3, 20, 50, 0 };

    private sealed class RidingStates : IStateCatalog
    {
        public IReadOnlyList<StatEffect> Resolve(int stateId, int stateLevel) => Array.Empty<StatEffect>();
        public bool IsEraseOnRequest(int stateId) => false;
        public bool Exists(int stateId) => true;

        public bool TryGetResurrection(int stateId, out ResurrectionStateValues values)
        {
            values = default;
            return false;
        }

        public StateRule GetRule(int stateId) => stateId == RidingState
            ? new StateRule(stateId, Array.Empty<int>(), 0, (StateTimeType)3, RidingStateValues.EffectType, RidingValues)
            : StateRule.None;
    }

    private sealed record Setup(GameClient Client, ConnectionInfo Info, StorageTestHarness.FrameConnection Connection,
        ICharacterService Character, ISkillCastService States, IEquipmentService Equipment, ItemUseService Service);

    private static Setup Build(ItemWearType wear = ItemWearType.None)
    {
        var repository = A.Fake<IItemResourceRepository>();
        A.CallTo(() => repository.GetUseFields()).Returns(new[]
        {
            new ItemUseFields(MountItem, 0, 0, ItemBaseType.Use, CoolTime: 10, CoolTimeGroup: 17,
                OptTypes: new short[] { (short)ItemEffectInstant.ToggleState }, OptVar1: new decimal[] { 0 },
                StateId: RidingState, StateLevel: 10)
        });
        var connection = new StorageTestHarness.FrameConnection(Array.Empty<byte>());
        var client = StorageTestHarness.NewGameClient(connection);
        var info = StorageTestHarness.Session(client);
        info.CharacterHandle = 1;
        info.CharacterName = "Rider";
        info.CharacterLevel = 20;
        info.CharacterHp = 100;
        info.X = 6650;
        info.Y = 6650;

        var character = A.Fake<ICharacterService>();
        A.CallTo(() => character.GetItemByHandleAsync("Rider", Handle)).Returns(Task.FromResult(
            new ItemEntity { Id = Handle, ItemResourceId = MountItem, Amount = 1, WearInfo = wear }));
        var states = A.Fake<ISkillCastService>();
        A.CallTo(() => states.ApplyPermanentState(client, RidingState, 10))
            .Invokes(() => info.ActiveBuffs.Add(new ActiveBuff(1, RidingState, 0, 10, ServerClock.Now, uint.MaxValue)))
            .Returns(true);
        var equipment = A.Fake<IEquipmentService>();
        A.CallTo(() => equipment.EquipRideItemAsync(client, Handle)).Returns(Task.FromResult(true));
        var pets = A.Fake<IPetSummonService>();
        A.CallTo(() => pets.TryUseCageAsync(A<GameClient>._, A<long>._, A<uint>._)).Returns(Task.FromResult(false));
        var service = new ItemUseService(character, new ItemUseCatalog(repository), pets, states, A.Fake<IStatService>(),
            stateCatalog: new RidingStates(), equipment: equipment);
        return new Setup(client, info, connection, character, states, equipment, service);
    }

    private static ushort LastResult(StorageTestHarness.FrameConnection connection) =>
        BitConverter.ToUInt16(connection.Sent.Last(frame => BitConverter.ToUInt16(frame, 4) == 0), 9);

    [Test]
    public async Task Using_a_ride_item_wears_it_in_slot_22_and_puts_its_riding_state_on_without_consuming_it()
    {
        var setup = Build();

        await setup.Service.UseAsync(setup.Client, new GameActionPackets.UseItemRequest(Handle, 1));

        A.CallTo(() => setup.Equipment.EquipRideItemAsync(setup.Client, Handle)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => setup.States.ApplyPermanentState(setup.Client, RidingState, 10))
                .MustHaveHappenedOnceExactly());
        A.CallTo(() => setup.Character.ConsumeItemAsync(A<string>._, A<uint>._, A<long>._)).MustNotHaveHappened();
        LastResult(setup.Connection).Should().Be((ushort)ResultCode.Success);
        ItemRiding.Current(setup.Info).Should().NotBeNull();
        ItemRiding.Current(setup.Info)!.Values.Speed.Should().Be(270);
    }

    [Test]
    public async Task Using_the_worn_ride_item_again_while_its_state_is_on_gets_off()
    {
        var setup = Build(ItemWearType.RideItem);
        setup.Info.ActiveBuffs.Add(new ActiveBuff(1, RidingState, 0, 10, ServerClock.Now, uint.MaxValue));

        await setup.Service.UseAsync(setup.Client, new GameActionPackets.UseItemRequest(Handle, 1));

        A.CallTo(() => setup.States.RemoveState(setup.Client, RidingState)).MustHaveHappenedOnceExactly();
        A.CallTo(() => setup.Equipment.EquipRideItemAsync(A<GameClient>._, A<uint>._)).MustNotHaveHappened();
        A.CallTo(() => setup.States.ApplyPermanentState(A<GameClient>._, A<int>._, A<int>._)).MustNotHaveHappened();
        LastResult(setup.Connection).Should().Be((ushort)ResultCode.Success);
    }

    [Test]
    public async Task A_worn_ride_item_whose_state_went_away_mounts_again_without_being_worn_twice()
    {
        // The riding state is erased at logout (state_time_type 3) while the item stays in slot 22.
        var setup = Build(ItemWearType.RideItem);

        await setup.Service.UseAsync(setup.Client, new GameActionPackets.UseItemRequest(Handle, 1));

        A.CallTo(() => setup.Equipment.EquipRideItemAsync(A<GameClient>._, A<uint>._)).MustNotHaveHappened();
        A.CallTo(() => setup.States.ApplyPermanentState(setup.Client, RidingState, 10)).MustHaveHappenedOnceExactly();
        ItemRiding.IsRiding(setup.Info).Should().BeTrue();
    }

    [Test]
    public async Task A_rider_of_a_summon_is_refused_access_denied_and_nothing_changes()
    {
        var setup = Build();
        setup.Info.RideHandle = 0x40001234;

        await setup.Service.UseAsync(setup.Client, new GameActionPackets.UseItemRequest(Handle, 1));

        LastResult(setup.Connection).Should().Be((ushort)ResultCode.AccessDenied);
        A.CallTo(() => setup.Equipment.EquipRideItemAsync(A<GameClient>._, A<uint>._)).MustNotHaveHappened();
        A.CallTo(() => setup.States.ApplyPermanentState(A<GameClient>._, A<int>._, A<int>._)).MustNotHaveHappened();
        setup.Info.ItemCooldowns.Should().BeEmpty();
    }

    [Test]
    public void The_riding_speed_is_the_state_speed_over_seven_unless_walking_is_faster_and_the_state_says_so()
    {
        var info = new ConnectionInfo();
        info.ActiveBuffs.Add(new ActiveBuff(1, RidingState, 0, 10, 0, uint.MaxValue));
        info.ItemRide = new ItemRide(RidingState, RidingStateValues.From(RidingValues), Handle);
        ItemRiding.Speed(info, 17).Should().Be(38);
        ItemRiding.Speed(info, 50).Should().Be(38);

        info.ItemRide = new ItemRide(RidingState, RidingStateValues.From(RidingValues) with { KeepFasterSpeed = true }, Handle);
        ItemRiding.Speed(info, 50).Should().Be(50);
    }

    [Test]
    public void A_ride_is_forgotten_once_its_state_is_gone()
    {
        var info = new ConnectionInfo();
        info.ItemRide = new ItemRide(RidingState, RidingStateValues.From(RidingValues), Handle);

        ItemRiding.Current(info).Should().BeNull();
        info.ItemRide.Should().BeNull();
        ItemRiding.Speed(info, 17).Should().BeNull();
    }

    [Test]
    public void A_hit_throws_the_rider_off_when_the_draw_does_not_exceed_the_state_chance()
    {
        var values = RidingStateValues.From(RidingValues);
        ItemRiding.FallsOnHit(values, false, _ => 20).Should().BeTrue();
        ItemRiding.FallsOnHit(values, false, _ => 21).Should().BeFalse();

        var draws = new Queue<int>(new[] { 50, 99 });
        ItemRiding.FallsOnHit(values, true, _ => draws.Dequeue()).Should().BeTrue();
    }

    [Test]
    public void Riding_is_refused_in_the_secret_and_instance_dungeons_and_the_arenas()
    {
        ItemRiding.IsMountablePlace(6650, 6650, 0).Should().BeTrue();
        foreach (short type in new short[] { 12, 14, 15, 16 })
        {
            ItemRiding.IsMountablePlace(6650, 6650, type).Should().BeFalse();
        }
    }
}
