using System;

namespace Navislamia.Game.DataAccess.Entities.Telecaster;

/// <summary>
/// One delivery of the item shop: what the player bought outside the game, how much of it is left, and
/// who took it. The class is the official table <c>PaidItem</c> of the reference dump, column by column
/// (<c>reference/ngemity/Database/Telecaster.sql:419-439</c>); the entity and table names are the
/// Navislamia choice, the columns are not.
/// <para>
/// This is the container behind <c>TM_SC_COMMERCIAL_STORAGE_LIST</c> (10004):
/// <see cref="ItemCode"/> is the <c>code</c> of a line, <see cref="RestItemCount"/> its <c>count</c> and
/// <see cref="Id"/> the <c>commercial_item_uid</c> the client copies verbatim into
/// <c>TM_CS_TAKEOUT_COMMERCIAL_ITEM</c> (10005). It is <b>not</b> the kept/auction storage of the
/// repository (<see cref="ItemStorageEntity"/>): those are instantiated items the player owns, parqued
/// in a slot, while a paid item is neither owned nor instantiated before it is taken.
/// </para>
/// <para>
/// See docs/packet-specs/socle-stockage-commercial-conteneur.md §5.1 and §5.2.
/// </para>
/// </summary>
public class PaidItemEntity : Entity
{
    /// <summary>The buying account (<c>account_id</c>, <c>:421</c>): half of the ownership test.</summary>
    public long AccountId { get; set; }

    /// <summary>
    /// The delivery target (<c>avatar_id</c>, <c>:422</c>). Left empty for an account-level purchase: such
    /// a row is takeable by any character of <see cref="AccountId"/> (§5.2.3).
    /// </summary>
    public long? CharacterId { get; set; }

    /// <summary>The name of the target at buying time (<c>avatar_name</c>, <c>:423</c>); never read.</summary>
    public string CharacterName { get; set; }

    /// <summary>The item code (<c>item_code</c>, <c>:424</c>): the <c>code</c> of a 10004 line.</summary>
    public int ItemCode { get; set; }

    /// <summary>The quantity bought (<c>item_count</c>, <c>:425</c>); kept as bought, never read.</summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// The quantity still to be taken (<c>rest_item_count</c>, <c>:426</c>): the <c>count</c> of a 10004
    /// line and the ceiling of a takeout. A row at zero stays, it simply leaves the list (§7c).
    /// </summary>
    public int RestItemCount { get; set; }

    /// <summary>When the purchase was paid (<c>bought_time</c>, <c>:427</c>); kept, never read.</summary>
    public DateTime? BoughtTime { get; set; }

    /// <summary>
    /// The validity limit (<c>valid_time</c>, <c>:428</c>): stored, <b>not applied</b> — no reference says
    /// what the server does with it (§7d).
    /// </summary>
    public DateTime? ValidTime { get; set; }

    /// <summary>
    /// The destination server chosen outside the game (<c>server_name</c>, <c>:429</c>): stored, <b>not
    /// filtered</b> (§7e).
    /// </summary>
    public string ServerName { get; set; }

    /// <summary>The character that took the goods (<c>taken_avatar_id</c>, <c>:430</c>).</summary>
    public long? TakenCharacterId { get; set; }

    /// <summary>The name of the taker at takeout time (<c>taken_avatar_name</c>, <c>:431</c>).</summary>
    public string TakenCharacterName { get; set; }

    /// <summary>The server of the taker (<c>taken_server_name</c>, <c>:432</c>); kept, never written.</summary>
    public string TakenServerName { get; set; }

    /// <summary>When the goods were taken (<c>taken_time</c>, <c>:433</c>).</summary>
    public DateTime? TakenTime { get; set; }

    /// <summary>The account of the taker (<c>taken_account_id</c>, <c>:434</c>).</summary>
    public long? TakenAccountId { get; set; }

    /// <summary>
    /// The acknowledgement flag (<c>confirmed</c>, <c>:435>): read only, it feeds
    /// <c>new_item_count</c>. Nothing in this repository writes it — its meaning is not established
    /// (§7b). <c>confirmed_time</c> (<c>:436</c>) is neither read nor written.
    /// </summary>
    public int Confirmed { get; set; }

    /// <summary>When the purchase was acknowledged (<c>confirmed_time</c>, <c>:436</c>); never touched.</summary>
    public DateTime? ConfirmedTime { get; set; }

    /// <summary>
    /// The cancelled purchase flag (<c>isCancel</c>, <c>:437</c>): a cancelled row is excluded from the
    /// list, it must never be handed to the player (§5.3).
    /// </summary>
    public bool IsCancel { get; set; }
}
