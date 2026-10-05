using System;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using Navislamia.Game.Network.Packets;
using Navislamia.Game.Services;
using Navislamia.Game.Services.Creatures;
using Navislamia.Game.Services.Buffs;
using Navislamia.Game.Services.Stats;
using NUnit.Framework;

namespace Tests.Game;

[TestFixture]
public class SmallPacketsPersistenceTests
{
    [Test]
    public async Task Rename_and_gold_are_saved_together_with_ownership_and_banned_word_checks()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new TelecasterContext(options))
        {
            db.Characters.AddRange(new CharacterEntity { Id = 1, CharacterName = "Owner", Gold = 25000 },
                new CharacterEntity { Id = 2, CharacterName = "Other" });
            db.Items.Add(new ItemEntity { Id = 88, CharacterId = 1, ItemResourceId = 540015, Amount = 1 });
            db.Summons.Add(new SummonEntity { Id = 7, CharacterId = 1, CardItemId = 88, Name = "Original", Lv = 5, Sp = 100 });
            await db.SaveChangesAsync();
        }
        var banned = A.Fake<IBannedWordsRepository>(); A.CallTo(() => banned.ContainsBannedWord("Banned")).Returns(true);
        var service = new CharacterService(A.Fake<IStarterItemsRepository>(), new CharacterRepositoryFactory(options),
            new CharacterGate(), A.Fake<ILogger<CharacterService>>(), bannedWords: banned);
        (await service.RenameSummonAsync("Other", 7, "Renamed", 0)).Should().Be(ResultCode.NotExist);
        (await service.RenameSummonAsync("Owner", 7, "Banned", 0)).Should().Be(ResultCode.InvalidText);
        (await service.RenameSummonAsync("Owner", 7, "original", 0)).Should().Be(ResultCode.AlreadyExist);
        (await service.RenameSummonAsync("Owner", 7, "Renamed", 20000)).Should().Be(ResultCode.Success);
        await using var verify = new TelecasterContext(options);
        (await verify.Summons.SingleAsync()).Name.Should().Be("Renamed");
        (await verify.Characters.SingleAsync(c => c.Id == 1)).Gold.Should().Be(20000);
        var card = new CreatureCard { SummonId = 7, SummonCode = 2201, SummonName = "Original", Level = 5, Sp = 345 };
        (await service.SaveSummonProgressAsync("Owner", new[] { card.Progress() })).Should().BeTrue();
        await verify.Entry(await verify.Summons.SingleAsync()).ReloadAsync();
        (await verify.Summons.SingleAsync()).Sp.Should().Be(345);
        (await verify.Summons.SingleAsync()).Name.Should().Be("Renamed", "a delayed progress snapshot must not undo the rename");
    }

    [TestCase(10031, 26, false)] [TestCase(10032, 26, true)]
    public void Master_SP_passives_use_variables_four_and_five_without_changing_the_master(int effect, float amount, bool percent)
    {
        var repository = A.Fake<ISkillResourceRepository>();
        A.CallTo(() => repository.GetStatPassives()).Returns(new[] {
            new SkillPassiveFields(123, effect, new decimal[] { 0, 0, 0, 0, 20, 2 }, 0, true) });
        var catalog = new SkillPassiveCatalog(repository);
        catalog.Resolve(123, 3, null).Should().BeEmpty();
        catalog.ResolveSummonSp(123, 3).Should().ContainSingle().Which.Should().Be(new StatEffect(StatTarget.MaxSp, amount, percent));
        catalog.ResolveSummonSp(123, 0).Should().BeEmpty();
        catalog.ResolveSummonSp(124, 3).Should().BeEmpty();
    }

    [Test]
    public void A_summon_interprets_bit_23_buffs_as_SP_instead_of_stamina()
    {
        var states = A.Fake<IStateCatalog>();
        A.CallTo(() => states.Resolve(123, 1)).Returns(new[] {
            new StatEffect(StatTarget.MaxStamina, 100, false), new StatEffect(StatTarget.MaxStamina, 0.1f, true) });
        var presence = new SummonPresence(99, new SummonWorldEntry {
            BaseStats = new StatBlock { MaxHp = 100, MaxMp = 100, MaxSp = 1000 } }, 0, 0, 0);
        presence.ActiveBuffs.Add(new ActiveBuff(1, 123, 123, 1, 0, uint.MaxValue));
        SummonBuffStats.Refresh(presence, states);
        presence.Stats.MaxSp.Should().BeApproximately(1210, 0.01f);
        presence.Stats.MaxStamina.Should().Be(0);
    }
}
