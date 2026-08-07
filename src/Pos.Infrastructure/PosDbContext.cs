using Microsoft.EntityFrameworkCore;

namespace Pos.Infrastructure;

public sealed class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<RegisterRecord> Registers => Set<RegisterRecord>();
    public DbSet<ProductRecord> Products => Set<ProductRecord>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<ShiftRecord> Shifts => Set<ShiftRecord>();
    public DbSet<SaleRecord> Sales => Set<SaleRecord>();
    public DbSet<SaleLineRecord> SaleLines => Set<SaleLineRecord>();
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    public DbSet<InventoryMovementRecord> InventoryMovements => Set<InventoryMovementRecord>();
    public DbSet<CashMovementRecord> CashMovements => Set<CashMovementRecord>();
    public DbSet<PrintJobRecord> PrintJobs => Set<PrintJobRecord>();
    public DbSet<CustomerRecord> Customers => Set<CustomerRecord>();
    public DbSet<CreditTransactionRecord> CreditTransactions => Set<CreditTransactionRecord>();
    public DbSet<SupplierRecord> Suppliers => Set<SupplierRecord>();
    public DbSet<PurchaseRecord> Purchases => Set<PurchaseRecord>();
    public DbSet<PurchaseLineRecord> PurchaseLines => Set<PurchaseLineRecord>();
    public DbSet<SaleReversalRecord> SaleReversals => Set<SaleReversalRecord>();
    public DbSet<ReturnRecord> Returns => Set<ReturnRecord>();
    public DbSet<ReturnLineRecord> ReturnLines => Set<ReturnLineRecord>();

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
        modelBuilder.Entity<PermissionRecord>(entity =>
        {
            entity.ToTable("permission_assignment");
            entity.HasKey(permission => permission.Id);
            entity.Property(permission => permission.Code).HasMaxLength(80).IsRequired();
            entity.HasIndex(permission => new { permission.UserId, permission.Code }).IsUnique();
            entity.HasOne<UserRecord>().WithMany().HasForeignKey(permission => permission.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ShiftRecord>(entity =>
        {
            entity.ToTable("shift");
            entity.HasKey(shift => shift.Id);
            entity.Property(shift => shift.InitialCash).HasPrecision(18, 2);
            entity.Property(shift => shift.Status).HasMaxLength(20).IsRequired();
            entity.Property(shift => shift.OpenedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(shift => new { shift.RegisterId, shift.Status }).IsUnique();
        });
        modelBuilder.Entity<SaleRecord>(entity =>
        {
            entity.ToTable("sale"); entity.HasKey(sale => sale.Id);
            entity.Property(sale => sale.OperationId).IsRequired(); entity.HasIndex(sale => sale.OperationId).IsUnique();
            entity.Property(sale => sale.Total).HasPrecision(18, 2); entity.Property(sale => sale.Status).HasMaxLength(20).IsRequired(); entity.HasIndex(sale => sale.CustomerId); entity.HasOne<CustomerRecord>().WithMany().HasForeignKey(sale => sale.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(sale => sale.CreatedAtUtc).HasColumnType("timestamp with time zone");
        });
        modelBuilder.Entity<SaleLineRecord>(entity =>
        {
            entity.ToTable("sale_line"); entity.HasKey(line => line.Id); entity.Property(line => line.Quantity).HasPrecision(18, 3); entity.Property(line => line.UnitPrice).HasPrecision(18, 2); entity.Property(line => line.LineTotal).HasPrecision(18, 2); entity.Property(line => line.StockBefore).HasPrecision(18, 3); entity.Property(line => line.StockAfter).HasPrecision(18, 3);
            entity.HasOne<SaleRecord>().WithMany().HasForeignKey(line => line.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PaymentRecord>(entity =>
        {
            entity.ToTable("payment"); entity.HasKey(payment => payment.Id); entity.Property(payment => payment.Amount).HasPrecision(18, 2); entity.Property(payment => payment.Received).HasPrecision(18, 2); entity.Property(payment => payment.Change).HasPrecision(18, 2); entity.Property(payment => payment.Method).HasMaxLength(20).IsRequired();
            entity.HasOne<SaleRecord>().WithMany().HasForeignKey(payment => payment.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<InventoryMovementRecord>(entity =>
        {
            entity.ToTable("inventory_movement"); entity.HasKey(movement => movement.Id); entity.Property(movement => movement.Quantity).HasPrecision(18, 3); entity.Property(movement => movement.StockBefore).HasPrecision(18, 3); entity.Property(movement => movement.StockAfter).HasPrecision(18, 3); entity.Property(movement => movement.Reason).HasMaxLength(80).IsRequired(); entity.Property(movement => movement.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<ProductRecord>().WithMany().HasForeignKey(movement => movement.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CashMovementRecord>(entity =>
        {
            entity.ToTable("cash_movement"); entity.HasKey(movement => movement.Id); entity.Property(movement => movement.Amount).HasPrecision(18, 2); entity.Property(movement => movement.Type).HasMaxLength(10).IsRequired(); entity.Property(movement => movement.Reason).HasMaxLength(160).IsRequired(); entity.Property(movement => movement.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<ShiftRecord>().WithMany().HasForeignKey(movement => movement.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PrintJobRecord>(entity =>
        {
            entity.ToTable("print_job"); entity.HasKey(job => job.Id); entity.Property(job => job.Status).HasMaxLength(20).IsRequired(); entity.Property(job => job.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.Property(job => job.CompletedAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(job => new { job.SaleId, job.Status }); entity.HasOne<SaleRecord>().WithMany().HasForeignKey(job => job.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CustomerRecord>(entity =>
        {
            entity.ToTable("customer"); entity.HasKey(customer => customer.Id); entity.Property(customer => customer.Name).HasMaxLength(160).IsRequired(); entity.Property(customer => customer.Phone).HasMaxLength(40); entity.Property(customer => customer.Email).HasMaxLength(160); entity.Property(customer => customer.TaxId).HasMaxLength(20); entity.Property(customer => customer.CreditLimit).HasPrecision(18, 2); entity.Property(customer => customer.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(customer => customer.Name);
        });
        modelBuilder.Entity<CreditTransactionRecord>(entity =>
        {
            entity.ToTable("credit_transaction"); entity.HasKey(item => item.Id); entity.Property(item => item.Type).HasMaxLength(20).IsRequired(); entity.Property(item => item.Amount).HasPrecision(18, 2); entity.Property(item => item.BalanceBefore).HasPrecision(18, 2); entity.Property(item => item.BalanceAfter).HasPrecision(18, 2); entity.Property(item => item.Reason).HasMaxLength(200).IsRequired(); entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(item => item.OperationId).IsUnique(); entity.HasOne<CustomerRecord>().WithMany().HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<SaleRecord>().WithMany().HasForeignKey(item => item.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SupplierRecord>(entity =>
        {
            entity.ToTable("supplier"); entity.HasKey(item => item.Id); entity.Property(item => item.Name).HasMaxLength(160).IsRequired(); entity.Property(item => item.Phone).HasMaxLength(40); entity.Property(item => item.Email).HasMaxLength(160); entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(item => item.Name);
        });
        modelBuilder.Entity<PurchaseRecord>(entity =>
        {
            entity.ToTable("purchase"); entity.HasKey(item => item.Id); entity.Property(item => item.OperationId).IsRequired(); entity.HasIndex(item => item.OperationId).IsUnique(); entity.Property(item => item.Total).HasPrecision(18, 2); entity.Property(item => item.Status).HasMaxLength(20).IsRequired(); entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.HasOne<SupplierRecord>().WithMany().HasForeignKey(item => item.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseLineRecord>(entity =>
        {
            entity.ToTable("purchase_line"); entity.HasKey(item => item.Id); entity.Property(item => item.Quantity).HasPrecision(18, 3); entity.Property(item => item.UnitCost).HasPrecision(18, 2); entity.Property(item => item.LineTotal).HasPrecision(18, 2); entity.HasOne<PurchaseRecord>().WithMany().HasForeignKey(item => item.PurchaseId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<ProductRecord>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SaleReversalRecord>(entity =>
        {
            entity.ToTable("sale_reversal"); entity.HasKey(item => item.Id); entity.Property(item => item.Reason).HasMaxLength(200).IsRequired(); entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(item => item.OperationId).IsUnique(); entity.HasIndex(item => item.SaleId).IsUnique(); entity.HasOne<SaleRecord>().WithMany().HasForeignKey(item => item.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ReturnRecord>(entity =>
        {
            entity.ToTable("sale_return"); entity.HasKey(item => item.Id); entity.Property(item => item.Reason).HasMaxLength(200).IsRequired(); entity.Property(item => item.Amount).HasPrecision(18, 2); entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(item => item.OperationId).IsUnique(); entity.HasOne<SaleRecord>().WithMany().HasForeignKey(item => item.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ReturnLineRecord>(entity =>
        {
            entity.ToTable("sale_return_line"); entity.HasKey(item => item.Id); entity.Property(item => item.Quantity).HasPrecision(18, 3); entity.Property(item => item.UnitPrice).HasPrecision(18, 2); entity.Property(item => item.Amount).HasPrecision(18, 2); entity.HasOne<ReturnRecord>().WithMany().HasForeignKey(item => item.ReturnId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<ProductRecord>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

public sealed class StoreRecord { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string BusinessType { get; set; } = string.Empty; public string TimeZoneId { get; set; } = "America/Mexico_City"; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class UserRecord { public Guid Id { get; set; } public string NormalizedUserName { get; set; } = string.Empty; public string PasswordHash { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public bool IsAdministrator { get; set; } public bool IsActive { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class RegisterRecord { public Guid Id { get; set; } public Guid StoreId { get; set; } public string Name { get; set; } = string.Empty; public bool IsActive { get; set; } }
public sealed class ProductRecord { public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string NormalizedCode { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public decimal Price { get; set; } public decimal Cost { get; set; } public decimal Stock { get; set; } public bool IsActive { get; set; } }
public sealed class SessionRecord { public Guid Id { get; set; } public Guid UserId { get; set; } public string TokenHash { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset ExpiresAtUtc { get; set; } public DateTimeOffset? RevokedAtUtc { get; set; } }
public sealed class PermissionRecord { public Guid Id { get; set; } public Guid UserId { get; set; } public string Code { get; set; } = string.Empty; }
public sealed class ShiftRecord { public Guid Id { get; set; } public Guid RegisterId { get; set; } public Guid UserId { get; set; } public decimal InitialCash { get; set; } public string Status { get; set; } = "Open"; public DateTimeOffset OpenedAtUtc { get; set; } public DateTimeOffset? ClosedAtUtc { get; set; } public decimal? CountedCash { get; set; } public decimal? Difference { get; set; } }
public sealed class SaleRecord { public Guid Id { get; set; } public Guid OperationId { get; set; } public Guid ShiftId { get; set; } public Guid? CustomerId { get; set; } public decimal Total { get; set; } public string Status { get; set; } = "Completed"; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class SaleLineRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal LineTotal { get; set; } public decimal StockBefore { get; set; } public decimal StockAfter { get; set; } }
public sealed class PaymentRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public string Method { get; set; } = "Cash"; public decimal Amount { get; set; } public decimal Received { get; set; } public decimal Change { get; set; } }
public sealed class InventoryMovementRecord { public Guid Id { get; set; } public Guid ProductId { get; set; } public Guid? SaleId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public decimal Quantity { get; set; } public decimal StockBefore { get; set; } public decimal StockAfter { get; set; } public string Reason { get; set; } = "Sale"; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class CashMovementRecord { public Guid Id { get; set; } public Guid ShiftId { get; set; } public string Type { get; set; } = "In"; public decimal Amount { get; set; } public string Reason { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class PrintJobRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public string Status { get; set; } = "Pending"; public int Attempts { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset? CompletedAtUtc { get; set; } }
public sealed class CustomerRecord { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string? Phone { get; set; } public string? Email { get; set; } public string? TaxId { get; set; } public decimal CreditLimit { get; set; } public bool CreditEnabled { get; set; } public bool IsActive { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class CreditTransactionRecord { public Guid Id { get; set; } public Guid CustomerId { get; set; } public Guid? SaleId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public string Type { get; set; } = string.Empty; public decimal Amount { get; set; } public decimal BalanceBefore { get; set; } public decimal BalanceAfter { get; set; } public string Reason { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class SupplierRecord { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string? Phone { get; set; } public string? Email { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class PurchaseRecord { public Guid Id { get; set; } public Guid OperationId { get; set; } public Guid SupplierId { get; set; } public Guid UserId { get; set; } public decimal Total { get; set; } public string Status { get; set; } = "Received"; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class PurchaseLineRecord { public Guid Id { get; set; } public Guid PurchaseId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitCost { get; set; } public decimal LineTotal { get; set; } }
public sealed class SaleReversalRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public string Reason { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class ReturnRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public decimal Amount { get; set; } public string Reason { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class ReturnLineRecord { public Guid Id { get; set; } public Guid ReturnId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal Amount { get; set; } }
