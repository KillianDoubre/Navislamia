using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Navislamia.AuthServer.Accounts;
using Navislamia.Game.DataAccess.Entities.Auth;
using Navislamia.Game.DataAccess.Repositories.Interfaces;
using NUnit.Framework;

namespace Tests.AuthServer;

[TestFixture]
public class PasswordHasherTests
{
    [Test]
    public void Verify_returns_true_for_correct_password()
    {
        var stored = PasswordHasher.Hash("hunter2");

        PasswordHasher.Verify("hunter2", stored).Should().BeTrue();
    }

    [Test]
    public void Verify_returns_false_for_wrong_password()
    {
        var stored = PasswordHasher.Hash("hunter2");

        PasswordHasher.Verify("wrong", stored).Should().BeFalse();
    }

    [Test]
    public void Hash_is_salted_so_two_hashes_of_same_password_differ()
    {
        PasswordHasher.Hash("same").Should().NotBe(PasswordHasher.Hash("same"));
    }

    [Test]
    public void Verify_returns_false_for_malformed_stored_value()
    {
        PasswordHasher.Verify("x", "not-a-valid-hash").Should().BeFalse();
    }
}

[TestFixture]
public class AccountServiceTests
{
    [Test]
    public async Task ValidateCredentials_returns_account_for_valid_credentials()
    {
        var repo = A.Fake<IAccountRepository>();
        var account = new AccountEntity { Username = "alice", PasswordHash = PasswordHasher.Hash("pw") };
        A.CallTo(() => repo.GetByUsernameAsync("alice")).Returns(account);
        var service = new AccountService(repo);

        var result = await service.ValidateCredentialsAsync("alice", "pw");

        result.Should().BeSameAs(account);
    }

    [Test]
    public async Task ValidateCredentials_returns_null_for_wrong_password()
    {
        var repo = A.Fake<IAccountRepository>();
        var account = new AccountEntity { Username = "alice", PasswordHash = PasswordHasher.Hash("pw") };
        A.CallTo(() => repo.GetByUsernameAsync("alice")).Returns(account);
        var service = new AccountService(repo);

        var result = await service.ValidateCredentialsAsync("alice", "bad");

        result.Should().BeNull();
    }

    [Test]
    public async Task ValidateCredentials_returns_null_for_unknown_account()
    {
        var repo = A.Fake<IAccountRepository>();
        A.CallTo(() => repo.GetByUsernameAsync(A<string>._)).Returns((AccountEntity?)null);
        var service = new AccountService(repo);

        var result = await service.ValidateCredentialsAsync("ghost", "pw");

        result.Should().BeNull();
    }
}
