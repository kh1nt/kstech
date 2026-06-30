using kstech.Configuration;
using kstech.Data;
using kstech.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace kstech.Services
{
    /// <summary>
    /// Seeds sample data for development/testing.
    /// </summary>
    public class DataSeeder
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuthService _authService;
        private readonly SeedOptions _seedOptions;
        private readonly ILogger<DataSeeder> _logger;

        public DataSeeder(
            ApplicationDbContext context,
            IAuthService authService,
            IOptions<SeedOptions> seedOptions,
            ILogger<DataSeeder> logger)
        {
            _context = context;
            _authService = authService;
            _seedOptions = seedOptions.Value;
            _logger = logger;
        }

        public void EnsureSystemAccounts()
        {
            _context.Database.Migrate();
            EnsureSuperAdminUser();

            if (_seedOptions.CleanupToSuperAdminOnlyOnStartup)
            {
                var superAdminUserId = ResolveSuperAdminUserIdForCleanup();
                var removedUserCount = CleanDatabaseKeepOnlyUser(superAdminUserId);
                _logger.LogWarning(
                    "CleanupToSuperAdminOnlyOnStartup is enabled. Database data was purged; only superadmin user ID {SuperAdminUserId} is preserved. Removed {RemovedUserCount} non-superadmin users.",
                    superAdminUserId,
                    removedUserCount);
                return;
            }

            if (!_seedOptions.EnsureDefaultOwnerAccount)
            {
                return;
            }

            var owner = EnsureDefaultOwnerUser();
            BackfillLegacyOwnerData(owner.UserID);
        }

        public void Seed()
        {
            if (_seedOptions.CleanupToSuperAdminOnlyOnStartup)
            {
                _logger.LogWarning(
                    "Sample data seeding skipped because CleanupToSuperAdminOnlyOnStartup is enabled.");
                return;
            }

            EnsureSystemAccounts();
            var owner = EnsureDefaultOwnerUser();

            SeedCategoriesAndProducts(owner.UserID);
            SeedCustomers(owner.UserID);
            SeedOrdersAndSales(owner.UserID);
        }

        public int CleanDatabaseToSuperAdminOnly()
        {
            _context.Database.Migrate();
            EnsureSuperAdminUser();

            var superAdminUserId = ResolveSuperAdminUserIdForCleanup();
            var removedUserCount = CleanDatabaseKeepOnlyUser(superAdminUserId);

            _logger.LogWarning(
                "Database cleaned for publish. Only superadmin user ID {SuperAdminUserId} was preserved. Removed {RemovedUserCount} non-superadmin users.",
                superAdminUserId,
                removedUserCount);

            return removedUserCount;
        }

        private void EnsureSuperAdminUser()
        {
            if (string.IsNullOrWhiteSpace(_seedOptions.SuperAdminEmail))
            {
                return;
            }

            var superAdminEmail = _seedOptions.SuperAdminEmail.Trim();
            var superAdmin = _context.Users.FirstOrDefault(user => user.Email == superAdminEmail);
            var hasChanges = false;

            if (superAdmin == null)
            {
                var password = ResolveSeedPassword(_seedOptions.SuperAdminPassword, "SuperAdmin");
                superAdmin = new User
                {
                    Email = superAdminEmail,
                    PasswordHash = _authService.HashPassword(password),
                    FullName = "System Super Admin",
                    Role = "SuperAdmin",
                    UserType = "Internal",
                    OwnerUserID = null,
                    DateCreated = DateTime.UtcNow,
                    IsActive = true,
                    IsEmailVerified = true
                };

                _context.Users.Add(superAdmin);
                _context.SaveChanges();
                return;
            }

            if (!string.Equals(superAdmin.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                superAdmin.Role = "SuperAdmin";
                hasChanges = true;
            }

            if (!string.Equals(superAdmin.UserType, "Internal", StringComparison.OrdinalIgnoreCase))
            {
                superAdmin.UserType = "Internal";
                hasChanges = true;
            }

            if (superAdmin.OwnerUserID.HasValue)
            {
                superAdmin.OwnerUserID = null;
                hasChanges = true;
            }

            if (!superAdmin.IsActive)
            {
                superAdmin.IsActive = true;
                hasChanges = true;
            }

            if (hasChanges)
            {
                _context.SaveChanges();
            }
        }

        private User EnsureDefaultOwnerUser()
        {
            var existingOwner = _context.Users
                .OrderBy(user => user.UserID)
                .FirstOrDefault(user =>
                    user.UserType == "Internal" &&
                    user.Role == "Owner");

            if (existingOwner != null)
            {
                if (!existingOwner.IsEmailVerified)
                {
                    existingOwner.IsEmailVerified = true;
                    existingOwner.EmailVerificationToken = null;
                    _context.SaveChanges();
                }

                if (!existingOwner.OwnerUserID.HasValue || existingOwner.OwnerUserID != existingOwner.UserID)
                {
                    existingOwner.OwnerUserID = existingOwner.UserID;
                    _context.SaveChanges();
                }

                EnsureOwnerEmployeeRecord(existingOwner);
                return existingOwner;
            }

            var adminPassword = ResolveSeedPassword(_seedOptions.AdminPassword, "Owner");
            var ownerEmail = string.IsNullOrWhiteSpace(_seedOptions.AdminEmail)
                ? "admin@kstech.com"
                : _seedOptions.AdminEmail.Trim();

            var owner = new User
            {
                Email = ownerEmail,
                PasswordHash = _authService.HashPassword(adminPassword),
                FullName = "System Owner",
                Role = "Owner",
                UserType = "Internal",
                AllowSuperAdminWorkspaceEdits = false,
                DateCreated = DateTime.UtcNow,
                IsActive = true,
                IsEmailVerified = true
            };

            _context.Users.Add(owner);
            _context.SaveChanges();

            owner.OwnerUserID = owner.UserID;
            _context.SaveChanges();

            EnsureOwnerEmployeeRecord(owner);
            return owner;
        }

        private void EnsureOwnerEmployeeRecord(User owner)
        {
            if (_context.Employees.Any(employee => employee.UserID == owner.UserID))
            {
                return;
            }

            var ownerEmployee = new Employee
            {
                UserID = owner.UserID,
                OwnerUserID = owner.UserID,
                FullName = owner.FullName,
                Position = "Owner",
                HireDate = DateTime.UtcNow,
                ContactNumber = "N/A"
            };

            _context.Employees.Add(ownerEmployee);
            _context.SaveChanges();
        }

        private void SeedCustomers(int ownerUserId)
        {
            var customerPassword = ResolveSeedPassword(
                _seedOptions.DefaultCustomerPassword,
                "Customer");

            var random = new Random();
            const int minimumCustomersPerOwner = 7;

            var existingCustomerEmails = _context.Users
                .Where(user =>
                    user.OwnerUserID == ownerUserId &&
                    user.UserType == "Customer")
                .Select(user => user.Email.ToUpper())
                .ToHashSet();

            var customerCount = existingCustomerEmails.Count;
            if (customerCount >= minimumCustomersPerOwner)
            {
                return;
            }

            var philippineCustomerData = new List<(string FullName, string Email, string Phone, string Address, string City)>
            {
                ("Juan Dela Cruz", "juan.delacruz@kstech.ph", "09171001001", "Blk 12 Lot 8, Commonwealth Avenue", "Quezon City"),
                ("Maria Santos", "maria.santos@kstech.ph", "09181002002", "Unit 5B, Ayala Avenue", "Makati"),
                ("Jose Reyes", "jose.reyes@kstech.ph", "09201003003", "P. Burgos Street, Barangay Lahug", "Cebu City"),
                ("Angela Cruz", "angela.cruz@kstech.ph", "09171004004", "Maa Road, Barangay Maa", "Davao City"),
                ("Carlo Mendoza", "carlo.mendoza@kstech.ph", "09221005005", "Jaro District, E. Lopez Street", "Iloilo City"),
                ("Patricia Gomez", "patricia.gomez@kstech.ph", "09271006006", "Session Road, Upper General Luna", "Baguio City"),
                ("Ramon Villanueva", "ramon.villanueva@kstech.ph", "09051007007", "Masterson Avenue, Upper Carmen", "Cagayan de Oro"),
                ("Liza Navarro", "liza.navarro@kstech.ph", "09171008008", "Ortigas Avenue Extension", "Pasig City"),
                ("Miguel Bautista", "miguel.bautista@kstech.ph", "09451009009", "Alabang-Zapote Road", "Muntinlupa City"),
                ("Theresa Aquino", "theresa.aquino@kstech.ph", "09171010010", "Lacson Avenue, Sampaloc", "Manila")
            };

            foreach (var customerRow in philippineCustomerData)
            {
                if (customerCount >= minimumCustomersPerOwner)
                {
                    break;
                }

                var normalizedEmail = customerRow.Email.ToUpperInvariant();
                if (existingCustomerEmails.Contains(normalizedEmail))
                {
                    continue;
                }

                CreateCustomerWithAccount(
                    ownerUserId,
                    customerPassword,
                    customerRow.FullName,
                    customerRow.Email,
                    customerRow.Phone,
                    customerRow.Address,
                    customerRow.City,
                    random);

                existingCustomerEmails.Add(normalizedEmail);
                customerCount++;
            }

            var generatedCustomerIndex = 1;
            while (customerCount < minimumCustomersPerOwner)
            {
                var generatedEmail = $"sample.customer{generatedCustomerIndex:00}@kstech.ph";
                var normalizedGeneratedEmail = generatedEmail.ToUpperInvariant();

                if (existingCustomerEmails.Contains(normalizedGeneratedEmail))
                {
                    generatedCustomerIndex++;
                    continue;
                }

                CreateCustomerWithAccount(
                    ownerUserId,
                    customerPassword,
                    $"Sample Customer {generatedCustomerIndex:00}",
                    generatedEmail,
                    $"+63990{generatedCustomerIndex:000000}",
                    "Sample Address, Barangay Poblacion",
                    "Makati",
                    random);

                existingCustomerEmails.Add(normalizedGeneratedEmail);
                customerCount++;
                generatedCustomerIndex++;
            }
        }

        private void CreateCustomerWithAccount(
            int ownerUserId,
            string customerPassword,
            string fullName,
            string email,
            string phone,
            string address,
            string city,
            Random random)
        {
            var user = new User
            {
                Email = email,
                PasswordHash = _authService.HashPassword(customerPassword),
                FullName = fullName,
                Role = "Customer",
                UserType = "Customer",
                OwnerUserID = ownerUserId,
                DateCreated = DateTime.UtcNow,
                IsActive = true,
                IsEmailVerified = true
            };

            _context.Users.Add(user);
            _context.SaveChanges();

            var customer = new Customer
            {
                UserID = user.UserID,
                FullName = fullName,
                Email = email,
                Phone = phone,
                Address = address,
                City = city,
                RegistrationDate = DateTime.UtcNow.AddMonths(-random.Next(1, 12))
            };

            _context.Customers.Add(customer);
            _context.SaveChanges();
        }

        private void SeedCategoriesAndProducts(int ownerUserId)
        {
            if (_context.Products.Any(product => product.OwnerUserID == ownerUserId))
            {
                return;
            }

            var products = new List<Product>
            {
                new() { OwnerUserID = ownerUserId, ProductName = "NVIDIA GeForce RTX 4090", Sku = "NV-4090", CategoryName = "GPU", Brand = "NVIDIA", StockQuantity = 10, CostPrice = 1400m, SellingPrice = 1699m, MarketPrice = 1650m, MarketPriceSource = "Seed", ImageUrl = string.Empty, Description = "24GB GDDR6X, The ultimate GPU for gamers and creators." },
                new() { OwnerUserID = ownerUserId, ProductName = "NVIDIA GeForce RTX 4080", Sku = "NV-4080", CategoryName = "GPU", Brand = "NVIDIA", StockQuantity = 25, CostPrice = 900m, SellingPrice = 1199m, MarketPrice = 1150m, MarketPriceSource = "Seed", ImageUrl = string.Empty, Description = "16GB GDDR6X, Ada Lovelace Architecture." },
                new() { OwnerUserID = ownerUserId, ProductName = "AMD Radeon RX 7900 XTX", Sku = "AMD-7900XTX", CategoryName = "GPU", Brand = "AMD", StockQuantity = 15, CostPrice = 800m, SellingPrice = 999m, MarketPrice = 950m, MarketPriceSource = "Seed", ImageUrl = string.Empty, Description = "24GB GDDR6, RDNA 3 Architecture." },
                new() { OwnerUserID = ownerUserId, ProductName = "Intel Core i9-13900K", Sku = "INT-13900K", CategoryName = "CPU", Brand = "Intel", StockQuantity = 20, CostPrice = 480m, SellingPrice = 589m, MarketPrice = 570m, MarketPriceSource = "Seed", ImageUrl = string.Empty, Description = "24 Cores, up to 5.8 GHz." },
                new() { OwnerUserID = ownerUserId, ProductName = "Samsung 990 PRO 2TB", Sku = "SAM-990-2T", CategoryName = "Storage", Brand = "Samsung", StockQuantity = 100, CostPrice = 130m, SellingPrice = 189m, MarketPrice = 179m, MarketPriceSource = "Seed", ImageUrl = string.Empty, Description = "PCIe 4.0 NVMe M.2 SSD." },
                new() { OwnerUserID = ownerUserId, ProductName = "Corsair Vengeance RGB 32GB", Sku = "COR-DDR5-32", CategoryName = "RAM", Brand = "Corsair", StockQuantity = 60, CostPrice = 110m, SellingPrice = 159m, MarketPrice = 150m, MarketPriceSource = "Seed", ImageUrl = string.Empty, Description = "DDR5 6000MHz CL36." }
            };

            _context.Products.AddRange(products);
            _context.SaveChanges();
        }

        private void SeedOrdersAndSales(int ownerUserId)
        {
            if (_context.Orders.Any(order => order.OwnerUserID == ownerUserId))
            {
                return;
            }

            var customers = _context.Customers
                .Where(customer =>
                    customer.User != null &&
                    customer.User.OwnerUserID == ownerUserId)
                .ToList();
            if (!customers.Any())
            {
                return;
            }

            var products = _context.Products
                .Where(product => product.OwnerUserID == ownerUserId)
                .ToList();
            if (!products.Any())
            {
                return;
            }

            var random = new Random();
            var orders = new List<Order>();

            for (var i = 0; i < 150; i++)
            {
                var customer = customers[random.Next(customers.Count)];
                var orderDate = DateTime.UtcNow.AddDays(-random.Next(1, 180));

                var order = new Order
                {
                    OwnerUserID = ownerUserId,
                    Customer = customer,
                    OrderDate = orderDate,
                    OrderStatus = "Completed",
                    PaymentStatus = "Paid",
                    OrderDetails = new List<OrderDetail>()
                };

                var itemsCount = random.Next(1, 4);
                var total = 0m;

                for (var itemIndex = 0; itemIndex < itemsCount; itemIndex++)
                {
                    var product = products[random.Next(products.Count)];
                    var quantity = random.Next(1, 3);
                    var price = product.SellingPrice;

                    var detail = new OrderDetail
                    {
                        Product = product,
                        Quantity = quantity,
                        UnitPriceAtSale = price,
                        SubTotal = price * quantity
                    };

                    total += detail.SubTotal;
                    order.OrderDetails.Add(detail);
                }

                var earnedPoints = (int)Math.Floor(total * 0.05m);
                order.LoyaltyPointsEarned = earnedPoints;

                var tenantLoyalty = _context.CustomerTenantLoyalties.Local
                    .FirstOrDefault(l => l.CustomerID == customer.CustomerID && l.TenantOwnerUserID == ownerUserId)
                    ?? _context.CustomerTenantLoyalties
                    .FirstOrDefault(l => l.CustomerID == customer.CustomerID && l.TenantOwnerUserID == ownerUserId);

                if (tenantLoyalty == null)
                {
                    tenantLoyalty = new CustomerTenantLoyalty
                    {
                        CustomerID = customer.CustomerID,
                        TenantOwnerUserID = ownerUserId,
                        LoyaltyPoints = 0,
                        LifetimePointsEarned = 0,
                        LifetimePointsRedeemed = 0
                    };
                    _context.CustomerTenantLoyalties.Add(tenantLoyalty);
                }

                tenantLoyalty.LoyaltyPoints += earnedPoints;
                tenantLoyalty.LifetimePointsEarned += earnedPoints;
                tenantLoyalty.LastLoyaltyActivityUtc = orderDate;
                order.TotalAmount = total;

                orders.Add(order);
            }

            _context.Orders.AddRange(orders);
            _context.SaveChanges();
        }

        private int ResolveSuperAdminUserIdForCleanup()
        {
            var configuredSuperAdminEmail = _seedOptions.SuperAdminEmail?.Trim();
            User? superAdmin = null;

            if (!string.IsNullOrWhiteSpace(configuredSuperAdminEmail))
            {
                var normalizedSuperAdminEmail = configuredSuperAdminEmail.ToUpperInvariant();
                superAdmin = _context.Users
                    .OrderBy(user => user.UserID)
                    .FirstOrDefault(user =>
                        user.Email.ToUpper() == normalizedSuperAdminEmail);
            }

            superAdmin ??= _context.Users
                .OrderBy(user => user.UserID)
                .FirstOrDefault(user =>
                    user.Role.ToUpper() == "SUPERADMIN");

            if (superAdmin == null)
            {
                throw new InvalidOperationException(
                    "Superadmin account was not found. Configure Seed:SuperAdminEmail and Seed:SuperAdminPassword before cleanup.");
            }

            return superAdmin.UserID;
        }

        private int CleanDatabaseKeepOnlyUser(int superAdminUserId)
        {
            using var transaction = _context.Database.BeginTransaction();

            _context.CartItems.ExecuteDelete();
            _context.PurchaseOrderLines.ExecuteDelete();
            _context.Payments.ExecuteDelete();
            _context.OrderDetails.ExecuteDelete();
            _context.TechnicalInquiries.ExecuteDelete();
            _context.EmailNotifications.ExecuteDelete();
            _context.Orders.ExecuteDelete();
            _context.InventoryMovements.ExecuteDelete();
            _context.PurchaseOrders.ExecuteDelete();
            _context.FinancialBudgets.ExecuteDelete();
            _context.Campaigns.ExecuteDelete();
            _context.SystemLogs.ExecuteDelete();
            _context.CustomerTenantLoyalties.ExecuteDelete();
            _context.Customers.ExecuteDelete();
            _context.Employees.ExecuteDelete();
            _context.Products.ExecuteDelete();

            var removedUserCount = _context.Users
                .Where(user => user.UserID != superAdminUserId)
                .ExecuteDelete();

            _context.Users
                .Where(user => user.UserID == superAdminUserId)
                .ExecuteUpdate(setters => setters
                    .SetProperty(user => user.Role, "SuperAdmin")
                    .SetProperty(user => user.UserType, "Internal")
                    .SetProperty(user => user.OwnerUserID, (int?)null)
                    .SetProperty(user => user.AllowSuperAdminWorkspaceEdits, false)
                    .SetProperty(user => user.IsActive, true));

            transaction.Commit();
            return removedUserCount;
        }

        private void BackfillLegacyOwnerData(int fallbackOwnerUserId)
        {
            _context.Users
                .Where(user =>
                    user.UserType == "Internal" &&
                    user.Role == "Owner" &&
                    user.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(user => user.OwnerUserID, user => user.UserID));

            _context.Users
                .Where(user =>
                    user.UserType == "Internal" &&
                    user.Role != "SuperAdmin" &&
                    user.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(user => user.OwnerUserID, fallbackOwnerUserId));

            _context.Employees
                .Where(employee => employee.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(employee => employee.OwnerUserID, fallbackOwnerUserId));

            _context.Products
                .Where(product => product.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(product => product.OwnerUserID, fallbackOwnerUserId));

            _context.Orders
                .Where(order => order.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(order => order.OwnerUserID, fallbackOwnerUserId));

            _context.Payments
                .Where(payment => payment.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(payment => payment.OwnerUserID, fallbackOwnerUserId));

            _context.TechnicalInquiries
                .Where(inquiry => inquiry.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(inquiry => inquiry.OwnerUserID, fallbackOwnerUserId));

            var orphanLogs = _context.SystemLogs
                .Where(log => log.OwnerUserID == null)
                .Join(
                    _context.Users,
                    log => log.UserID,
                    user => user.UserID,
                    (log, user) => new { Log = log, User = user })
                .ToList();

            foreach (var item in orphanLogs)
            {
                if (string.Equals(item.User.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                item.Log.OwnerUserID = item.User.OwnerUserID ?? fallbackOwnerUserId;
            }

            _context.SaveChanges();

            _context.Campaigns
                .Where(campaign => campaign.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(campaign => campaign.OwnerUserID, fallbackOwnerUserId));

            _context.EmailNotifications
                .Where(notification => notification.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(notification => notification.OwnerUserID, fallbackOwnerUserId));

            _context.InventoryMovements
                .Where(movement => movement.OwnerUserID == null)
                .ExecuteUpdate(setters => setters
                    .SetProperty(movement => movement.OwnerUserID, fallbackOwnerUserId));
        }

        private string ResolveSeedPassword(string configuredPassword, string accountType)
        {
            if (!string.IsNullOrWhiteSpace(configuredPassword))
            {
                return configuredPassword;
            }

            var generatedPassword = $"DevOnly!{Guid.NewGuid():N}"[..16];
            _logger.LogWarning(
                "{AccountType} seed password not configured. Generated temporary development password: {GeneratedPassword}",
                accountType,
                generatedPassword);

            return generatedPassword;
        }
    }
}
