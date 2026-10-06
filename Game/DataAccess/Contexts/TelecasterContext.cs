using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Entities.Telecaster;

namespace Navislamia.Game.DataAccess.Contexts;

public class TelecasterContext : SoftDeletionContext
{
    public DbSet<DonationScoreEntity> DonationScores { get; set; }
    public DbSet<CharacterFriendEntity> CharacterFriends { get; set; }
    public DbSet<CreatureFarmEntity> CreatureFarms { get; set; }
    public TelecasterContext(DbContextOptions<TelecasterContext> options) : base(options) { }

    public DbSet<AllianceEntity> Alliances { get; set; }
    public DbSet<AuctionEntity> Auctions { get; set; }
    public DbSet<AuctionListingEntity> AuctionListings { get; set; }
    public DbSet<AutoAuctionRegistrationEntity> AutoAuctionRegistrations { get; set; }
    public DbSet<AuctionKeepingEntity> AuctionKeepings { get; set; }
    public DbSet<CharacterEntity> Characters { get; set; }
    public DbSet<CharacterSkillEntity> CharacterSkills { get; set; }
    public DbSet<SummonSkillEntity> SummonSkills { get; set; }
    public DbSet<CharacterStateEntity> CharacterStates { get; set; }
    public DbSet<CharacterTitleStateEntity> CharacterTitleStates { get; set; }
    public DbSet<CharacterQuestEntity> CharacterQuests { get; set; }
    public DbSet<CharacterQuestCompletionEntity> CharacterQuestCompletions { get; set; }
    public DbSet<DungeonEntity> Dungeons { get; set; }
    public DbSet<GuildEntity> Guilds { get; set; }
    public DbSet<GuildRaidEntity> GuildRaids { get; set; }
    public DbSet<GuildSiegeEntity> GuildSieges { get; set; }
    public DbSet<GuildSiegeParticipantEntity> GuildSiegeParticipants { get; set; }
    public DbSet<ItemEntity> Items { get; set; }
    public DbSet<ItemStorageEntity> ItemStorages { get; set; }
    public DbSet<PartyEntity> Parties { get; set; }
    public DbSet<PetEntity> Pets { get; set; }
    public DbSet<PaidItemEntity> PaidItems { get; set; }
    public DbSet<AccountStorageGoldEntity> AccountStorageGolds { get; set; }
    public DbSet<CharacterFavorEntity> CharacterFavors { get; set; }
    public DbSet<SummonEntity> Summons { get; set; }
    public DbSet<StarterItemsEntity> StarterItems { get; set; }
    public DbSet<GlobalVariableEntity> GlobalVariables { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DonationScoreEntity>().HasIndex(s => new { s.CharacterId, s.Period }).IsUnique()
            .HasFilter("\"DeletedOn\" IS NULL");
        modelBuilder.Entity<DonationScoreEntity>().Property(s => s.Score).HasPrecision(18, 4);
        modelBuilder.Entity<DonationScoreEntity>().HasOne<CharacterEntity>().WithMany()
            .HasForeignKey(s => s.CharacterId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CharacterFriendEntity>().HasIndex(f => new { f.OwnerId, f.TargetId, f.IsDenial }).IsUnique()
            .HasFilter("\"DeletedOn\" IS NULL");
        modelBuilder.Entity<CharacterFriendEntity>().HasIndex(f => f.TargetId);
        modelBuilder.Entity<CharacterFriendEntity>().HasOne<CharacterEntity>().WithMany()
            .HasForeignKey(f => f.OwnerId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CharacterFriendEntity>().HasOne<CharacterEntity>().WithMany()
            .HasForeignKey(f => f.TargetId).OnDelete(DeleteBehavior.Cascade);
        // One farm row per character and per slot: three slots at most, none of them shared. The unique
        // index ignores soft-deleted rows like the two above.
        modelBuilder.Entity<CreatureFarmEntity>().HasIndex(f => new { f.CharacterId, f.Slot }).IsUnique()
            .HasFilter("\"DeletedOn\" IS NULL");
        modelBuilder.Entity<CreatureFarmEntity>().HasIndex(f => f.CardItemId);
        // The navigation is bound by name on purpose: an explicit HasOne<CharacterEntity>() leaves
        // CreatureFarmEntity.Character to the convention, which then mints a second, shadow relationship
        // (nullable CharacterId1 + its index), because CharacterId is already taken by this one.
        modelBuilder.Entity<CreatureFarmEntity>().HasOne(f => f.Character).WithMany()
            .HasForeignKey(f => f.CharacterId).OnDelete(DeleteBehavior.Cascade);
        base.OnModelCreating(modelBuilder);

        ConfigureAuctions(modelBuilder);
        modelBuilder.Entity<AuctionListingEntity>().HasIndex(a => a.SellerId);
        modelBuilder.Entity<AutoAuctionRegistrationEntity>().HasIndex(a => a.ResourceId).IsUnique();
        modelBuilder.Entity<AuctionKeepingEntity>().HasIndex(k => k.OwnerId);
        modelBuilder.Entity<CharacterTitleStateEntity>().HasKey(s => s.CharacterId);
        modelBuilder.Entity<CharacterTitleStateEntity>().HasOne<CharacterEntity>().WithOne()
            .HasForeignKey<CharacterTitleStateEntity>(s => s.CharacterId).OnDelete(DeleteBehavior.Cascade);
        ConfigureCharacters(modelBuilder);
        ConfigureItems(modelBuilder);
        ConfigureItemStorages(modelBuilder);
        ConfigureParties(modelBuilder);
        ConfigureGuilds(modelBuilder);
        ConfigurePets(modelBuilder);
        ConfigureSummons(modelBuilder);
        ConfigurePaidItems(modelBuilder);
        ConfigureStorageGoldAndFavors(modelBuilder);
        modelBuilder.Entity<CharacterStateEntity>().HasIndex(s => new { s.CharacterId, s.SummonCardId });
        modelBuilder.Entity<CharacterStateEntity>().HasOne<CharacterEntity>().WithMany()
            .HasForeignKey(s => s.CharacterId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// One stored-gold row per account and one favor counter per character and id. Both unique indexes
    /// ignore soft-deleted rows, the way the active-quest index does.
    /// </summary>
    private static void ConfigureStorageGoldAndFavors(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountStorageGoldEntity>()
            .HasIndex(row => row.AccountId)
            .IsUnique().HasFilter("\"DeletedOn\" IS NULL");

        modelBuilder.Entity<CharacterFavorEntity>()
            .HasIndex(row => new { row.CharacterId, row.FavorId })
            .IsUnique().HasFilter("\"DeletedOn\" IS NULL");
        modelBuilder.Entity<CharacterFavorEntity>()
            .HasOne<CharacterEntity>()
            .WithMany()
            .HasForeignKey(row => row.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// The commercial storage (item shop) container: one table of its own, never the kept/auction
    /// <c>ItemStorages</c> (docs/packet-specs/socle-stockage-commercial-conteneur.md §5.1). The index is
    /// the pair the read path filters on at every world entry: the account of the reader and the target
    /// of the delivery, which is empty for an account-level purchase.
    /// </summary>
    private static void ConfigurePaidItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaidItemEntity>().HasIndex(row => new { row.AccountId, row.CharacterId });
    }
    
    private static void ConfigureItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ItemEntity>()
            .HasOne(c => c.Summon)
            .WithOne(i => i.CardItem)
            .HasForeignKey<SummonEntity>(s => s.CardItemId);
        
        modelBuilder.Entity<ItemEntity>()
            .HasOne(c => c.Auction)
            .WithOne(a => a.Item)
            .HasForeignKey<AuctionEntity>(a => a.ItemId);
        
        modelBuilder.Entity<ItemEntity>()
            .HasOne(c => c.ItemStorage)
            .WithOne(a => a.Item)
            .HasForeignKey<ItemStorageEntity>(a => a.ItemId);
        
        modelBuilder.Entity<ItemEntity>().Property(i => i.SocketItemIds).HasMaxLength(4);

        // The counter storage is read by account (StorageRepository.StorageRows): without an index every
        // storage open and every storage move scanned the whole item table.
        modelBuilder.Entity<ItemEntity>().HasIndex(i => i.AccountId);
    }
    
    private static void ConfigureCharacters(ModelBuilder modelBuilder)
    {
        // Every character operation resolves the row by name, the lobby by account name and the creation
        // limit by account id. None of the three was indexed, so each one scanned the character table.
        // Not unique: existing data is not guaranteed to be, and uniqueness is the name check's job.
        modelBuilder.Entity<CharacterEntity>().HasIndex(c => c.CharacterName);
        modelBuilder.Entity<CharacterEntity>().HasIndex(c => c.AccountName);
        modelBuilder.Entity<CharacterEntity>().HasIndex(c => c.AccountId);

        modelBuilder.Entity<CharacterEntity>()
            .HasMany(c => c.Items)
            .WithOne(i => i.Character)
            .HasForeignKey(i => i.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CharacterEntity>()
            .HasMany(c => c.Skills)
            .WithOne(skill => skill.Character)
            .HasForeignKey(skill => skill.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CharacterSkillEntity>()
            .HasIndex(skill => new { skill.CharacterId, skill.SkillId })
            .IsUnique();

        // A summon's skills (StructSummon::onRegisterSkill): one row per summon and skill, deleted with the summon.
        modelBuilder.Entity<SummonSkillEntity>()
            .HasOne(skill => skill.Summon)
            .WithMany()
            .HasForeignKey(skill => skill.SummonId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SummonSkillEntity>()
            .HasIndex(skill => new { skill.SummonId, skill.SkillId })
            .IsUnique();

        modelBuilder.Entity<CharacterEntity>()
            .HasMany(c => c.Quests)
            .WithOne(quest => quest.Character)
            .HasForeignKey(quest => quest.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        // A character carries a code once: the row is the state 603 erases by character + code and
        // 600 re-reads whole. The six wire slots live in two fixed arrays.
        modelBuilder.Entity<CharacterQuestEntity>()
            .HasIndex(quest => new { quest.CharacterId, quest.Code })
            .IsUnique().HasFilter("\"DeletedOn\" IS NULL");

        modelBuilder.Entity<CharacterQuestEntity>().Property(quest => quest.Value).HasMaxLength(6);
        modelBuilder.Entity<CharacterQuestEntity>().Property(quest => quest.Status).HasMaxLength(6);
        modelBuilder.Entity<CharacterQuestCompletionEntity>()
            .HasIndex(quest => new { quest.CharacterId, quest.Code })
            .IsUnique();
        modelBuilder.Entity<CharacterQuestCompletionEntity>()
            .HasOne<CharacterEntity>()
            .WithMany()
            .HasForeignKey(quest => quest.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<CharacterEntity>()
            .HasOne(c => c.Party)
            .WithMany(i => i.PartyMembers)
            .HasForeignKey(c => c.PartyId);
        
        // A leader may have led parties before (soft-deleted rows keep them): not unique (socle-groupe.md §persistance).
        modelBuilder.Entity<PartyEntity>()
            .HasOne(p => p.Leader)
            .WithMany()
            .HasForeignKey(p => p.LeaderId);
        
        modelBuilder.Entity<CharacterEntity>()
            .HasOne(c => c.Guild)
            .WithMany(g => g.Members)
            .HasForeignKey(c => c.GuildId);
        
        modelBuilder.Entity<CharacterEntity>()
            .HasOne(c => c.PreviousGuild)
            .WithMany(g => g.PreviousMembers)
            .HasForeignKey(c => c.PreviousGuildId);
        
        modelBuilder.Entity<CharacterEntity>()
            .HasOne(c => c.MainSummon)
            .WithOne(g => g.MainSummonsMaster)
            .HasForeignKey<CharacterEntity>(c => c.MainSummonId);
        
        modelBuilder.Entity<CharacterEntity>()
            .HasOne(c => c.SubSummon)
            .WithOne(g => g.SubSummonsMaster)
            .HasForeignKey<CharacterEntity>(c => c.SubSummonId);
        
        modelBuilder.Entity<CharacterEntity>()
            .HasOne(c => c.Pet)
            .WithOne(g => g.Character)
            .HasForeignKey<CharacterEntity>(c => c.PetId);
        
        modelBuilder.Entity<CharacterEntity>().Property(c => c.Position).HasMaxLength(3);
        modelBuilder.Entity<CharacterEntity>().Property(c => c.PreviousJobs).HasMaxLength(3);
        modelBuilder.Entity<CharacterEntity>().Property(c => c.JobLvs).HasMaxLength(3);
        modelBuilder.Entity<CharacterEntity>().Property(c => c.Models).HasMaxLength(5);
        modelBuilder.Entity<CharacterEntity>().Property(c => c.BeltItemIds).HasMaxLength(6);
        modelBuilder.Entity<CharacterEntity>().Property(c => c.SummonSlotItemIds).HasMaxLength(6);
    }
    
    private static void ConfigureAuctions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuctionEntity>()
            .HasOne(a => a.Seller)
            .WithMany(c => c.Sellers)
            .HasForeignKey(a => a.SellerId);
        
        modelBuilder.Entity<AuctionEntity>()
            .HasOne(a => a.HighestBidder)
            .WithMany(c => c.HighestBidders)
            .HasForeignKey(a => a.HighestBidderId);
    }
    
    private static void ConfigureItemStorages(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ItemStorageEntity>()
            .HasOne(s => s.Character)
            .WithOne(c => c.ItemStorage)
            .HasForeignKey<ItemStorageEntity>(s => s.CharacterId);
        
        modelBuilder.Entity<ItemStorageEntity>()
            .HasOne(s => s.RelatedAuction)
            .WithOne(a => a.ItemStorage)
            .HasForeignKey<ItemStorageEntity>(s => s.RelatedAuctionId);
        
        modelBuilder.Entity<ItemStorageEntity>()
            .HasOne(s => s.RelatedItem)
            .WithOne(a => a.RelatedItemStorage)
            .HasForeignKey<ItemStorageEntity>(s => s.RelatedItemId);
    }
    
    private static void ConfigureParties(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PartyEntity>()
            .HasOne(s => s.LeadParty)
            .WithMany(a => a.RaidParties)
            .HasForeignKey(s => s.LeadPartyId);
    }
    
    private static void ConfigurePets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PetEntity>()
            .HasOne(s => s.Item)
            .WithOne(a => a.PetItem)
            .HasForeignKey<PetEntity>(s => s.ItemId);
    }
    
    private static void ConfigureSummons(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SummonEntity>().Property(s => s.PreviousLevel).HasMaxLength(2);
        modelBuilder.Entity<SummonEntity>().Property(s => s.PreviousSummonResourceIds).HasMaxLength(2);
    }
    
    private static void ConfigureGuilds(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildEntity>()
            .HasOne(s => s.Alliance)
            .WithMany(a => a.Guilds)
            .HasForeignKey(g => g.AllianceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AllianceEntity>().HasOne(a => a.LeadGuild).WithMany()
            .HasForeignKey(a => a.LeadGuildId).OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<GuildEntity>()
            .HasOne(s => s.Dungeon)
            .WithMany()
            .HasForeignKey(g => g.DungeonId).OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<DungeonEntity>().HasOne(d => d.OwnerGuild).WithMany()
            .HasForeignKey(d => d.OwnerGuildId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DungeonEntity>().HasOne(d => d.RaidGuild).WithMany()
            .HasForeignKey(d => d.RaidGuildId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GuildEntity>().HasOne<CharacterEntity>().WithMany()
            .HasForeignKey(g => g.LeaderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GuildEntity>().HasIndex(g => g.NormalizedName).IsUnique().HasFilter("\"DeletedOn\" IS NULL");
        modelBuilder.Entity<AllianceEntity>().HasIndex(g => g.NormalizedName).IsUnique().HasFilter("\"DeletedOn\" IS NULL");
        modelBuilder.Entity<GuildRaidEntity>().HasIndex(r => new { r.GuildId, r.Week }).IsUnique().HasFilter("\"DeletedOn\" IS NULL");
        modelBuilder.Entity<GuildSiegeEntity>().HasIndex(r => new { r.DungeonId, r.Week }).IsUnique().HasFilter("\"DeletedOn\" IS NULL");
        modelBuilder.Entity<GuildSiegeParticipantEntity>().HasIndex(r => new { r.SiegeId, r.CharacterId }).IsUnique();
        modelBuilder.Entity<GuildRaidEntity>().HasOne<GuildEntity>().WithMany().HasForeignKey(r => r.GuildId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GuildRaidEntity>().HasOne<DungeonEntity>().WithMany().HasForeignKey(r => r.DungeonId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GuildSiegeEntity>().HasOne<DungeonEntity>().WithMany().HasForeignKey(r => r.DungeonId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GuildSiegeParticipantEntity>().HasOne<GuildSiegeEntity>().WithMany().HasForeignKey(r => r.SiegeId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GuildSiegeParticipantEntity>().HasOne<CharacterEntity>().WithMany().HasForeignKey(r => r.CharacterId).OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<GuildEntity>().Property(g => g.PermissionNames).HasMaxLength(6);
        modelBuilder.Entity<GuildEntity>().Property(g => g.PermissionSets).HasMaxLength(6);

        
        // usage could not be found in Captain
        // modelBuilder.Entity<GuildEntity>()
        //     .HasOne(s => s.Leader)
        //     .WithOne(a => a.LeadersGuild)
        //     .HasForeignKey<GuildEntity>(g => g.LeaderId);
        //
        // modelBuilder.Entity<GuildEntity>()
        //     .HasOne(s => s.RaidLeader)
        //     .WithOne(a => a.RaidLeadersGuild)
        //     .HasForeignKey<GuildEntity>(g => g.RaidLeaderId); 

    }
}
