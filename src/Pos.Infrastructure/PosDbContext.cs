using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pos.Infrastructure;

public sealed class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<RegisterRecord> Registers => Set<RegisterRecord>();
    public DbSet<ProductRecord> Products => Set<ProductRecord>();
    public DbSet<DepartmentRecord> Departments => Set<DepartmentRecord>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<ShiftRecord> Shifts => Set<ShiftRecord>();
    public DbSet<SaleRecord> Sales => Set<SaleRecord>();
    public DbSet<SaleLineRecord> SaleLines => Set<SaleLineRecord>();
    public DbSet<SaleDraftRecord> SaleDrafts => Set<SaleDraftRecord>();
    public DbSet<SaleDraftLineRecord> SaleDraftLines => Set<SaleDraftLineRecord>();
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    public DbSet<InventoryMovementRecord> InventoryMovements => Set<InventoryMovementRecord>();
    public DbSet<InventoryLimitChangeRecord> InventoryLimitChanges => Set<InventoryLimitChangeRecord>();
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
    public DbSet<PromotionRecord> Promotions => Set<PromotionRecord>();
    public DbSet<KitComponentRecord> KitComponents => Set<KitComponentRecord>();
    public DbSet<DeviceRecord> Devices => Set<DeviceRecord>();
    public DbSet<PairingCodeRecord> PairingCodes => Set<PairingCodeRecord>();
    public DbSet<ImportBatchRecord> ImportBatches => Set<ImportBatchRecord>();
    public DbSet<MercadoPagoOrderRecord> MercadoPagoOrders => Set<MercadoPagoOrderRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pos");
        modelBuilder.Entity<StoreRecord>(entity =>
        {
            entity.ToTable("store");
            entity.HasKey(store => store.Id);
            entity.Property(store => store.Name).HasMaxLength(160).IsRequired();
            entity.Property(store => store.BusinessType).HasMaxLength(80).IsRequired();
            entity.Property(store => store.LegalName).HasMaxLength(200);
            entity.Property(store => store.TaxId).HasMaxLength(20);
            entity.Property(store => store.Address).HasMaxLength(300);
            entity.Property(store => store.Phone).HasMaxLength(30);
            entity.Property(store => store.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.Property(store => store.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.Property(store => store.TicketHeader).HasMaxLength(300); entity.Property(store => store.TicketFooter).HasMaxLength(300);
            entity.Property(store => store.NextSaleFolio).HasDefaultValue(1L);
            entity.Property(store => store.DefaultWeightUnit).HasMaxLength(20).HasDefaultValue("Kilogramo");
            entity.Property(store => store.CurrencySymbol).HasMaxLength(5).HasDefaultValue("$");
            entity.Property(store => store.CashPaymentEnabled).HasDefaultValue(true);
            entity.Property(store => store.CardPaymentEnabled).HasDefaultValue(true);
            entity.Property(store => store.TransferPaymentEnabled).HasDefaultValue(true);
            entity.Property(store => store.CreditPaymentEnabled).HasDefaultValue(true);
            entity.Property(store => store.RequireCashCountOnClose).HasDefaultValue(true);
            entity.Property(store => store.AutoAdjustCashDifference).HasDefaultValue(true);
            entity.Property(store => store.CashLimitEnabled).HasDefaultValue(false);
            entity.Property(store => store.CashLimit).HasPrecision(18, 2).HasDefaultValue(0m);
            entity.Property(store => store.CashLimitMessage).HasMaxLength(300).HasDefaultValue("Realiza un retiro de efectivo (F8); se superó el límite permitido en caja.");
            entity.Property(store => store.InventoryEnabled).HasDefaultValue(true);
            entity.Property(store => store.InventoryCostMethod).HasMaxLength(30).HasDefaultValue("WeightedAverage");
            entity.Property(store => store.CreditSalesEnabled).HasDefaultValue(true);
            entity.Property(store => store.CommonProductsEnabled).HasDefaultValue(true);
            entity.Property(store => store.AutoPriceWithProfit).HasDefaultValue(true);
            entity.Property(store => store.DefaultProfitPercent).HasPrecision(5, 2).HasDefaultValue(20m);
            entity.Property(store => store.RoundSaleAmounts).HasDefaultValue(false);
            entity.Property(store => store.RoundingMode).HasMaxLength(20).HasDefaultValue("Tenths");
            entity.Property(store => store.OccasionalNotice).HasMaxLength(300).HasDefaultValue("");
            entity.Property(store => store.OccasionalNoticeEverySales).HasDefaultValue(5);
            entity.Property(store => store.CashDrawerEnabled).HasDefaultValue(false);
            entity.Property(store => store.CashDrawerPrinterName).HasMaxLength(260).HasDefaultValue("");
            entity.Property(store => store.CashDrawerModel).HasMaxLength(80).HasDefaultValue("PrinterPulse");
            entity.Property(store => store.CashDrawerPort).HasMaxLength(20).HasDefaultValue("USB");
            entity.Property(store => store.ScaleEnabled).HasDefaultValue(false);
            entity.Property(store => store.ScalePort).HasMaxLength(20).HasDefaultValue("");
            entity.Property(store => store.ScaleBaudRate).HasDefaultValue(9600);
            entity.Property(store => store.ScaleParity).HasMaxLength(10).HasDefaultValue("None");
            entity.Property(store => store.ScaleDataBits).HasDefaultValue(8);
            entity.Property(store => store.ScaleStopBits).HasMaxLength(10).HasDefaultValue("One");
            entity.Property(store => store.ScaleTerminator).HasMaxLength(10).HasDefaultValue("CRLF");
            entity.Property(store => store.ScaleUnit).HasMaxLength(20).HasDefaultValue("Kilogramo");
            entity.Property(store => store.ScaleReadTimeoutMs).HasDefaultValue(1500);
            entity.Property(store => store.MercadoPagoEnabled).HasDefaultValue(false);
            entity.Property(store => store.MercadoPagoEnvironment).HasMaxLength(20).HasDefaultValue("Test");
            entity.Property(store => store.MercadoPagoAccessTokenProtected).HasMaxLength(4096).HasDefaultValue("");
            entity.Property(store => store.MercadoPagoRefreshTokenProtected).HasMaxLength(4096).HasDefaultValue("");
            entity.Property(store => store.MercadoPagoOAuthState).HasMaxLength(128).HasDefaultValue("");
            entity.Property(store => store.MercadoPagoOAuthVerifierProtected).HasMaxLength(4096).HasDefaultValue("");
            entity.Property(store => store.MercadoPagoOAuthStateExpiresAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(store => store.MercadoPagoTokenExpiresAtUtc).HasColumnType("timestamp with time zone");
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
            entity.Property(register => register.MercadoPagoTerminalId).HasMaxLength(180).HasDefaultValue("");
            entity.Property(register => register.MercadoPagoTerminalLabel).HasMaxLength(180).HasDefaultValue("");
        });
        modelBuilder.Entity<DeviceRecord>(entity =>
        {
            entity.ToTable("device"); entity.HasKey(item => item.Id); entity.Property(item => item.Name).HasMaxLength(120).IsRequired(); entity.Property(item => item.DeviceType).HasMaxLength(30).IsRequired(); entity.Property(item => item.DeviceTokenHash).HasMaxLength(64).IsRequired(); entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.Property(item => item.LastSeenAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(item => item.DeviceTokenHash).IsUnique(); entity.HasIndex(item => new { item.StoreId, item.Name }).IsUnique(); entity.HasOne<StoreRecord>().WithMany().HasForeignKey(item => item.StoreId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<RegisterRecord>().WithMany().HasForeignKey(item => item.RegisterId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PairingCodeRecord>(entity =>
        {
            entity.ToTable("pairing_code"); entity.HasKey(item => item.Id); entity.Property(item => item.CodeHash).HasMaxLength(64).IsRequired(); entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.Property(item => item.ExpiresAtUtc).HasColumnType("timestamp with time zone"); entity.Property(item => item.UsedAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(item => item.CodeHash).IsUnique(); entity.HasOne<StoreRecord>().WithMany().HasForeignKey(item => item.StoreId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<UserRecord>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MercadoPagoOrderRecord>(entity =>
        {
            entity.ToTable("mercado_pago_order"); entity.HasKey(item => item.Id); entity.HasIndex(item => item.OperationId).IsUnique(); entity.HasIndex(item => item.ProviderOrderId).IsUnique();
            entity.Property(item => item.ProviderOrderId).HasMaxLength(80); entity.Property(item => item.ProviderPaymentId).HasMaxLength(80); entity.Property(item => item.Status).HasMaxLength(30).IsRequired(); entity.Property(item => item.StatusDetail).HasMaxLength(120); entity.Property(item => item.Amount).HasPrecision(18, 2); entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone"); entity.Property(item => item.UpdatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<StoreRecord>().WithMany().HasForeignKey(item => item.StoreId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<RegisterRecord>().WithMany().HasForeignKey(item => item.RegisterId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProductRecord>(entity =>
        {
            entity.ToTable("product");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Code).HasMaxLength(80).IsRequired();
            entity.Property(product => product.NormalizedCode).HasMaxLength(80).IsRequired();
            entity.Property(product => product.Description).HasMaxLength(200).IsRequired();
            entity.Property(product => product.Category).HasMaxLength(100);
            entity.Property(product => product.ProfitPercent).HasPrecision(5, 2);
            entity.Property(product => product.WholesaleProfitPercent).HasPrecision(5, 2);
            entity.Property(product => product.UnitOfMeasure).HasMaxLength(30).HasDefaultValue("Pieza");
            entity.Property(product => product.Price).HasPrecision(18, 2);
            entity.Property(product => product.WholesalePrice).HasPrecision(18, 2);
            entity.Property(product => product.WholesaleMinimumQuantity).HasPrecision(18, 3);
            entity.Property(product => product.MinimumStock).HasPrecision(18, 3);
            entity.Property(product => product.MaximumStock).HasPrecision(18, 3);
            entity.Property(product => product.IsCommonProduct).HasDefaultValue(false);
            entity.HasIndex(product => product.NormalizedCode).IsUnique();
            entity.HasIndex(product => product.PrimarySupplierId);
            entity.HasOne<SupplierRecord>().WithMany().HasForeignKey(product => product.PrimarySupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(product => product.Department).WithMany().HasForeignKey(product => product.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DepartmentRecord>(entity =>
        {
            entity.ToTable("department");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(100).IsRequired();
            entity.Property(item => item.NormalizedName).HasMaxLength(100).IsRequired();
            entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(item => item.NormalizedName).IsUnique();
        });
        modelBuilder.Entity<ImportBatchRecord>(entity =>
        {
            entity.ToTable("import_batch");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.OperationId).IsUnique();
            entity.Property(item => item.SourceFileName).HasMaxLength(260).IsRequired();
            entity.Property(item => item.DuplicateRule).HasMaxLength(20).IsRequired();
            entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<UserRecord>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PromotionRecord>(entity =>
        {
            entity.ToTable("promotion"); entity.HasKey(item => item.Id); entity.Property(item => item.Name).HasMaxLength(120).IsRequired(); entity.Property(item => item.Percent).HasPrecision(5, 2); entity.Property(item => item.DiscountAmount).HasPrecision(18, 2); entity.Property(item => item.BuyQuantity).HasPrecision(18, 3); entity.Property(item => item.PayQuantity).HasPrecision(18, 3); entity.Property(item => item.StartsAtUtc).HasColumnType("timestamp with time zone"); entity.Property(item => item.EndsAtUtc).HasColumnType("timestamp with time zone"); entity.HasIndex(item => item.Name).IsUnique(); entity.HasOne<ProductRecord>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<KitComponentRecord>(entity =>
        {
            entity.ToTable("kit_component"); entity.HasKey(item => item.Id); entity.Property(item => item.Quantity).HasPrecision(18, 3); entity.HasIndex(item => new { item.KitProductId, item.ComponentProductId }).IsUnique(); entity.HasOne<ProductRecord>().WithMany().HasForeignKey(item => item.KitProductId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<ProductRecord>().WithMany().HasForeignKey(item => item.ComponentProductId).OnDelete(DeleteBehavior.Restrict);
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
            entity.HasIndex(shift => new { shift.RegisterId, shift.Status })
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'");
        });
        modelBuilder.Entity<SaleRecord>(entity =>
        {
            entity.ToTable("sale"); entity.HasKey(sale => sale.Id);
            entity.Property(sale => sale.OperationId).IsRequired(); entity.HasIndex(sale => sale.OperationId).IsUnique();
            entity.Property(sale => sale.Total).HasPrecision(18, 2); entity.Property(sale => sale.Status).HasMaxLength(20).IsRequired(); entity.HasIndex(sale => sale.Folio).IsUnique(); entity.HasIndex(sale => sale.CustomerId); entity.HasOne<CustomerRecord>().WithMany().HasForeignKey(sale => sale.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(sale => sale.CreatedAtUtc).HasColumnType("timestamp with time zone");
        });
        modelBuilder.Entity<SaleLineRecord>(entity =>
        {
            entity.ToTable("sale_line"); entity.HasKey(line => line.Id); entity.Property(line => line.Quantity).HasPrecision(18, 3); entity.Property(line => line.UnitPrice).HasPrecision(18, 2); entity.Property(line => line.LineTotal).HasPrecision(18, 2); entity.Property(line => line.StockBefore).HasPrecision(18, 3); entity.Property(line => line.StockAfter).HasPrecision(18, 3);
            entity.HasOne<SaleRecord>().WithMany().HasForeignKey(line => line.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SaleDraftRecord>(entity =>
        {
            entity.ToTable("sale_draft");
            entity.HasKey(draft => draft.Id);
            entity.Property(draft => draft.Status).HasMaxLength(20).IsRequired();
            entity.Property(draft => draft.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(draft => draft.UpdatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(draft => draft.CompletedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(draft => draft.OperationId).IsUnique();
            entity.HasIndex(draft => new { draft.ShiftId, draft.TicketNumber }).IsUnique();
            entity.HasIndex(draft => new { draft.ShiftId, draft.Status });
            entity.HasOne<ShiftRecord>().WithMany().HasForeignKey(draft => draft.ShiftId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserRecord>().WithMany().HasForeignKey(draft => draft.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SaleDraftLineRecord>(entity =>
        {
            entity.ToTable("sale_draft_line");
            entity.HasKey(line => line.Id);
            entity.Property(line => line.Code).HasMaxLength(80).IsRequired();
            entity.Property(line => line.Description).HasMaxLength(200).IsRequired();
            entity.Property(line => line.Quantity).HasPrecision(18, 3);
            entity.Property(line => line.UnitPrice).HasPrecision(18, 2);
            entity.HasIndex(line => new { line.DraftId, line.ProductId }).IsUnique();
            entity.HasOne<SaleDraftRecord>().WithMany(draft => draft.Lines).HasForeignKey(line => line.DraftId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductRecord>().WithMany().HasForeignKey(line => line.ProductId).OnDelete(DeleteBehavior.Restrict);
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
        modelBuilder.Entity<InventoryLimitChangeRecord>(entity =>
        {
            entity.ToTable("inventory_limit_change"); entity.HasKey(item => item.Id); entity.Property(item => item.PreviousMinimumStock).HasPrecision(18, 3); entity.Property(item => item.PreviousMaximumStock).HasPrecision(18, 3); entity.Property(item => item.MinimumStock).HasPrecision(18, 3); entity.Property(item => item.MaximumStock).HasPrecision(18, 3); entity.HasIndex(item => item.OperationId).IsUnique(); entity.HasOne<ProductRecord>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict); entity.HasOne<UserRecord>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
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

public sealed class StoreRecord { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string BusinessType { get; set; } = string.Empty; public string LegalName { get; set; } = string.Empty; public string TaxId { get; set; } = string.Empty; public string Address { get; set; } = string.Empty; public string Phone { get; set; } = string.Empty; public string TimeZoneId { get; set; } = "America/Mexico_City"; public string TicketHeader { get; set; } = string.Empty; public string TicketFooter { get; set; } = "Gracias por su compra"; public int TicketWidthMm { get; set; } = 80; public long NextSaleFolio { get; set; } = 1; public string DefaultWeightUnit { get; set; } = "Kilogramo"; public string CurrencySymbol { get; set; } = "$"; public bool CashPaymentEnabled { get; set; } = true; public bool CardPaymentEnabled { get; set; } = true; public bool TransferPaymentEnabled { get; set; } = true; public bool CreditPaymentEnabled { get; set; } = true; public bool RequireCashCountOnClose { get; set; } = true; public bool AutoAdjustCashDifference { get; set; } = true; public bool CashLimitEnabled { get; set; } public decimal CashLimit { get; set; } public string CashLimitMessage { get; set; } = "Realiza un retiro de efectivo (F8); se superó el límite permitido en caja."; public bool InventoryEnabled { get; set; } = true; public string InventoryCostMethod { get; set; } = "WeightedAverage"; public bool CreditSalesEnabled { get; set; } = true; public bool CommonProductsEnabled { get; set; } = true; public bool AutoPriceWithProfit { get; set; } = true; public decimal DefaultProfitPercent { get; set; } = 20m; public bool RoundSaleAmounts { get; set; } public string RoundingMode { get; set; } = "Tenths"; public string OccasionalNotice { get; set; } = string.Empty; public int OccasionalNoticeEverySales { get; set; } = 5; public bool CashDrawerEnabled { get; set; } public string CashDrawerPrinterName { get; set; } = string.Empty; public string CashDrawerModel { get; set; } = "PrinterPulse"; public string CashDrawerPort { get; set; } = "USB"; public bool ScaleEnabled { get; set; } public string ScalePort { get; set; } = string.Empty; public int ScaleBaudRate { get; set; } = 9600; public string ScaleParity { get; set; } = "None"; public int ScaleDataBits { get; set; } = 8; public string ScaleStopBits { get; set; } = "One"; public string ScaleTerminator { get; set; } = "CRLF"; public string ScaleUnit { get; set; } = "Kilogramo"; public int ScaleReadTimeoutMs { get; set; } = 1500; public bool MercadoPagoEnabled { get; set; } public string MercadoPagoEnvironment { get; set; } = "Test"; public string MercadoPagoAccessTokenProtected { get; set; } = string.Empty; public string MercadoPagoRefreshTokenProtected { get; set; } = string.Empty; public long? MercadoPagoUserId { get; set; } public DateTimeOffset? MercadoPagoTokenExpiresAtUtc { get; set; } public string MercadoPagoOAuthState { get; set; } = string.Empty; public string MercadoPagoOAuthVerifierProtected { get; set; } = string.Empty; public DateTimeOffset? MercadoPagoOAuthStateExpiresAtUtc { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class UserRecord { public Guid Id { get; set; } public string NormalizedUserName { get; set; } = string.Empty; public string PasswordHash { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public bool IsAdministrator { get; set; } public bool IsActive { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class RegisterRecord { public Guid Id { get; set; } public Guid StoreId { get; set; } public string Name { get; set; } = string.Empty; public bool IsActive { get; set; } public string MercadoPagoTerminalId { get; set; } = string.Empty; public string MercadoPagoTerminalLabel { get; set; } = string.Empty; }
public sealed class MercadoPagoOrderRecord { public Guid Id { get; set; } public Guid StoreId { get; set; } public Guid RegisterId { get; set; } public Guid OperationId { get; set; } public string? ProviderOrderId { get; set; } public string? ProviderPaymentId { get; set; } public decimal Amount { get; set; } public string Status { get; set; } = "Pending"; public string StatusDetail { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset UpdatedAtUtc { get; set; } }
public sealed class DeviceRecord { public Guid Id { get; set; } public Guid StoreId { get; set; } public Guid RegisterId { get; set; } public string Name { get; set; } = string.Empty; public string DeviceType { get; set; } = "Register"; public string DeviceTokenHash { get; set; } = string.Empty; public bool IsActive { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset? LastSeenAtUtc { get; set; } }
public sealed class PairingCodeRecord { public Guid Id { get; set; } public Guid StoreId { get; set; } public Guid CreatedByUserId { get; set; } public string CodeHash { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset ExpiresAtUtc { get; set; } public DateTimeOffset? UsedAtUtc { get; set; } }
public sealed class ProductRecord { public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string NormalizedCode { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string Category { get; set; } = string.Empty; public Guid? DepartmentId { get; set; } public DepartmentRecord? Department { get; set; } public string UnitOfMeasure { get; set; } = "Pieza"; public decimal Price { get; set; } public decimal Cost { get; set; } public decimal ProfitPercent { get; set; } = 20m; public decimal WholesalePrice { get; set; } public decimal WholesaleProfitPercent { get; set; } public decimal WholesaleMinimumQuantity { get; set; } public decimal Stock { get; set; } public decimal MinimumStock { get; set; } public decimal MaximumStock { get; set; } public Guid? PrimarySupplierId { get; set; } public bool IsKit { get; set; } public bool IsCommonProduct { get; set; } public bool IsActive { get; set; } }
public sealed class DepartmentRecord { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string NormalizedName { get; set; } = string.Empty; public bool IsActive { get; set; } = true; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class ImportBatchRecord { public Guid Id { get; set; } public Guid OperationId { get; set; } public Guid UserId { get; set; } public string SourceFileName { get; set; } = string.Empty; public string DuplicateRule { get; set; } = "Skip"; public int CreatedCount { get; set; } public int UpdatedCount { get; set; } public int SkippedCount { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class PromotionRecord { public Guid Id { get; set; } public Guid ProductId { get; set; } public string Name { get; set; } = string.Empty; public decimal Percent { get; set; } public decimal DiscountAmount { get; set; } public decimal BuyQuantity { get; set; } public decimal PayQuantity { get; set; } public DateTimeOffset? StartsAtUtc { get; set; } public DateTimeOffset? EndsAtUtc { get; set; } public bool IsActive { get; set; } }
public sealed class KitComponentRecord { public Guid Id { get; set; } public Guid KitProductId { get; set; } public Guid ComponentProductId { get; set; } public decimal Quantity { get; set; } }
public sealed class SessionRecord { public Guid Id { get; set; } public Guid UserId { get; set; } public string TokenHash { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset ExpiresAtUtc { get; set; } public DateTimeOffset? RevokedAtUtc { get; set; } }
public sealed class PermissionRecord { public Guid Id { get; set; } public Guid UserId { get; set; } public string Code { get; set; } = string.Empty; }
public sealed class ShiftRecord { public Guid Id { get; set; } public Guid RegisterId { get; set; } public Guid UserId { get; set; } public decimal InitialCash { get; set; } public string Status { get; set; } = "Open"; public DateTimeOffset OpenedAtUtc { get; set; } public DateTimeOffset? ClosedAtUtc { get; set; } public decimal? CountedCash { get; set; } public decimal? Difference { get; set; } }
public sealed class SaleRecord { public Guid Id { get; set; } public Guid OperationId { get; set; } public Guid ShiftId { get; set; } public Guid? CustomerId { get; set; } public long Folio { get; set; } public decimal Total { get; set; } public string Status { get; set; } = "Completed"; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class SaleLineRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal LineTotal { get; set; } public decimal StockBefore { get; set; } public decimal StockAfter { get; set; } }
public sealed class SaleDraftRecord { public Guid Id { get; set; } public Guid OperationId { get; set; } public Guid ShiftId { get; set; } public Guid UserId { get; set; } public int TicketNumber { get; set; } public string Status { get; set; } = "Open"; public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset UpdatedAtUtc { get; set; } public DateTimeOffset? CompletedAtUtc { get; set; } public List<SaleDraftLineRecord> Lines { get; set; } = []; }
public sealed class SaleDraftLineRecord { public Guid Id { get; set; } public Guid DraftId { get; set; } public Guid ProductId { get; set; } public string Code { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } }
public sealed class PaymentRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public string Method { get; set; } = "Cash"; public decimal Amount { get; set; } public decimal Received { get; set; } public decimal Change { get; set; } }
public sealed class InventoryMovementRecord { public Guid Id { get; set; } public Guid ProductId { get; set; } public Guid? SaleId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public decimal Quantity { get; set; } public decimal StockBefore { get; set; } public decimal StockAfter { get; set; } public string Reason { get; set; } = "Sale"; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class InventoryLimitChangeRecord { public Guid Id { get; set; } public Guid ProductId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public decimal PreviousMinimumStock { get; set; } public decimal PreviousMaximumStock { get; set; } public decimal MinimumStock { get; set; } public decimal MaximumStock { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class CashMovementRecord { public Guid Id { get; set; } public Guid ShiftId { get; set; } public string Type { get; set; } = "In"; public decimal Amount { get; set; } public string Reason { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class PrintJobRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public bool PrintRequested { get; set; } = true; public string Status { get; set; } = "Pending"; public int Attempts { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } public DateTimeOffset? CompletedAtUtc { get; set; } }
public sealed class CustomerRecord { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string? Phone { get; set; } public string? Email { get; set; } public string? TaxId { get; set; } public decimal CreditLimit { get; set; } public bool CreditEnabled { get; set; } public bool IsActive { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class CreditTransactionRecord { public Guid Id { get; set; } public Guid CustomerId { get; set; } public Guid? SaleId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public string Type { get; set; } = string.Empty; public decimal Amount { get; set; } public decimal BalanceBefore { get; set; } public decimal BalanceAfter { get; set; } public string Reason { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class SupplierRecord { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string? Phone { get; set; } public string? Email { get; set; } public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class PurchaseRecord { public Guid Id { get; set; } public Guid OperationId { get; set; } public Guid SupplierId { get; set; } public Guid UserId { get; set; } public decimal Total { get; set; } public string Status { get; set; } = "Received"; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class PurchaseLineRecord { public Guid Id { get; set; } public Guid PurchaseId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitCost { get; set; } public decimal LineTotal { get; set; } }
public sealed class SaleReversalRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public string Reason { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class ReturnRecord { public Guid Id { get; set; } public Guid SaleId { get; set; } public Guid UserId { get; set; } public Guid OperationId { get; set; } public decimal Amount { get; set; } public string Reason { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } }
public sealed class ReturnLineRecord { public Guid Id { get; set; } public Guid ReturnId { get; set; } public Guid ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal Amount { get; set; } }
