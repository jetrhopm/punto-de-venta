using Microsoft.EntityFrameworkCore;

namespace Pos.Infrastructure;

public sealed class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<RegisterRecord> Registers => Set<RegisterRecord>();
    public DbSet<ProductRecord> Products => Set<ProductRecord>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pos");
        modelBuilder.Entity<StoreRecord>(entity =>
        {
            entity.ToTable("store");
            entity.HasKey(store => store.Id);
            entity.Property(store => store.Name).HasMaxLength(160).IsRequired();
            entity.Property(store => store.BusinessType).HasMaxLength(80).IsRequired();
            entity.Property(store => store.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(store => store.CreatedAtUtc).HasColumnType("timestamp with time zone");
        });
        modelBuilder.Entity<UserRecord>(entity =>
        {
            entity.ToTable("user_account");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.NormalizedUserName).HasMaxLength(80).IsRequired();
            entity.HasIndex(user => user.NormalizedUserName).IsUnique();
            entity.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(user => user.CreatedAtUtc).HasColumnType("timestamp with time zone");
        });
        modelBuilder.Entity<RegisterRecord>(entity =>
        {
            entity.ToTable("register");
            entity.HasKey(register => register.Id);
            entity.Property(register => register.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(register => new { register.StoreId, register.Name }).IsUnique();
            entity.HasOne<StoreRecord>().WithMany().HasForeignKey(register => register.StoreId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProductRecord>(entity =>
        {
            entity.ToTable("product");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Code).HasMaxLength(80).IsRequired();
            entity.Property(product => product.NormalizedCode).HasMaxLength(80).IsRequired();
            entity.Property(product => product.Description).HasMaxLength(200).IsRequired();
            entity.Property(product => product.Price).HasPrecision(18, 2);
            entity.HasIndex(product => product.NormalizedCode).IsUnique();
        });
        modelBuilder.Entity<SessionRecord>(entity =>
        {
            entity.ToTable("session");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.Property(session => session.ExpiresAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<UserRecord>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class StoreRecord { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string BusinessType { get; set; } = string.Empty; public string TimeZoneId { get; set; } = "America/Mexico_City"; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class UserRecord { public Guid Id { get; set; } public string NormalizedUserName { get; set; } = string.Empty; public string PasswordHash { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public bool IsAdministrator { get; set; } public bool IsActive { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class RegisterRecord { public Guid Id { get; set; } public Guid StoreId { get; set; } public string Name { get; set; } = string.Empty; public bool IsActive { get; set; } }
public sealed class ProductRecord { public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string NormalizedCode { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public decimal Price { get; set; } public bool IsActive { get; set; } }
public sealed class SessionRecord { public Guid Id { get; set; } public Guid UserId { get; set; } public string TokenHash { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset ExpiresAtUtc { get; set; } public DateTimeOffset? RevokedAtUtc { get; set; } }
