using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Contexts;
using Navislamia.Game.DataAccess.Entities.Telecaster;
using Navislamia.Game.DataAccess.Repositories;

namespace Tests.DataAccess;

/// <summary>
/// The ownership predicate of the commercial storage, read on the SQL the provider really produces. This
/// is the security-relevant part of the family (docs/packet-specs/socle-stockage-commercial-conteneur.md
/// §5.2): a uid must never resolve outside the account of the reader and the character it is aimed at, and
/// the comparison must stay on the 64 bits of the identity key.
/// <para>
/// No database is opened — the model is built and the query is translated offline
/// (<c>ToQueryString</c>) — which is the only way to see this code exercised in a container without a
/// PostgreSQL server.
/// </para>
/// </summary>
[TestFixture]
public class PaidItemRepositoryQueryTests
{
    private const long CharacterId = 41;
    private const long AccountId = 7;

    private static TelecasterContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TelecasterContext>()
            .UseNpgsql("Host=localhost;Database=telecaster;Username=navislamia;Password=navislamia")
            .Options;

        return new TelecasterContext(options);
    }

    private static CharacterEntity Reader() => new()
    {
        Id = CharacterId,
        AccountId = AccountId,
        CharacterName = "Killian"
    };

    [Test]
    public void VisibleRows_RestrictsTheQueryToTheAccountOfTheReader()
    {
        using var context = BuildContext();

        var sql = PaidItemRepository.VisibleRows(context, Reader()).ToQueryString();

        sql.Should().Contain("\"AccountId\" = ", "a row of another account must never be read");
        sql.Should().Contain("IS NULL", "an account-level delivery is visible to every character of it");
        sql.Should().Contain("\"IsCancel\"", "a cancelled order leaves the container");
        sql.Should().Contain("\"RestItemCount\" > 0", "an empty line leaves the container");
        sql.Should().Contain("ORDER BY", "the emission order is stable; the lines are sorted on the id");
        sql.Should().NotContain("IS NOT NULL",
            "the target condition must accept a null character id, never require one");
    }

    [Test]
    public void VisibleRows_ComparesTheUidOnTheFullWidthOfTheIdentityKey()
    {
        using var context = BuildContext();
        const uint uid = 0x48CE60u;

        var sql = PaidItemRepository.VisibleRows(context, Reader())
            .Where(row => row.Id == uid)
            .ToQueryString();

        sql.Should().Contain("\"Id\" = ",
            "the uid is matched against the identity key itself, never a truncated copy of it");
        sql.Should().NotContain("::int",
            "a truncation to int32 would make two rows sharing their low 32 bits one identity");
    }
}
