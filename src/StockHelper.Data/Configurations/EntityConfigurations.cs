using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockHelper.Core.Entities;

namespace StockHelper.Data.Configurations;

internal static class ConfigurationExtensions
{
    public const int NameLength = 200;
    public const int NoteLength = 1000;

    public static void ConfigureAudit<T>(this EntityTypeBuilder<T> b) where T : AuditableEntity
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired();
        b.Property(e => e.UpdatedBy).HasMaxLength(100);
        b.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
    }

    public static void ConfigureLookup<T>(this EntityTypeBuilder<T> b) where T : LookupEntity
    {
        b.ConfigureAudit();
        b.Property(e => e.Name).HasMaxLength(NameLength).IsRequired();
        b.Property(e => e.NormalizedName).HasMaxLength(NameLength).IsRequired();
        b.HasIndex(e => e.NormalizedName).IsUnique();
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b) => b.ConfigureLookup();
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.ConfigureLookup();
        b.Property(e => e.Name).HasMaxLength(50);
        b.Property(e => e.Factor).HasDefaultValue(1m);
        b.Ignore(e => e.IsPackage);
        b.HasOne(e => e.BaseUnit).WithMany().HasForeignKey(e => e.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StorageLocationConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> b)
    {
        b.ConfigureLookup();
        b.Property(e => e.Description).HasMaxLength(ConfigurationExtensions.NoteLength);
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ConfigureAudit();
        b.Property(e => e.Name).HasMaxLength(ConfigurationExtensions.NameLength).IsRequired();
        b.Property(e => e.NormalizedName).HasMaxLength(ConfigurationExtensions.NameLength).IsRequired();
        b.HasIndex(e => e.NormalizedName).IsUnique();
        b.Property(e => e.Code).HasMaxLength(100);
        b.Property(e => e.Note).HasMaxLength(ConfigurationExtensions.NoteLength);
        b.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.Unit).WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.ConfigureAudit();
        b.Ignore(e => e.Amount);
        b.Property(e => e.Note).HasMaxLength(ConfigurationExtensions.NoteLength);
        b.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.Unit).WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.StorageLocation).WithMany().HasForeignKey(e => e.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Date);
        b.HasIndex(e => new { e.ItemId, e.Date });
    }
}

internal sealed class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public void Configure(EntityTypeBuilder<Issue> b)
    {
        b.ConfigureAudit();
        b.Ignore(e => e.IsReturned);
        b.Ignore(e => e.IsOnHand);
        b.Ignore(e => e.NetQuantity);
        b.Property(e => e.IssuedTo).HasMaxLength(ConfigurationExtensions.NameLength);
        b.Property(e => e.Note).HasMaxLength(ConfigurationExtensions.NoteLength);
        b.Property(e => e.ReturnedBy).HasMaxLength(100);
        b.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.Unit).WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.StorageLocation).WithMany().HasForeignKey(e => e.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Date);
        b.HasIndex(e => new { e.ItemId, e.Date });
        b.HasIndex(e => e.ReturnedAt);
    }
}

internal sealed class StockTakeConfiguration : IEntityTypeConfiguration<StockTake>
{
    public void Configure(EntityTypeBuilder<StockTake> b)
    {
        b.ConfigureAudit();
        b.Property(e => e.Note).HasMaxLength(ConfigurationExtensions.NoteLength);
        b.Property(e => e.CompletedBy).HasMaxLength(100);
        b.Property(e => e.Status).HasConversion<int>();
        b.HasMany(e => e.Lines).WithOne(l => l.StockTake).HasForeignKey(l => l.StockTakeId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => e.Date);
    }
}

internal sealed class StockTakeLineConfiguration : IEntityTypeConfiguration<StockTakeLine>
{
    public void Configure(EntityTypeBuilder<StockTakeLine> b)
    {
        b.ConfigureAudit();
        b.HasOne(e => e.Item).WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.StorageLocation).WithMany().HasForeignKey(e => e.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.StockTakeId, e.ItemId, e.StorageLocationId }).IsUnique();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ConfigureAudit();
        b.Property(e => e.Login).HasMaxLength(100).IsRequired();
        b.Property(e => e.NormalizedLogin).HasMaxLength(100).IsRequired();
        b.HasIndex(e => e.NormalizedLogin).IsUnique();
        b.Property(e => e.DisplayName).HasMaxLength(ConfigurationExtensions.NameLength).IsRequired();
        b.Property(e => e.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(e => e.Role).HasConversion<int>();
    }
}
