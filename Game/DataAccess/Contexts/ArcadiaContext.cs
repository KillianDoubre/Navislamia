using Microsoft.EntityFrameworkCore;
using Navislamia.Game.DataAccess.Entities;
using Navislamia.Game.DataAccess.Entities.Arcadia;

namespace Navislamia.Game.DataAccess.Contexts;

public class ArcadiaContext : SoftDeletionContext
{
    public ArcadiaContext(DbContextOptions<ArcadiaContext> options) : base(options) { }

    public DbSet<ChannelResourceEntity> ChannelResources { get; set; }
    public DbSet<GlobalVariableEntity> GlobalVariables { get; set; }
    public DbSet<ItemResourceEntity> ItemResources { get; set; }
    public DbSet<ItemEffectResourceEntity> ItemEffectResources { get; set; }
    public DbSet<StringResourceEntity> StringResources { get; set; }
    public DbSet<SetItemEffectResourceEntity> SetItemEffectResources { get; set; }
    public DbSet<SummonResourceEntity> SummonResources { get; set; }
    public DbSet<EnhanceResourceEntity> EnhanceResources { get; set; }
    public DbSet<EffectResourceEntity> EffectResources { get; set; }
    public DbSet<LevelResourceEntity> LevelResources { get; set; }
    public DbSet<SkillResourceEntity> SkillResources { get; set; }
    public DbSet<StateResourceEntity> StateResources { get; set; }
    public DbSet<StatResourceEntity> StatResources { get; set; }
    public DbSet<JobResourceEntity> JobResources { get; set; }
    public DbSet<JobLevelBonusEntity> JobLevelBonuses { get; set; }
    public DbSet<ModelEffectResourceEntity> ModelEffectResources { get; set; }
    public DbSet<BannedWordsResourceEntity> BannedWordsResources { get; set; }
    public DbSet<NpcResourceEntity> NpcResources { get; set; }
    public DbSet<MonsterResourceEntity> MonsterResources { get; set; }
    public DbSet<AuctionCateryResourceEntity> AuctionCateryResources { get; set; }
    public DbSet<WorldLocationEntity> WorldLocations { get; set; }
    public DbSet<QuestResourceEntity> QuestResources { get; set; }
    public DbSet<QuestLinkResourceEntity> QuestLinkResources { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureItemResources(modelBuilder);
        ConfigureSetItemEffectResources(modelBuilder);
        ConfigureSummonResources(modelBuilder);
        ConfigureStateResources(modelBuilder);
        ConfigureSkillResources(modelBuilder);
        ConfigureEffectResources(modelBuilder);
        ConfigureEnhanceResource(modelBuilder);
        ConfigureLevelResource(modelBuilder);
        ConfigureModelEffectResource(modelBuilder);
        ConfigureBannedWordsResource(modelBuilder);
        ConfigureMonsterResource(modelBuilder);
        ConfigureAuctionCateryResource(modelBuilder);
        ConfigureWorldLocations(modelBuilder);
        ConfigureQuestCatalogue(modelBuilder);
    }

    /// <summary>
    /// The two tables of the quest catalogue. Every <c>char</c> column of the schema is declared as a
    /// one-character column (<c>character varying(1)</c>, <c>HasMaxLength(1)</c>): the retail data holds a
    /// character there (<c>'0'</c>/<c>'1'</c>) and no reference proves the same domain for each flag, so none
    /// of them is folded into a <c>bool</c> (docs/packet-specs/socle-cycle-quete.md §5.1).
    /// <c>QuestLinkResource</c> declares no primary key, so the pair (npc_id, quest_id) is used as one — EF
    /// requires a key and that is the pair the schema's data uses once per row.
    /// </summary>
    private static void ConfigureQuestCatalogue(ModelBuilder modelBuilder)
    {
        var quest = modelBuilder.Entity<QuestResourceEntity>();

        // The schema declares these fifteen columns `not null`; every other column of the table carries its
        // own type and nullability without configuration. Without IsRequired they would be created nullable,
        // because this project compiles without nullable reference types and EF then reads every string as
        // optional — which is what the other resource tables of this context do (MonsterResource.model is
        // `not null` in the schema and optional in the model).
        quest.Property(resource => resource.LimitDeva).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.LimitAsura).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.LimitGaia).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.LimitFighter).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.LimitHunter).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.LimitMagician).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.LimitSummoner).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.Repeatable).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.OrFlag).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.IsAutoQuest).HasMaxLength(1).IsRequired();
        quest.Property(resource => resource.TimeLimitType).HasMaxLength(10).IsRequired();
        quest.Property(resource => resource.ScriptStartText).HasMaxLength(512);
        quest.Property(resource => resource.ScriptEndText).HasMaxLength(512);
        quest.Property(resource => resource.ScriptDropText).HasMaxLength(512);
        quest.Property(resource => resource.ShowTargetType).HasMaxLength(1);
        quest.Property(resource => resource.MarkHide).HasMaxLength(1);

        var questLink = modelBuilder.Entity<QuestLinkResourceEntity>();

        // The table declares no primary key; EF requires one, so the pair the schema's data uses once per
        // row is declared as the key. That uniqueness is a modelling choice, not a schema guarantee.
        questLink.HasKey(link => new { link.NpcId, link.QuestId });

        questLink.Property(link => link.FlagStart).HasMaxLength(1).IsRequired();
        questLink.Property(link => link.FlagProgress).HasMaxLength(1).IsRequired();
        questLink.Property(link => link.FlagEnd).HasMaxLength(1).IsRequired();
    }

    /// <summary>
    /// The table has no surrogate id: a category is identified by its <c>catery_id</c> /
    /// <c>sub_catery_id</c> pair, the key already used by <c>EnhanceResourceEntity</c> and
    /// <c>SetItemEffectResourceEntity</c>. The uniqueness of that pair is not declared by
    /// <c>ArcadiaSchemaPSQL.sql:1-9</c>.
    /// </summary>
    private static void ConfigureAuctionCateryResource(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuctionCateryResourceEntity>()
            .HasKey(catery => new { catery.CateryId, catery.SubCateryId });
    }

    private static void ConfigureWorldLocations(ModelBuilder modelBuilder)
    {
        // WorldLocation has no primary key of its own and one row per (id, weather_id, time_id): the three
        // columns together are the natural key.
        modelBuilder.Entity<WorldLocationEntity>()
            .HasKey(location => new { location.Id, location.WeatherId, location.TimeId });
    }

    private static void ConfigureMonsterResource(ModelBuilder modelBuilder)
    {
        var monster = modelBuilder.Entity<MonsterResourceEntity>();
        monster.Property(m => m.Size).HasPrecision(10, 2);
        monster.Property(m => m.Scale).HasPrecision(10, 2);
        monster.Property(m => m.TargetFxSize).HasPrecision(10, 2);
        monster.Property(m => m.TargetX).HasPrecision(10, 2);
        monster.Property(m => m.TargetY).HasPrecision(10, 2);
        monster.Property(m => m.TargetZ).HasPrecision(10, 2);
        monster.Property(m => m.AttackRange).HasPrecision(10, 2);
        monster.Property(m => m.HidesenseRange).HasPrecision(10, 2);
        monster.Property(m => m.TamingPercentage).HasPrecision(12, 4);
        monster.Property(m => m.TamingExpMod).HasPrecision(10, 2);
    }
    
    private static void ConfigureBannedWordsResource(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BannedWordsResourceEntity>()
            .HasKey(b => new { b.Id, b.Word });
    }

    private static void ConfigureModelEffectResource(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelEffectResourceEntity>()
            .HasOne(s => s.EffectFile)
            .WithOne(s => s.Model)
            .HasForeignKey<ModelEffectResourceEntity>(s => s.EffectFileId);
    }

    private static void ConfigureItemResources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ItemResourceEntity>()
            .HasOne(item => item.Name)
            .WithMany(s => s.ItemResourceNames)
            .HasForeignKey(item => item.NameId);
        
        modelBuilder.Entity<ItemResourceEntity>()
            .HasOne(item => item.Tooltip)
            .WithMany(s => s.ItemResourceTooltips)
            .HasForeignKey(item => item.TooltipId);
        
        modelBuilder.Entity<ItemResourceEntity>()
            .HasOne(item => item.Name)
            .WithMany(s => s.ItemResourceNames)
            .HasForeignKey(item => item.NameId);
        
        modelBuilder.Entity<ItemResourceEntity>()
            .HasOne(item => item.Effect)
            .WithOne(effect => effect.Item)
            .HasForeignKey<ItemResourceEntity>(item => item.EffectId).IsRequired(false);

        modelBuilder.Entity<ItemResourceEntity>()
            .HasOne(item => item.Skill)
            .WithMany(skill => skill.Items)
            .HasForeignKey(item => item.SkillId);
        
        modelBuilder.Entity<ItemResourceEntity>()
            .HasOne(item => item.State)
            .WithMany(state => state.Items)
            .HasForeignKey(item => item.StateId);
       

        
        modelBuilder.Entity<ItemResourceEntity>().ToTable(i => i
            .HasCheckConstraint(
                $"CK_{nameof(ItemResourceEntity)}_{nameof(ItemResourceEntity.BaseTypes)}_MaxSize4",
                $"cardinality(\"{nameof(ItemResourceEntity.BaseTypes)}\") <= 4"));
        
        modelBuilder.Entity<ItemResourceEntity>().ToTable(i => i
                .HasCheckConstraint(
                    $"CK_{nameof(ItemResourceEntity)}_{nameof(ItemResourceEntity.BaseVar1)}_MaxSize4",
                    $"cardinality(\"{nameof(ItemResourceEntity.BaseVar1)}\") <= 4"))
            .Property(i => i.BaseVar1)
            .HasPrecision(12, 2);

        modelBuilder.Entity<ItemResourceEntity>().ToTable(i => i
                .HasCheckConstraint(
                    $"CK_{nameof(ItemResourceEntity)}_{nameof(ItemResourceEntity.BaseVar2)}_MaxSize4",
                    $"cardinality(\"{nameof(ItemResourceEntity.BaseVar2)}\") <= 4"))
            .Property(i => i.BaseVar2)
            .HasPrecision(12, 2);

        modelBuilder.Entity<ItemResourceEntity>().ToTable(i => i
            .HasCheckConstraint(
                $"CK_{nameof(ItemResourceEntity)}_{nameof(ItemResourceEntity.OptTypes)}_MaxSize4",
                $"cardinality(\"{nameof(ItemResourceEntity.OptTypes)}\") <= 4"));

        modelBuilder.Entity<ItemResourceEntity>().ToTable(i => i
                .HasCheckConstraint(
                    $"CK_{nameof(ItemResourceEntity)}_{nameof(ItemResourceEntity.OptVar1)}_MaxSize4",
                    $"cardinality(\"{nameof(ItemResourceEntity.OptVar1)}\") <= 4"))
            .Property(i => i.OptVar1)
            .HasPrecision(12, 2);

        modelBuilder.Entity<ItemResourceEntity>().ToTable(i => i
                .HasCheckConstraint(
                    $"CK_{nameof(ItemResourceEntity)}_{nameof(ItemResourceEntity.OptVar2)}_MaxSize4",
                    $"cardinality(\"{nameof(ItemResourceEntity.OptVar2)}\") <= 4"))
            .Property(i => i.OptVar2)
            .HasPrecision(12, 2);

        modelBuilder.Entity<ItemResourceEntity>().ToTable(i => i
            .HasCheckConstraint(
                $"CK_{nameof(ItemResourceEntity)}_{nameof(ItemResourceEntity.EnhanceIds)}_MaxSize2",
                $"cardinality(\"{nameof(ItemResourceEntity.EnhanceIds)}\") <= 2"));
        
        modelBuilder.Entity<ItemResourceEntity>().ToTable(i => i
            .HasCheckConstraint(
                $"CK_{nameof(ItemResourceEntity)}_{nameof(ItemResourceEntity.EnhanceValues)}_MaxSize8",
                $"cardinality(\"{nameof(ItemResourceEntity.EnhanceValues)}\") <= 8"))
            .Property(i => i.EnhanceValues)
            .HasPrecision(10, 2);
        
        modelBuilder.Entity<ItemResourceEntity>().Property(i => i.Range).HasPrecision(10, 2);
        modelBuilder.Entity<ItemResourceEntity>().Property(i => i.Weight).HasPrecision(10, 2);
        modelBuilder.Entity<ItemResourceEntity>().Property(i => i.ThrowRange).HasPrecision(10, 2);
    }
    
    private static void ConfigureEnhanceResource(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EnhanceResourceEntity>()
            .HasOne(enhance => enhance.RequiredItem)
            .WithMany(item => item.RequiredByEnhanceResources)
            .HasForeignKey(enhance => enhance.RequiredItemId);
        
        modelBuilder.Entity<EnhanceResourceEntity>().HasKey(enhance => new { enhance.Id, enhance.LocalFlag });
        
        modelBuilder.Entity<EnhanceResourceEntity>().ToTable(enhance => enhance
                .HasCheckConstraint(
                    $"CK_{nameof(EnhanceResourceEntity)}_{nameof(EnhanceResourceEntity.Percentage)}_MaxSize20",
                    $"cardinality(\"{nameof(EnhanceResourceEntity.Percentage)}\") <= 20"))
            .Property(enhance => enhance.Percentage)
            .HasPrecision(10, 3);
    }

    private static void ConfigureSetItemEffectResources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SetItemEffectResourceEntity>()
            .HasKey(set => new { set.Id, set.Parts });

        modelBuilder.Entity<SetItemEffectResourceEntity>()
            .HasOne(set => set.Text)
            .WithMany(str => str.SetTexts)
            .HasForeignKey(set => set.TextId);
        
        modelBuilder.Entity<SetItemEffectResourceEntity>()
            .HasOne(set => set.Tooltip)
            .WithMany(str => str.SetTooltips)
            .HasForeignKey(set => set.TooltipId);
        
        modelBuilder.Entity<SetItemEffectResourceEntity>().ToTable(set => set
                .HasCheckConstraint(
                    $"CK_{nameof(SetItemEffectResourceEntity)}_{nameof(SetItemEffectResourceEntity.BaseTypes)}_MaxSize4",
                    $"cardinality(\"{nameof(SetItemEffectResourceEntity.BaseTypes)}\") <= 4"))
            .Property(set => set.BaseTypes);
        
        modelBuilder.Entity<SetItemEffectResourceEntity>().ToTable(set => set
                .HasCheckConstraint(
                    $"CK_{nameof(SetItemEffectResourceEntity)}_{nameof(SetItemEffectResourceEntity.BaseValues)}_MaxSize8",
                    $"cardinality(\"{nameof(SetItemEffectResourceEntity.BaseValues)}\") <= 8"))
            .Property(set => set.BaseValues)
            .HasPrecision(12, 2);
        
        modelBuilder.Entity<SetItemEffectResourceEntity>().ToTable(set => set
                .HasCheckConstraint(
                    $"CK_{nameof(SetItemEffectResourceEntity)}_{nameof(SetItemEffectResourceEntity.OptTypes)}_MaxSize4",
                    $"cardinality(\"{nameof(SetItemEffectResourceEntity.OptTypes)}\") <= 4"))
            .Property(set => set.OptTypes);
        
        modelBuilder.Entity<SetItemEffectResourceEntity>().ToTable(set => set
                .HasCheckConstraint(
                    $"CK_{nameof(SetItemEffectResourceEntity)}_{nameof(SetItemEffectResourceEntity.OptValues)}_MaxSize8",
                    $"cardinality(\"{nameof(SetItemEffectResourceEntity.OptValues)}\") <= 8"))
            .Property(set => set.OptValues)
            .HasPrecision(12, 2);
        
    }
    
    private static void ConfigureLevelResource(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LevelResourceEntity>().HasKey(lvl => lvl.Level);
        modelBuilder.Entity<LevelResourceEntity>().ToTable(i => i
            .HasCheckConstraint(
                $"CK_{nameof(LevelResourceEntity)}_{nameof(LevelResourceEntity.JLvs)}_MaxSize4",
                $"cardinality(\"{nameof(LevelResourceEntity.JLvs)}\") <= 4"));
    }
    
    private static void ConfigureSkillResources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SkillResourceEntity>()
            .HasOne(s => s.Text)
            .WithMany(s => s.SkillTexts)
            .HasForeignKey(s => s.TextId);
        
        modelBuilder.Entity<SkillResourceEntity>()
            .HasOne(s => s.Description)
            .WithMany(s => s.SkillDescriptions)
            .HasForeignKey(s => s.DescriptionId);
        
        modelBuilder.Entity<SkillResourceEntity>()
            .HasOne(s => s.Tooltip)
            .WithMany(s => s.SkillTooltips)
            .HasForeignKey(s => s.TooltipId);
        
        modelBuilder.Entity<SkillResourceEntity>()
            .HasOne(s => s.SkillUpgrade)
            .WithOne(s => s.Skill)
            .HasForeignKey<SkillResourceEntity>(s => s.UpgradeIntoSkillId);
        
        modelBuilder.Entity<SkillResourceEntity>()
            .HasOne(s => s.State)
            .WithMany(s => s.Skills)
            .HasForeignKey(s => s.StateId);
        
        modelBuilder.Entity<SkillResourceEntity>()
            .HasOne(s => s.RequiredState)
            .WithMany(s => s.RequiredBySkills)
            .HasForeignKey(s => s.RequiredStateId);

        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.CostHpPer).HasPrecision(10, 2); 
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.CostHpPerSklPer).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.CostMpPer).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.CostMpPerSklPer).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.CostEnergy).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.CostEnergyPerSkl).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.DelayCast).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.DelayCastPerSkl).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.DelayCastModePerEnhance).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.DelayCommon).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.DelayCooltime).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.DelayCooltimePerSkl).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.DelayCooltimeModePerEnhance).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.StateLevelPerSkill).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.StateLevelPerEnhance).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.StateSecond).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.StateSecondPerLevel).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.StateSecondPerEnhance).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.HateMod).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.HatePerSkill).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.HatePerEnhance).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().ToTable(s => s
                .HasCheckConstraint(
                    $"CK_{nameof(SkillResourceEntity)}_{nameof(SkillResourceEntity.Values)}_MaxSize20",
                    $"cardinality(\"{nameof(SkillResourceEntity.Values)}\") <= 20"))
            .Property(s => s.Values)
            .HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.ProjectileSpeed).HasPrecision(10, 2);
        modelBuilder.Entity<SkillResourceEntity>().Property(s => s.ProjectileAcceleration).HasPrecision(10, 2);
    }
    
    private static void ConfigureStateResources(ModelBuilder modelBuilder)
    {       
        modelBuilder.Entity<StateResourceEntity>().Property(i => i.AmplifyBase).HasPrecision(13, 3);
        modelBuilder.Entity<StateResourceEntity>().Property(i => i.AmplifyPerSkill).HasPrecision(13, 3);
        modelBuilder.Entity<StateResourceEntity>()
            .HasOne(s => s.Tooltip)
            .WithMany(s => s.StateTooltips)
            .HasForeignKey(s => s.TooltipId);
        
        modelBuilder.Entity<StateResourceEntity>()
            .HasOne(s => s.Text)
            .WithMany(s => s.StateTexts)
            .HasForeignKey(s => s.TextId);
        
        modelBuilder.Entity<StateResourceEntity>().ToTable(i => i
            .HasCheckConstraint(
                $"CK_{nameof(StateResourceEntity)}_{nameof(StateResourceEntity.Values)}_MaxSize20",
                $"cardinality(\"{nameof(StateResourceEntity.Values)}\") <= 20"))
            .Property(i => i.Values)
            .HasPrecision(13, 3);
        
        modelBuilder.Entity<StateResourceEntity>().Property(i => i.DuplicateGroup).HasMaxLength(3);
    }

    private static void ConfigureSummonResources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SummonResourceEntity>().Property(i => i.Size).HasPrecision(10, 2);
        modelBuilder.Entity<SummonResourceEntity>().Property(i => i.TargetFxSize).HasPrecision(10, 2);
        modelBuilder.Entity<SummonResourceEntity>().Property(i => i.Scale).HasPrecision(10, 2);
        modelBuilder.Entity<SummonResourceEntity>().Property(i => i.AttackRange).HasPrecision(10, 2);
        modelBuilder.Entity<SummonResourceEntity>().Property(i => i.TargetPosition).HasPrecision(10, 2);
        
        modelBuilder.Entity<SummonResourceEntity>()
            .HasOne(s => s.Model)
            .WithMany(m => m.SummonModels)
            .HasForeignKey(s => s.ModelId);

        modelBuilder.Entity<SummonResourceEntity>()
            .HasMany(summon => summon.Skills)
            .WithOne(skill => skill.Summon);
        
        // modelBuilder.Entity<SummonResourceEntity>()
        //     .HasMany(s => s.SkillTexts)
        //     .WithMany(s => s.SummonSkillTexts);
        
        modelBuilder.Entity<SummonResourceEntity>()
            .HasOne(s => s.EvolveTarget)
            .WithOne(s => s.EvolveSource)
            .HasForeignKey<SummonResourceEntity>(s => s.EvolveTargetId);

        modelBuilder.Entity<SummonResourceEntity>()
            .HasOne(s => s.Stat)
            .WithMany(s => s.Summons)
            .HasForeignKey(s => s.StatId);
        
        modelBuilder.Entity<SummonResourceEntity>()
            .HasOne(s => s.Name)
            .WithMany(s => s.SummonNames)
            .HasForeignKey(s => s.NameId);
        
        modelBuilder.Entity<SummonResourceEntity>()
            .HasMany(s => s.Items)
            .WithOne(i => i.Summon)
            .HasForeignKey(i => i.SummonId);
        
        modelBuilder.Entity<SummonResourceEntity>()
            .HasOne(s => s.Card)
            .WithMany(i => i.Cards)
            .HasForeignKey(s => s.CardId);

        modelBuilder.Entity<SummonResourceEntity>().ToTable(i => i
            .HasCheckConstraint(
                $"CK_{nameof(SummonResourceEntity)}_{nameof(SummonResourceEntity.CameraPosition)}_MaxSize3",
                $"cardinality(\"{nameof(SummonResourceEntity.CameraPosition)}\") <= 3"));
        
        modelBuilder.Entity<SummonResourceEntity>().ToTable(i => i
            .HasCheckConstraint(
                $"CK_{nameof(SummonResourceEntity)}_{nameof(SummonResourceEntity.CameraPosition)}_MaxSize3",
                $"cardinality(\"{nameof(SummonResourceEntity.CameraPosition)}\") <= 3"));
    }
    
    private static void ConfigureEffectResources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ItemEffectResourceEntity>().ToTable(i => i
                    .HasCheckConstraint(
                        $"CK_{nameof(ItemEffectResourceEntity)}_{nameof(ItemEffectResourceEntity.Values)}_MaxSize20", 
                        $"cardinality(\"{nameof(ItemEffectResourceEntity.Values)}\") <= 20"))
            .Property(i => i.Values)
            .HasPrecision(12, 2);
        
        modelBuilder.Entity<ItemEffectResourceEntity>()
            .HasKey(i => new { i.Id, i.OrdinalId });

        modelBuilder.Entity<ItemEffectResourceEntity>()
            .HasOne(i => i.Tooltip)
            .WithMany(s => s.ItemEffectToolTips)
            .HasForeignKey(i => i.TooltipId);
        
    }
}