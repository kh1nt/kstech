using kstech.Data;
using kstech.Models;
using kstech.Models.Entities;
using kstech.Models.ViewModels;
using kstech.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace kstech.Controllers
{
    public class CartController : Controller
    {
        private const string ArchivedMarketPriceSource = "Archived";
        private static readonly HashSet<string> SupportedPaymentMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "Card",
            "GCash",
            "BankTransfer"
        };
        private readonly ApplicationDbContext _context;
        private readonly ILoyaltyService _loyaltyService;
        private readonly IInventoryControlService _inventoryControlService;
        private const string CustomerScheme = "CustomerScheme";

        public CartController(
            ApplicationDbContext context,
            ILoyaltyService loyaltyService,
            IInventoryControlService inventoryControlService)
        {
            _context = context;
            _loyaltyService = loyaltyService;
            _inventoryControlService = inventoryControlService;
        }

        private string GetSessionId()
        {
            if (HttpContext.Session.GetString("SessionId") == null)
            {
                HttpContext.Session.SetString("SessionId", Guid.NewGuid().ToString());
            }
            return HttpContext.Session.GetString("SessionId")!;
        }

        private async Task<int?> GetCurrentUserIdAsync()
        {
            var customerAuthResult = await HttpContext.AuthenticateAsync(CustomerScheme);
            if (!customerAuthResult.Succeeded)
            {
                return null;
            }

            var customerClaim = customerAuthResult.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(customerClaim, out var userId) ? userId : null;
        }

        // Action of Index
        public async Task<IActionResult> Index()
        {
            var sessionId = GetSessionId();
            var currentUserId = await GetCurrentUserIdAsync();
            var cartItems = await GetCartItemsAsync(currentUserId, sessionId, includeProduct: true);
            var viewModel = ToCartViewModel(cartItems, currentUserId.HasValue);

            if (currentUserId.HasValue)
            {
                await ApplyLoyaltyCartContextAsync(viewModel, currentUserId.Value, cartItems);
            }

            return View(viewModel);
        }

        // Action of Mini
        [HttpGet]
        public async Task<IActionResult> Mini()
        {
            var sessionId = GetSessionId();
            var currentUserId = await GetCurrentUserIdAsync();
            return Json(await BuildMiniCartPayloadAsync(currentUserId, sessionId));
        }

        // Action of AddToCart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(int productId, int quantity = 1)
        {
            if (quantity <= 0)
            {
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message = "Quantity must be at least 1." });
                }

                return RedirectBackOrCart();
            }

            var product = await _context.Products.FirstOrDefaultAsync(p =>
                p.ProductID == productId &&
                p.MarketPriceSource != ArchivedMarketPriceSource);
            if (product == null || product.StockQuantity <= 0)
            {
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message = "This product is currently unavailable." });
                }

                return RedirectBackOrCart();
            }

            var sessionId = GetSessionId();
            var currentUserId = await GetCurrentUserIdAsync();
            if (currentUserId.HasValue)
            {
                await MergeGuestCartIntoUserCartAsync(currentUserId.Value, sessionId);
            }

            var existingItem = await _context.CartItems
                .FirstOrDefaultAsync(c =>
                    c.ProductID == productId &&
                    (currentUserId.HasValue
                        ? c.UserID == currentUserId.Value
                        : c.SessionId == sessionId && c.UserID == null));

            var finalQuantity = Math.Min(quantity, product.StockQuantity);

            if (existingItem != null)
            {
                existingItem.Quantity = Math.Min(existingItem.Quantity + finalQuantity, product.StockQuantity);
            }
            else
            {
                var cartItem = new CartItem
                {
                    SessionId = sessionId,
                    UserID = currentUserId,
                    ProductID = productId,
                    Quantity = finalQuantity,
                    DateCreated = DateTime.UtcNow
                };
                _context.CartItems.Add(cartItem);
            }

            await _context.SaveChangesAsync();

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    message = $"{product.ProductName} added to cart.",
                    cart = await BuildMiniCartPayloadAsync(currentUserId, sessionId)
                });
            }

            return RedirectBackOrCart();
        }

        // Action of RemoveFromCart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFromCart(int cartItemId)
        {
            var sessionId = GetSessionId();
            var currentUserId = await GetCurrentUserIdAsync();
            if (currentUserId.HasValue)
            {
                await MergeGuestCartIntoUserCartAsync(currentUserId.Value, sessionId);
            }

            var item = await _context.CartItems.FirstOrDefaultAsync(c =>
                c.CartItemID == cartItemId &&
                (currentUserId.HasValue
                    ? c.UserID == currentUserId.Value
                    : c.SessionId == sessionId && c.UserID == null));

            if (item != null)
            {
                _context.CartItems.Remove(item);
                await _context.SaveChangesAsync();
            }

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    cart = await BuildMiniCartPayloadAsync(currentUserId, sessionId)
                });
            }

            return RedirectBackOrCart();
        }

        // Action of UpdateQuantity
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(int cartItemId, int quantity)
        {
            var sessionId = GetSessionId();
            var currentUserId = await GetCurrentUserIdAsync();
            if (currentUserId.HasValue)
            {
                await MergeGuestCartIntoUserCartAsync(currentUserId.Value, sessionId);
            }

            if (quantity < 0)
            {
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message = "Quantity cannot be negative." });
                }

                TempData["CartError"] = "Quantity cannot be negative.";
                return RedirectToAction(nameof(Index));
            }

            var cartItem = await _context.CartItems
                .Include(item => item.Product)
                .FirstOrDefaultAsync(item =>
                    item.CartItemID == cartItemId &&
                    (currentUserId.HasValue
                        ? item.UserID == currentUserId.Value
                        : item.SessionId == sessionId && item.UserID == null));

            if (cartItem == null)
            {
                if (IsAjaxRequest())
                {
                    return NotFound(new { success = false, message = "Cart item not found." });
                }

                TempData["CartError"] = "Cart item not found.";
                return RedirectToAction(nameof(Index));
            }

            if (quantity == 0)
            {
                _context.CartItems.Remove(cartItem);
                await _context.SaveChangesAsync();

                if (IsAjaxRequest())
                {
                    return Json(new
                    {
                        success = true,
                        message = "Item removed from cart.",
                        cart = await BuildMiniCartPayloadAsync(currentUserId, sessionId)
                    });
                }

                return RedirectToAction(nameof(Index));
            }

            if (cartItem.Product == null ||
                string.Equals(cartItem.Product.MarketPriceSource, ArchivedMarketPriceSource, StringComparison.OrdinalIgnoreCase))
            {
                _context.CartItems.Remove(cartItem);
                await _context.SaveChangesAsync();

                if (IsAjaxRequest())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "This product is no longer available.",
                        cart = await BuildMiniCartPayloadAsync(currentUserId, sessionId)
                    });
                }

                TempData["CartError"] = "This product is no longer available.";
                return RedirectToAction(nameof(Index));
            }

            if (cartItem.Product.StockQuantity <= 0)
            {
                _context.CartItems.Remove(cartItem);
                await _context.SaveChangesAsync();

                if (IsAjaxRequest())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "This product is out of stock.",
                        cart = await BuildMiniCartPayloadAsync(currentUserId, sessionId)
                    });
                }

                TempData["CartError"] = "This product is out of stock.";
                return RedirectToAction(nameof(Index));
            }

            var adjustedQuantity = Math.Min(quantity, cartItem.Product.StockQuantity);
            cartItem.Quantity = adjustedQuantity;
            await _context.SaveChangesAsync();

            var wasAdjustedByStock = adjustedQuantity != quantity;
            var updateMessage = wasAdjustedByStock
                ? $"Updated to {adjustedQuantity} due to current stock limits."
                : "Quantity updated.";

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    message = updateMessage,
                    cart = await BuildMiniCartPayloadAsync(currentUserId, sessionId)
                });
            }

            if (wasAdjustedByStock)
            {
                TempData["CartError"] = updateMessage;
            }

            return RedirectToAction(nameof(Index));
        }

        // Action of ClearCart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearCart()
        {
            var sessionId = GetSessionId();
            var currentUserId = await GetCurrentUserIdAsync();
            if (currentUserId.HasValue)
            {
                await MergeGuestCartIntoUserCartAsync(currentUserId.Value, sessionId);
            }

            var cartItems = await _context.CartItems
                .Where(item => currentUserId.HasValue
                    ? item.UserID == currentUserId.Value
                    : item.SessionId == sessionId && item.UserID == null)
                .ToListAsync();

            if (cartItems.Any())
            {
                _context.CartItems.RemoveRange(cartItems);
                await _context.SaveChangesAsync();
            }

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    message = "Cart cleared.",
                    cart = await BuildMiniCartPayloadAsync(currentUserId, sessionId)
                });
            }

            return RedirectBackOrCart();
        }

        // Action of Checkout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(
            int pointsToRedeem = 0,
            string paymentMethod = "Card",
            bool payNow = true)
        {
            var normalizedPaymentMethod = NormalizePaymentMethod(paymentMethod);
            if (!SupportedPaymentMethods.Contains(normalizedPaymentMethod))
            {
                TempData["CartError"] = "Unsupported payment method selected.";
                return RedirectToAction("Index");
            }

            var sessionId = GetSessionId();
            var currentUserId = await GetCurrentUserIdAsync();
            if (!currentUserId.HasValue)
            {
                TempData["CartError"] = "Sign in is required before you can place an order.";

                if (IsAjaxRequest())
                {
                    return Unauthorized(new
                    {
                        success = false,
                        requiresSignIn = true,
                        message = "Please sign in to complete checkout.",
                        loginUrl = Url.Action("Login", "Store", new { returnUrl = Url.Action("Index", "Cart") })
                    });
                }

                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action("Index", "Cart") });
            }

            var cartItems = await GetCartItemsAsync(currentUserId, sessionId, includeProduct: true);

            if (!cartItems.Any())
            {
                return RedirectToAction("Index");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var user = await _context.Users.FirstOrDefaultAsync(u =>
                u.UserID == currentUserId.Value &&
                u.IsActive &&
                u.UserType == "Customer");

            if (user == null)
            {
                await HttpContext.SignOutAsync(CustomerScheme);
                TempData["CartError"] = "Your customer session is no longer valid. Please sign in again.";
                return RedirectToAction("Login", "Store", new { returnUrl = Url.Action("Index", "Cart") });
            }

            var productIds = cartItems.Select(item => item.ProductID).Distinct().ToList();
            var productsById = await _context.Products
                .Where(product =>
                    productIds.Contains(product.ProductID) &&
                    product.MarketPriceSource != ArchivedMarketPriceSource)
                .ToDictionaryAsync(product => product.ProductID, product => product);

            var cartOwnerIds = productsById.Values
                .Where(product => product.OwnerUserID.HasValue)
                .Select(product => product.OwnerUserID!.Value)
                .Distinct()
                .ToList();

            if (!cartOwnerIds.Any())
            {
                await transaction.RollbackAsync();
                TempData["CartError"] = "Products in your cart are missing owner assignment. Please contact support.";
                return RedirectToAction("Index");
            }

            if (cartOwnerIds.Count > 1)
            {
                await transaction.RollbackAsync();
                TempData["CartError"] = "Your cart contains products from multiple owner stores. Please checkout one store at a time.";
                return RedirectToAction("Index");
            }

            var orderOwnerUserId = cartOwnerIds[0];
            var customer = await EnsureCustomerRecordAsync(user);

            if (!user.IsEmailVerified)
            {
                await transaction.RollbackAsync();
                TempData["CartError"] = "Please verify your email address before you can check out. Check your inbox for the verification link.";
                return RedirectToAction("Index");
            }

            var stockIssues = new List<(CartItem Item, Product? Product, int AvailableStock, int RequestedStock)>();
            foreach (var cartItem in cartItems)
            {
                productsById.TryGetValue(cartItem.ProductID, out var product);
                cartItem.Product = product;

                if (product == null || cartItem.Quantity > product.StockQuantity)
                {
                    stockIssues.Add((cartItem, product, product?.StockQuantity ?? 0, cartItem.Quantity));
                }
            }

            if (stockIssues.Any())
            {
                await transaction.CommitAsync();
                TempData["CartError"] = "Inventory changed while checking out. Please review your cart quantities and try again.";
                return RedirectToAction("Index");
            }

            var orderSubtotal = cartItems.Sum(item => item.Quantity * (item.Product?.SellingPrice ?? 0m));
            var lifetimeSpendBeforeOrder = await _context.Orders
                .Where(order =>
                    order.CustomerID == customer.CustomerID &&
                    order.OwnerUserID == orderOwnerUserId &&
                    order.PaymentStatus != "Refunded")
                .SumAsync(order => order.TotalAmount);

            var loyaltyComputation = _loyaltyService.CalculateCheckout(
                customer,
                lifetimeSpendBeforeOrder,
                orderSubtotal,
                pointsToRedeem);

            var nowUtc = DateTime.UtcNow;
            var orderStatus = payNow ? "Completed" : "Processing";
            var paymentStatus = payNow ? "Paid" : "Pending";

            // Create Order
            var order = new Order
            {
                CustomerID = customer.CustomerID,
                OwnerUserID = orderOwnerUserId,
                OrderDate = nowUtc,
                TotalAmount = loyaltyComputation.NetSubtotal,
                OrderStatus = orderStatus,
                PaymentStatus = paymentStatus,
                LoyaltyPointsRedeemed = loyaltyComputation.PointsRedeemed,
                LoyaltyPointsEarned = loyaltyComputation.PointsEarned,
                LoyaltyDiscountAmount = loyaltyComputation.RedemptionDiscount
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            var payment = new Payment
            {
                OrderID = order.OrderID,
                OwnerUserID = orderOwnerUserId,
                PaymentMethod = normalizedPaymentMethod,
                AmountPaid = order.TotalAmount,
                PaymentDateUtc = nowUtc
            };
            _context.Payments.Add(payment);

            // Create Order Details and Update Inventory
            foreach (var item in cartItems)
            {
                var product = item.Product!;
                var lineSubtotal = item.Quantity * product.SellingPrice;
                var orderDetail = new OrderDetail
                {
                    OrderID = order.OrderID,
                    ProductID = item.ProductID,
                    Quantity = item.Quantity,
                    UnitPriceAtSale = product.SellingPrice,
                    SubTotal = lineSubtotal
                };
                _context.OrderDetails.Add(orderDetail);

                _inventoryControlService.ApplyStockOut(
                    product,
                    item.Quantity,
                    $"Customer order #{order.OrderID}",
                    "Order",
                    order.OrderID.ToString(),
                    user.UserID);
            }

            _loyaltyService.ApplyCheckout(customer, order, loyaltyComputation, nowUtc);

            // Log Activity
            _context.SystemLogs.Add(new SystemLog
            {
                UserID = user.UserID,
                OwnerUserID = orderOwnerUserId,
                Action = $"Order #{order.OrderID} created with {normalizedPaymentMethod}. Payment {paymentStatus}. Loyalty +{loyaltyComputation.PointsEarned}/-{loyaltyComputation.PointsRedeemed}",
                Timestamp = nowUtc
            });

            // Clear Cart
            _context.CartItems.RemoveRange(cartItems);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return RedirectToAction("OrderConfirmation", new { id = order.OrderID });
        }

        // Action of OrderConfirmation
        public async Task<IActionResult> OrderConfirmation(int id)
        {
            var currentUserId = await GetCurrentUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Store", new
                {
                    returnUrl = Url.Action("OrderConfirmation", "Cart", new { id })
                });
            }

            var order = await _context.Orders
                .Include(o => o.Customer)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o =>
                    o.OrderID == id &&
                    o.Customer != null &&
                    o.Customer.UserID == currentUserId.Value);

            if (order == null)
            {
                TempData["CartError"] = "Order not found for your account.";
                return RedirectToAction("Index", "Store");
            }

            var latestPayment = order.Payments
                .OrderByDescending(payment => payment.PaymentDateUtc)
                .FirstOrDefault();

            var model = new OrderConfirmationViewModel
            {
                OrderId = order.OrderID,
                OrderDateUtc = order.OrderDate,
                TotalAmount = order.TotalAmount,
                OrderStatus = string.IsNullOrWhiteSpace(order.OrderStatus) ? "Unknown" : order.OrderStatus,
                PaymentStatus = string.IsNullOrWhiteSpace(order.PaymentStatus) ? "Unknown" : order.PaymentStatus,
                PaymentMethod = latestPayment?.PaymentMethod ?? "Card",
                CanProcessPayment =
                    !string.Equals(order.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(order.PaymentStatus, "Refunded", StringComparison.OrdinalIgnoreCase)
            };

            return View(model);
        }

        // Action of ProcessPayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int id, string paymentMethod = "Card")
        {
            var normalizedPaymentMethod = NormalizePaymentMethod(paymentMethod);
            if (!SupportedPaymentMethods.Contains(normalizedPaymentMethod))
            {
                TempData["OrderPaymentError"] = "Unsupported payment method selected.";
                return RedirectToAction(nameof(OrderConfirmation), new { id });
            }

            var currentUserId = await GetCurrentUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return RedirectToAction("Login", "Store", new
                {
                    returnUrl = Url.Action("OrderConfirmation", "Cart", new { id })
                });
            }

            var order = await _context.Orders
                .Include(o => o.Customer)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o =>
                    o.OrderID == id &&
                    o.Customer != null &&
                    o.Customer.UserID == currentUserId.Value);

            if (order == null)
            {
                TempData["OrderPaymentError"] = "Order not found for your account.";
                return RedirectToAction("Index", "Store");
            }

            if (string.Equals(order.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                TempData["OrderPaymentMessage"] = "Payment already completed.";
                return RedirectToAction(nameof(OrderConfirmation), new { id });
            }

            if (string.Equals(order.PaymentStatus, "Refunded", StringComparison.OrdinalIgnoreCase))
            {
                TempData["OrderPaymentError"] = "Refunded orders cannot be charged again.";
                return RedirectToAction(nameof(OrderConfirmation), new { id });
            }

            var nowUtc = DateTime.UtcNow;
            order.PaymentStatus = "Paid";
            order.OrderStatus = "Completed";

            var payment = order.Payments
                .OrderByDescending(existing => existing.PaymentDateUtc)
                .FirstOrDefault();

            if (payment == null)
            {
                _context.Payments.Add(new Payment
                {
                    OrderID = order.OrderID,
                    OwnerUserID = order.OwnerUserID,
                    PaymentMethod = normalizedPaymentMethod,
                    AmountPaid = order.TotalAmount,
                    PaymentDateUtc = nowUtc
                });
            }
            else
            {
                payment.PaymentMethod = normalizedPaymentMethod;
                payment.PaymentDateUtc = nowUtc;
                payment.AmountPaid = order.TotalAmount;
            }

            _context.SystemLogs.Add(new SystemLog
            {
                UserID = currentUserId.Value,
                OwnerUserID = order.OwnerUserID,
                Action = $"Order #{order.OrderID} payment processed via {normalizedPaymentMethod}.",
                Timestamp = nowUtc
            });

            await _context.SaveChangesAsync();
            TempData["OrderPaymentMessage"] = "Payment processed successfully.";
            return RedirectToAction(nameof(OrderConfirmation), new { id });
        }

        private static CartViewModel ToCartViewModel(IEnumerable<CartItem> cartItems, bool isSignedIn)
        {
            return new CartViewModel
            {
                IsSignedIn = isSignedIn,
                Items = cartItems.Select(c => new CartItemViewModel
                {
                    CartItemId = c.CartItemID,
                    ProductId = c.ProductID,
                    ProductName = c.Product?.ProductName ?? "Unknown",
                    Price = c.Product?.SellingPrice ?? 0,
                    Quantity = c.Quantity,
                    ImageUrl = c.Product?.ImageUrl
                }).ToList()
            };
        }

        private async Task ApplyLoyaltyCartContextAsync(CartViewModel viewModel, int userId, IReadOnlyCollection<CartItem> cartItems)
        {
            var cartOwnerIds = cartItems
                .Select(item => item.Product?.OwnerUserID)
                .Where(ownerId => ownerId.HasValue)
                .Select(ownerId => ownerId!.Value)
                .Distinct()
                .Take(2)
                .ToList();
            var cartOwnerUserId = cartOwnerIds.Count == 1 ? cartOwnerIds[0] : (int?)null;

            IQueryable<Customer> customerQuery = _context.Customers
                .AsNoTracking()
                .Where(c => c.UserID == userId);

            if (cartOwnerUserId.HasValue)
            {
                customerQuery = customerQuery
                    .OrderByDescending(c => c.LastLoyaltyActivityUtc ?? c.RegistrationDate);
            }
            else
            {
                customerQuery = customerQuery
                    .OrderByDescending(c => c.LastLoyaltyActivityUtc ?? c.RegistrationDate);
            }

            var customer = await customerQuery.FirstOrDefaultAsync();

            if (customer == null)
            {
                return;
            }

            var lifetimeSpend = await _context.Orders
                .AsNoTracking()
                .Where(order =>
                    order.CustomerID == customer.CustomerID &&
                    (!cartOwnerUserId.HasValue || order.OwnerUserID == cartOwnerUserId.Value) &&
                    order.PaymentStatus != "Refunded")
                .SumAsync(order => order.TotalAmount);

            var redeemPreview = _loyaltyService.CalculateCheckout(
                customer,
                lifetimeSpend,
                viewModel.Total,
                customer.LoyaltyPoints);

            var earnPreview = _loyaltyService.CalculateCheckout(
                customer,
                lifetimeSpend,
                viewModel.Total,
                0);

            viewModel.AvailableLoyaltyPoints = customer.LoyaltyPoints;
            viewModel.LoyaltyPointValue = _loyaltyService.PointValue;
            viewModel.MaxRedeemablePoints = redeemPreview.PointsRedeemed;
            viewModel.MaxLoyaltyDiscount = redeemPreview.RedemptionDiscount;
            viewModel.EstimatedPointsToEarn = earnPreview.PointsEarned;
        }

        private async Task<Customer> EnsureCustomerRecordAsync(User user)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.UserID == user.UserID);
            if (customer != null)
            {
                return customer;
            }

            customer = new Customer
            {
                UserID = user.UserID,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Address = "Update in Profile",
                City = "Update in Profile",
                Phone = "N/A",
                RegistrationDate = DateTime.UtcNow
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();
            return customer;
        }

        private async Task<List<CartItem>> GetCartItemsAsync(int? currentUserId, string sessionId, bool includeProduct)
        {
            if (currentUserId.HasValue)
            {
                await MergeGuestCartIntoUserCartAsync(currentUserId.Value, sessionId);
            }

            IQueryable<CartItem> query = _context.CartItems.AsQueryable();
            if (includeProduct)
            {
                query = query.Include(item => item.Product);
            }

            query = currentUserId.HasValue
                ? query.Where(item => item.UserID == currentUserId.Value)
                : query.Where(item => item.SessionId == sessionId && item.UserID == null);

            return await query.OrderByDescending(item => item.DateCreated).ToListAsync();
        }

        private async Task MergeGuestCartIntoUserCartAsync(int userId, string sessionId)
        {
            var guestItems = await _context.CartItems
                .Where(item => item.SessionId == sessionId && item.UserID == null)
                .ToListAsync();

            if (!guestItems.Any())
            {
                return;
            }

            var productIds = guestItems
                .Select(item => item.ProductID)
                .Distinct()
                .ToList();

            var existingUserItems = await _context.CartItems
                .Where(item => item.UserID == userId && productIds.Contains(item.ProductID))
                .ToListAsync();

            var stockByProduct = await _context.Products
                .Where(product =>
                    productIds.Contains(product.ProductID) &&
                    product.MarketPriceSource != ArchivedMarketPriceSource)
                .Select(product => new { product.ProductID, product.StockQuantity })
                .ToDictionaryAsync(product => product.ProductID, product => product.StockQuantity);

            foreach (var guestItem in guestItems)
            {
                if (!stockByProduct.TryGetValue(guestItem.ProductID, out var stock) || stock <= 0)
                {
                    _context.CartItems.Remove(guestItem);
                    continue;
                }

                var quantityToMerge = Math.Min(guestItem.Quantity, stock);
                if (quantityToMerge <= 0)
                {
                    _context.CartItems.Remove(guestItem);
                    continue;
                }

                var existingUserItem = existingUserItems.FirstOrDefault(item => item.ProductID == guestItem.ProductID);
                if (existingUserItem == null)
                {
                    guestItem.UserID = userId;
                    guestItem.SessionId = sessionId;
                    guestItem.Quantity = quantityToMerge;
                    existingUserItems.Add(guestItem);
                    continue;
                }

                existingUserItem.Quantity = Math.Min(existingUserItem.Quantity + quantityToMerge, stock);
                _context.CartItems.Remove(guestItem);
            }

            await _context.SaveChangesAsync();
        }

        private async Task<object> BuildMiniCartPayloadAsync(int? currentUserId, string sessionId)
        {
            var cartItems = await GetCartItemsAsync(currentUserId, sessionId, includeProduct: true);
            var viewModel = ToCartViewModel(cartItems, currentUserId.HasValue);
            var cartUrl = Url.Action("Index", "Cart") ?? "/Cart";
            var loginUrl = Url.Action("Login", "Store", new { returnUrl = cartUrl }) ?? "/Store/Login";

            return new
            {
                itemCount = viewModel.ItemCount,
                subtotal = viewModel.Total,
                subtotalDisplay = viewModel.Total.ToString("C"),
                isSignedIn = viewModel.IsSignedIn,
                requiresSignIn = !viewModel.IsSignedIn,
                checkoutUrl = viewModel.IsSignedIn ? cartUrl : loginUrl,
                items = viewModel.Items.Select(item => new
                {
                    cartItemId = item.CartItemId,
                    productId = item.ProductId,
                    productName = item.ProductName,
                    quantity = item.Quantity,
                    priceDisplay = item.Price.ToString("C"),
                    totalDisplay = item.Total.ToString("C"),
                    imageUrl = item.ImageUrl
                })
            };
        }

        private static string NormalizePaymentMethod(string? paymentMethod)
        {
            return paymentMethod?.Trim().ToLowerInvariant() switch
            {
                "card" => "Card",
                "gcash" => "GCash",
                "banktransfer" => "BankTransfer",
                "bank_transfer" => "BankTransfer",
                "bank transfer" => "BankTransfer",
                _ => "Card"
            };
        }

        private bool IsAjaxRequest()
        {
            return string.Equals(
                Request.Headers["X-Requested-With"],
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase);
        }

        private IActionResult RedirectBackOrCart()
        {
            var referer = Request.Headers.Referer.ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) &&
                string.Equals(refererUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            {
                var localPath = refererUri.PathAndQuery;
                if (Url.IsLocalUrl(localPath))
                {
                    return Redirect(localPath);
                }
            }

            return RedirectToAction("Index");
        }
    }
}
