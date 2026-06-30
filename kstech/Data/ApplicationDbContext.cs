using Microsoft.EntityFrameworkCore;
using kstech.Models.Entities;

using kstech.Services;

namespace kstech.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly ITenantContext? _tenantContext;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext? tenantContext = null)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Employee> Employees { get; set; } = null!;
        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderDetail> OrderDetails { get; set; } = null!;
        public DbSet<Payment> Payments { get; set; } = null!;
        public DbSet<TechnicalInquiry> TechnicalInquiries { get; set; } = null!;
        public DbSet<EmailNotification> EmailNotifications { get; set; } = null!;
        public DbSet<SystemLog> SystemLogs { get; set; } = null!;
        public DbSet<CartItem> CartItems { get; set; } = null!;
        public DbSet<MarketingCampaign> Campaigns { get; set; } = null!;
        public DbSet<InventoryMovement> InventoryMovements { get; set; } = null!;
        public DbSet<FinancialBudget> FinancialBudgets { get; set; } = null!;
        public DbSet<BudgetEvent> BudgetEvents { get; set; } = null!;
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
        public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;
        public DbSet<EmailOutbox> EmailOutbox { get; set; } = null!;
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; } = null!;
        public DbSet<CustomerTenantLoyalty> CustomerTenantLoyalties { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure decimal precision
            modelBuilder.Entity<Product>()
                .Property(p => p.CostPrice)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<Product>()
                .Property(p => p.SellingPrice)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<Product>()
                .Property(p => p.MarketPrice)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<Order>()
                .Property(o => o.TotalAmount)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<Order>()
                .Property(o => o.LoyaltyDiscountAmount)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<OrderDetail>()
                .Property(od => od.UnitPriceAtSale)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<OrderDetail>()
                .Property(od => od.SubTotal)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<Payment>()
                .Property(p => p.AmountPaid)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<InventoryMovement>()
                .Property(movement => movement.UnitCostAtMovement)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<FinancialBudget>()
                .Property(budget => budget.BudgetAmount)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<BudgetEvent>()
                .Property(budgetEvent => budgetEvent.Amount)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<BudgetEvent>()
                .Property(budgetEvent => budgetEvent.BeforeAmount)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<BudgetEvent>()
                .Property(budgetEvent => budgetEvent.AfterAmount)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<PurchaseOrder>()
                .Property(purchaseOrder => purchaseOrder.TotalAmount)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<PurchaseOrderLine>()
                .Property(line => line.UnitCost)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<PurchaseOrderLine>()
                .Property(line => line.LineTotal)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<FinancialBudget>()
                .Property(budget => budget.Status)
                .HasMaxLength(20);

            modelBuilder.Entity<BudgetEvent>()
                .Property(budgetEvent => budgetEvent.EventType)
                .HasMaxLength(30);

            modelBuilder.Entity<BudgetEvent>()
                .Property(budgetEvent => budgetEvent.Reason)
                .HasMaxLength(250);

            modelBuilder.Entity<BudgetEvent>()
                .Property(budgetEvent => budgetEvent.ReferenceType)
                .HasMaxLength(30);

            modelBuilder.Entity<BudgetEvent>()
                .Property(budgetEvent => budgetEvent.ReferenceId)
                .HasMaxLength(50);

            modelBuilder.Entity<PurchaseOrder>()
                .Property(purchaseOrder => purchaseOrder.Status)
                .HasMaxLength(20);

            modelBuilder.Entity<Customer>()
                .Property(customer => customer.MarketingOptIn)
                .HasDefaultValue(true);

            modelBuilder.Entity<InventoryMovement>()
                .HasIndex(movement => new { movement.ProductID, movement.OccurredAtUtc });

            modelBuilder.Entity<InventoryMovement>()
                .HasOne(movement => movement.PerformedByUser)
                .WithMany()
                .HasForeignKey(movement => movement.PerformedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PurchaseOrder>()
                .ToTable(table =>
                    table.HasCheckConstraint(
                        "CK_PurchaseOrders_Status",
                        "[Status] IN ('Draft','Approved','PartiallyReceived','Received','Cancelled')"));

            modelBuilder.Entity<PurchaseOrderLine>()
                .ToTable(table =>
                    table.HasCheckConstraint(
                        "CK_PurchaseOrderLines_Quantities",
                        "[QuantityOrdered] >= 0 AND [QuantityReceived] >= 0 AND [QuantityReceived] <= [QuantityOrdered]"));

            modelBuilder.Entity<User>()
                .HasIndex(user => new { user.Role, user.OwnerUserID });

            modelBuilder.Entity<CustomerTenantLoyalty>()
                .HasIndex(ctl => new { ctl.CustomerID, ctl.TenantOwnerUserID })
                .IsUnique();

            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(token => token.TokenHash)
                .IsUnique();

            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(token => new { token.UserID, token.Audience, token.CreatedAtUtc });

            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(token => new { token.Audience, token.ExpiresAtUtc, token.ConsumedAtUtc });

            modelBuilder.Entity<PasswordResetToken>()
                .HasOne(token => token.User)
                .WithMany(user => user.PasswordResetTokens)
                .HasForeignKey(token => token.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Employee>()
                .HasIndex(employee => employee.OwnerUserID);

            modelBuilder.Entity<Product>()
                .HasIndex(product => product.OwnerUserID);

            modelBuilder.Entity<Product>()
                .HasIndex(product => new { product.OwnerUserID, product.CategoryName });

            modelBuilder.Entity<Order>()
                .HasIndex(order => new { order.OwnerUserID, order.OrderDate });

            modelBuilder.Entity<Payment>()
                .HasIndex(payment => payment.OwnerUserID);

            modelBuilder.Entity<TechnicalInquiry>()
                .HasIndex(inquiry => inquiry.OwnerUserID);

            modelBuilder.Entity<SystemLog>()
                .HasIndex(log => new { log.OwnerUserID, log.Timestamp });

            modelBuilder.Entity<MarketingCampaign>()
                .HasIndex(campaign => campaign.OwnerUserID);

            modelBuilder.Entity<EmailNotification>()
                .HasIndex(notification => notification.OwnerUserID);

            modelBuilder.Entity<InventoryMovement>()
                .HasIndex(movement => new { movement.OwnerUserID, movement.OccurredAtUtc });

            modelBuilder.Entity<PurchaseOrder>()
                .HasIndex(purchaseOrder => new
                {
                    purchaseOrder.OwnerUserID,
                    purchaseOrder.PurchaseOrderNumber
                })
                .IsUnique();

            modelBuilder.Entity<PurchaseOrder>()
                .HasIndex(purchaseOrder => new
                {
                    purchaseOrder.OwnerUserID,
                    purchaseOrder.Status,
                    purchaseOrder.CreatedAtUtc
                });

            modelBuilder.Entity<PurchaseOrderLine>()
                .HasIndex(line => line.PurchaseOrderID);

            modelBuilder.Entity<PurchaseOrderLine>()
                .HasIndex(line => new { line.ProductID, line.PurchaseOrderID });

            modelBuilder.Entity<FinancialBudget>()
                .HasIndex(budget => new
                {
                    budget.OwnerUserID,
                    budget.PeriodStartDateLocal,
                    budget.PeriodEndDateLocal
                })
                .IsUnique();

            modelBuilder.Entity<FinancialBudget>()
                .HasIndex(budget => new
                {
                    budget.OwnerUserID,
                    budget.Status,
                    budget.UpdatedAtUtc
                });

            modelBuilder.Entity<BudgetEvent>()
                .HasIndex(budgetEvent => new
                {
                    budgetEvent.OwnerUserID,
                    budgetEvent.BudgetID,
                    budgetEvent.OccurredAtUtc
                });

            modelBuilder.Entity<BudgetEvent>()
                .HasIndex(budgetEvent => new
                {
                    budgetEvent.BudgetID,
                    budgetEvent.EventType,
                    budgetEvent.OccurredAtUtc
                });

            modelBuilder.Entity<PurchaseOrder>()
                .HasOne(purchaseOrder => purchaseOrder.Budget)
                .WithMany()
                .HasForeignKey(purchaseOrder => purchaseOrder.BudgetID)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<BudgetEvent>()
                .HasOne(budgetEvent => budgetEvent.Budget)
                .WithMany()
                .HasForeignKey(budgetEvent => budgetEvent.BudgetID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BudgetEvent>()
                .HasOne(budgetEvent => budgetEvent.PerformedByUser)
                .WithMany()
                .HasForeignKey(budgetEvent => budgetEvent.PerformedByUserID)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<PurchaseOrderLine>()
                .HasOne(line => line.PurchaseOrder)
                .WithMany(purchaseOrder => purchaseOrder.Lines)
                .HasForeignKey(line => line.PurchaseOrderID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PurchaseOrderLine>()
                .HasOne(line => line.Product)
                .WithMany()
                .HasForeignKey(line => line.ProductID)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure multi-tenant global query filters
            if (_tenantContext != null)
            {
                modelBuilder.Entity<Product>().HasQueryFilter(p => !_tenantContext.HasOwnerScope || p.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<Order>().HasQueryFilter(o => !_tenantContext.HasOwnerScope || o.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<Payment>().HasQueryFilter(p => !_tenantContext.HasOwnerScope || p.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<TechnicalInquiry>().HasQueryFilter(t => !_tenantContext.HasOwnerScope || t.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<SystemLog>().HasQueryFilter(s => !_tenantContext.HasOwnerScope || s.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<MarketingCampaign>().HasQueryFilter(m => !_tenantContext.HasOwnerScope || m.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<EmailNotification>().HasQueryFilter(e => !_tenantContext.HasOwnerScope || e.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<InventoryMovement>().HasQueryFilter(i => !_tenantContext.HasOwnerScope || i.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<PurchaseOrder>().HasQueryFilter(p => !_tenantContext.HasOwnerScope || p.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<FinancialBudget>().HasQueryFilter(f => !_tenantContext.HasOwnerScope || f.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<BudgetEvent>().HasQueryFilter(b => !_tenantContext.HasOwnerScope || b.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<Employee>().HasQueryFilter(e => !_tenantContext.HasOwnerScope || e.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<OrderDetail>().HasQueryFilter(od => !_tenantContext.HasOwnerScope || od.Order!.OwnerUserID == _tenantContext.OwnerUserId);
                modelBuilder.Entity<CustomerTenantLoyalty>().HasQueryFilter(ctl => !_tenantContext.HasOwnerScope || ctl.TenantOwnerUserID == _tenantContext.OwnerUserId);
            }
        }
    }
}
