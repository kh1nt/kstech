using System.Globalization;
using kstech.Models;
using kstech.Models.ViewModels;
using kstech.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace kstech.Services
{
    public interface IReportPdfService
    {
        byte[] BuildSalesReport(SalesAnalyticsViewModel model);
        byte[] BuildHomeDashboardReport(HomeDashboardViewModel model);
        byte[] BuildInventoryReport(InventoryDashboardViewModel dashboardModel, InventoryViewModel inventoryModel);
        byte[] BuildFinancialReport(FinancialPerformanceViewModel model);
    }

    public class ReportPdfService : IReportPdfService
    {
        private const int MaxSalesRows = 50;
        private const int MaxInventoryRows = 50;
        private static readonly CultureInfo ReportCulture = CultureInfo.GetCultureInfo("en-PH");
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ReportPdfService(IWebHostEnvironment webHostEnvironment)
        {
            _webHostEnvironment = webHostEnvironment;
        }

        public byte[] BuildSalesReport(SalesAnalyticsViewModel model)
        {
            var generatedAt = BusinessTime.ConvertUtcToBusinessTime(DateTime.UtcNow);
            var reportLogo = TryReadReportLogo();
            var salesRows = (model.RecentOrders ?? new List<SalesOrderSnapshotViewModel>())
                .Take(MaxSalesRows)
                .ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(9));
                    page.PageColor(Colors.White);

                    page.Header().Element(header =>
                        ComposeReportHeader(
                            header,
                            "Sales Report",
                            $"Period: {model.FilterStartDate:MMM dd, yyyy} - {model.FilterEndDate:MMM dd, yyyy}",
                            generatedAt,
                            reportLogo));

                    page.Content().PaddingTop(10).Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Element(card => ComposeInfoCard(
                            card,
                            "Applied Filters",
                            new[]
                            {
                                ("Date Range", HumanizeDateRange(model.SelectedDateRange)),
                                ("Payment", HumanizeToken(model.SelectedPaymentFilter)),
                                ("Order Status", HumanizeToken(model.SelectedOrderStatusFilter))
                            }));

                        column.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Total Revenue", FormatCurrency(model.TotalRevenue)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Total Orders", model.TotalOrders.ToString("N0", ReportCulture)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Average Order Value", FormatCurrency(model.AverageOrderValue)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Paid Order Rate", $"{model.PaidOrderRate.ToString("0.##", ReportCulture)}%"));
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text("Payment Breakdown").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(3);
                                        columns.RelativeColumn(1);
                                        columns.RelativeColumn(1);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("Status");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Orders");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Share");
                                    });

                                    if (model.PaymentStatusBreakdown.Any())
                                    {
                                        foreach (var item in model.PaymentStatusBreakdown)
                                        {
                                            table.Cell().Element(TableBodyCell).Text(item.Label);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(item.Count.ToString("N0", ReportCulture));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text($"{item.Percentage.ToString("0.##", ReportCulture)}%");
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(3).Element(TableBodyCell).Text("No payment data for the selected period.");
                                    }
                                });
                            });
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text("Fast Moving Items").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(3);
                                        columns.RelativeColumn(2);
                                        columns.RelativeColumn(1);
                                        columns.RelativeColumn(1.5f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("Product");
                                        header.Cell().Element(TableHeaderCell).Text("Category");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Units");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Revenue");
                                    });

                                    if (model.FastMovingItems.Any())
                                    {
                                        foreach (var item in model.FastMovingItems)
                                        {
                                            table.Cell().Element(TableBodyCell).Text(item.ProductName);
                                            table.Cell().Element(TableBodyCell).Text(item.CategoryName);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(item.UnitsSold.ToString("N0", ReportCulture));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(item.Revenue));
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(4).Element(TableBodyCell).Text("No fast-moving items for the selected period.");
                                    }
                                });
                            });
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text($"Recent Orders (up to {MaxSalesRows})").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(1.2f);
                                        columns.RelativeColumn(2.2f);
                                        columns.RelativeColumn(1.8f);
                                        columns.RelativeColumn(2.1f);
                                        columns.RelativeColumn(1.7f);
                                        columns.RelativeColumn(1.5f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("Order");
                                        header.Cell().Element(TableHeaderCell).Text("Customer");
                                        header.Cell().Element(TableHeaderCell).Text("Date");
                                        header.Cell().Element(TableHeaderCell).Text("Products");
                                        header.Cell().Element(TableHeaderCell).Text("Status");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Total");
                                    });

                                    if (salesRows.Any())
                                    {
                                        foreach (var order in salesRows)
                                        {
                                            table.Cell().Element(TableBodyCell).Text($"#ORD-{order.OrderId}");
                                            table.Cell().Element(TableBodyCell).Text(order.CustomerName);
                                            table.Cell().Element(TableBodyCell).Text(BusinessTime.ConvertUtcToBusinessTime(order.OrderDate).ToString("MMM dd, yyyy HH:mm", ReportCulture));
                                            table.Cell().Element(TableBodyCell).Text(order.ProductsSummary);
                                            table.Cell().Element(TableBodyCell).Text($"{order.OrderStatus} / {order.PaymentStatus}");
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(order.TotalAmount));
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(6).Element(TableBodyCell).Text("No orders found for the selected period.");
                                    }
                                });
                            });
                        });
                    });

                    page.Footer().AlignRight().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        public byte[] BuildHomeDashboardReport(HomeDashboardViewModel model)
        {
            var generatedAt = BusinessTime.ConvertUtcToBusinessTime(DateTime.UtcNow);
            var reportLogo = TryReadReportLogo();
            var recentOrders = (model.RecentOrders ?? new List<RecentOrderViewModel>())
                .Take(20)
                .ToList();
            var topProducts = (model.FastSellingProducts ?? new List<ProductViewModel>())
                .Take(20)
                .ToList();
            var lowStockSkuText = model.LowStockSkus != null && model.LowStockSkus.Any()
                ? string.Join(", ", model.LowStockSkus)
                : "None";

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(9));
                    page.PageColor(Colors.White);

                    page.Header().Element(header =>
                        ComposeReportHeader(
                            header,
                            "Dashboard Report",
                            $"Period: {model.StartDate:MMM dd, yyyy} - {model.EndDate:MMM dd, yyyy}",
                            generatedAt,
                            reportLogo));

                    page.Content().PaddingTop(10).Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Element(card => ComposeInfoCard(
                            card,
                            "Applied Filters",
                            new[] { ("Date Range", HumanizeDateRange(model.FilterPeriod)) }));

                        column.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Total Sales", FormatCurrency(model.TotalSales)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Total Profits", FormatCurrency(model.TotalProfits)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Stock Health", $"{model.StockHealthPercentage.ToString("0.##", ReportCulture)}%"));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Active Customers", model.ActiveCustomers.ToString("N0", ReportCulture)));
                        });

                        column.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(card => ComposeInfoCard(
                                card,
                                "Stock Risk",
                                new[]
                                {
                                    ("At-Risk SKUs", $"{model.AtRiskSkuCount.ToString("N0", ReportCulture)} of {model.ActiveSkuCount.ToString("N0", ReportCulture)}"),
                                    ("At-Risk Share", $"{model.StockAtRiskPercentage.ToString("0.##", ReportCulture)}%"),
                                    ("Health Change", $"{model.StockHealthChangePercentage.ToString("+#0.##;-#0.##;0", ReportCulture)}%")
                                }));

                            row.RelativeItem().Element(card => ComposeInfoCard(
                                card,
                                "Operational Snapshot",
                                new[]
                                {
                                    ("Pending Orders", model.PendingOrdersCount.ToString("N0", ReportCulture)),
                                    ("Completed Orders", model.CompletedOrdersCount.ToString("N0", ReportCulture)),
                                    ("Open Inquiries", model.OpenInquiriesCount.ToString("N0", ReportCulture)),
                                    ("Low Stock Alerts", model.LowStockCount.ToString("N0", ReportCulture))
                                }));
                        });

                        column.Item().Element(card => ComposeInfoCard(
                            card,
                            "Low-Stock SKU Preview",
                            new[] { ("SKUs", lowStockSkuText) }));

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text("Sales Share by Category").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(3);
                                        columns.RelativeColumn(1);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("Category");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Share");
                                    });

                                    if (model.CategoryLabels.Any() && model.CategoryData.Any())
                                    {
                                        var count = Math.Min(model.CategoryLabels.Count, model.CategoryData.Count);
                                        for (var i = 0; i < count; i++)
                                        {
                                            table.Cell().Element(TableBodyCell).Text(model.CategoryLabels[i]);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text($"{model.CategoryData[i].ToString("N0", ReportCulture)}%");
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(2).Element(TableBodyCell).Text("No category share data for the selected period.");
                                    }
                                });
                            });
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text("Recent Orders").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(1.2f);
                                        columns.RelativeColumn(2.4f);
                                        columns.RelativeColumn(1.7f);
                                        columns.RelativeColumn(1.5f);
                                        columns.RelativeColumn(1.5f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("Order");
                                        header.Cell().Element(TableHeaderCell).Text("Customer");
                                        header.Cell().Element(TableHeaderCell).Text("Date");
                                        header.Cell().Element(TableHeaderCell).Text("Status");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Total");
                                    });

                                    if (recentOrders.Any())
                                    {
                                        foreach (var order in recentOrders)
                                        {
                                            table.Cell().Element(TableBodyCell).Text($"#ORD-{order.OrderId}");
                                            table.Cell().Element(TableBodyCell).Text(order.CustomerName);
                                            table.Cell().Element(TableBodyCell).Text(BusinessTime.ConvertUtcToBusinessTime(order.OrderDate).ToString("MMM dd, yyyy HH:mm", ReportCulture));
                                            table.Cell().Element(TableBodyCell).Text(order.Status);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(order.TotalAmount));
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(5).Element(TableBodyCell).Text("No recent orders found for the selected period.");
                                    }
                                });
                            });
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text("Top Selling Products").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(2.6f);
                                        columns.RelativeColumn(1.6f);
                                        columns.RelativeColumn(1);
                                        columns.RelativeColumn(1.2f);
                                        columns.RelativeColumn(1.4f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("Product");
                                        header.Cell().Element(TableHeaderCell).Text("Category");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Units");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Price");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Revenue");
                                    });

                                    if (topProducts.Any())
                                    {
                                        foreach (var product in topProducts)
                                        {
                                            var estimatedRevenue = product.RetailPrice * product.TotalSold;
                                            table.Cell().Element(TableBodyCell).Text(product.Name);
                                            table.Cell().Element(TableBodyCell).Text(product.Category);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(product.TotalSold.ToString("N0", ReportCulture));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(product.RetailPrice));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(estimatedRevenue));
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(5).Element(TableBodyCell).Text("No top-selling product data for the selected period.");
                                    }
                                });
                            });
                        });
                    });

                    page.Footer().AlignRight().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        public byte[] BuildInventoryReport(InventoryDashboardViewModel dashboardModel, InventoryViewModel inventoryModel)
        {
            var generatedAt = BusinessTime.ConvertUtcToBusinessTime(DateTime.UtcNow);
            var reportLogo = TryReadReportLogo();
            var productRows = (inventoryModel.Products ?? new List<ProductViewModel>())
                .Take(MaxInventoryRows)
                .ToList();
            var lowStockSkuText = dashboardModel.LowStockSkus != null && dashboardModel.LowStockSkus.Any()
                ? string.Join(", ", dashboardModel.LowStockSkus)
                : "None";
            var damagedSkuText = dashboardModel.DamagedSkus != null && dashboardModel.DamagedSkus.Any()
                ? string.Join(", ", dashboardModel.DamagedSkus)
                : "None";

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(9));
                    page.PageColor(Colors.White);

                    page.Header().Element(header =>
                        ComposeReportHeader(
                            header,
                            "Inventory Report",
                            $"Period: {dashboardModel.FilterStartDate:MMM dd, yyyy} - {dashboardModel.FilterEndDate:MMM dd, yyyy}",
                            generatedAt,
                            reportLogo));

                    page.Content().PaddingTop(10).Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Element(card => ComposeInfoCard(
                            card,
                            "Applied Filters",
                            new[] { ("Date Range", HumanizeDateRange(dashboardModel.SelectedDateRange)) }));

                        column.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Total Inventory Value", FormatCurrency(dashboardModel.TotalInventoryValue)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Sales in Period", FormatCurrency(dashboardModel.MonthlySales)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Low Stock Items", dashboardModel.LowStockCount.ToString("N0", ReportCulture)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Average Markup", $"{dashboardModel.AvgMarketMarkup.ToString("0.##", ReportCulture)}%"));
                        });

                        column.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(card => ComposeInfoCard(
                                card,
                                "Stock Movement",
                                new[]
                                {
                                    ("Stock In Units", dashboardModel.StockInUnitsThisMonth.ToString("N0", ReportCulture)),
                                    ("Stock Out Units", dashboardModel.StockOutUnitsThisMonth.ToString("N0", ReportCulture))
                                }));
                            row.RelativeItem().Element(card => ComposeInfoCard(
                                card,
                                "Sales Context",
                                new[]
                                {
                                    ("Top Sales Category", string.IsNullOrWhiteSpace(dashboardModel.HighestSalesCategory) ? "N/A" : dashboardModel.HighestSalesCategory),
                                    ("Low Stock SKU Preview", lowStockSkuText)
                                }));
                            row.RelativeItem().Element(card => ComposeInfoCard(
                                card,
                                "Inventory Quality",
                                new[]
                                {
                                    ("Damaged SKUs", dashboardModel.DamagedItemCount.ToString("N0", ReportCulture)),
                                    ("Damaged Units", dashboardModel.DamagedUnitCount.ToString("N0", ReportCulture)),
                                    ("Damage Rate", $"{dashboardModel.DamageRate.ToString("0.##", ReportCulture)}%"),
                                    ("Est. Loss Value", FormatCurrency(dashboardModel.EstimatedDamageLossValue)),
                                    ("Damaged SKU Preview", damagedSkuText)
                                }));
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text("Stock Distribution by Category").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(2);
                                        columns.RelativeColumn(1);
                                        columns.RelativeColumn(1);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("Category");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Share");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Value");
                                    });

                                    if (dashboardModel.StockDistributionData.Any())
                                    {
                                        foreach (var item in dashboardModel.StockDistributionData)
                                        {
                                            table.Cell().Element(TableBodyCell).Text(item.Category);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text($"{item.Percentage.ToString("N0", ReportCulture)}%");
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(item.Value));
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(3).Element(TableBodyCell).Text("No stock distribution data available.");
                                    }
                                });
                            });
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text($"Product Snapshot (up to {MaxInventoryRows})").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(1.4f);
                                        columns.RelativeColumn(2.2f);
                                        columns.RelativeColumn(1f);
                                        columns.RelativeColumn(1.3f);
                                        columns.RelativeColumn(1f);
                                        columns.RelativeColumn(1.1f);
                                        columns.RelativeColumn(1.1f);
                                        columns.RelativeColumn(1.1f);
                                        columns.RelativeColumn(1.1f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("SKU");
                                        header.Cell().Element(TableHeaderCell).Text("Product");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Stock");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Damaged");
                                        header.Cell().Element(TableHeaderCell).Text("Condition");
                                        header.Cell().Element(TableHeaderCell).Text("Stock Status");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Unit Cost");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Retail");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Market");
                                    });

                                    if (productRows.Any())
                                    {
                                        foreach (var product in productRows)
                                        {
                                            table.Cell().Element(TableBodyCell).Text(product.Sku);
                                            table.Cell().Element(TableBodyCell).Text(product.Name);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(product.StockQuantity.ToString("N0", ReportCulture));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(product.DamagedQuantity.ToString("N0", ReportCulture));
                                            table.Cell().Element(TableBodyCell).Text(product.ConditionStatus);
                                            table.Cell().Element(TableBodyCell).Text(product.StockStatus);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(product.UnitCost));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(product.RetailPrice));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(product.EbayLivePrice ?? 0m));
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(9).Element(TableBodyCell).Text("No inventory products available.");
                                    }
                                });
                            });
                        });
                    });

                    page.Footer().AlignRight().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        public byte[] BuildFinancialReport(FinancialPerformanceViewModel model)
        {
            var generatedAt = BusinessTime.ConvertUtcToBusinessTime(DateTime.UtcNow);
            var reportLogo = TryReadReportLogo();
            var transactionRows = (model.RecentTransactions ?? new List<FinancialTransactionViewModel>())
                .Take(MaxSalesRows)
                .ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(9));
                    page.PageColor(Colors.White);

                    page.Header().Element(header =>
                        ComposeReportHeader(
                            header,
                            "Financial Report",
                            $"Period: {model.FilterStartDate:MMM dd, yyyy} - {model.FilterEndDate:MMM dd, yyyy}",
                            generatedAt,
                            reportLogo));

                    page.Content().PaddingTop(10).Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Element(card => ComposeInfoCard(
                            card,
                            "Applied Filters",
                            new[]
                            {
                                ("Date Range", HumanizeDateRange(model.SelectedDateRange)),
                                ("Payment Scope", HumanizeToken(model.SelectedPaymentScope))
                            }));

                        column.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Revenue", FormatCurrency(model.Revenue)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "COGS", FormatCurrency(model.CostOfGoodsSold)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Gross Profit", FormatCurrency(model.GrossProfit)));
                            row.RelativeItem().Element(card => ComposeMetricCard(card, "Net Profit Margin", $"{model.NetProfitMargin.ToString("0.##", ReportCulture)}%"));
                        });

                        column.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(card => ComposeInfoCard(
                                card,
                                "Period-over-Period Change",
                                new[]
                                {
                                    ("Revenue Change", $"{model.RevenueChangePercentage.ToString("0.##", ReportCulture)}%"),
                                    ("Gross Profit Change", $"{model.GrossProfitChangePercentage.ToString("0.##", ReportCulture)}%"),
                                    ("Margin Change", $"{model.MarginChangePercentage.ToString("0.##", ReportCulture)}%")
                                }));

                            row.RelativeItem().Element(card => ComposeInfoCard(
                                card,
                                "Budget Context",
                                new[]
                                {
                                    ("Budget Amount", FormatCurrency(model.BudgetAmount)),
                                    ("Budget Variance", FormatCurrency(model.BudgetVariance)),
                                    ("Budget Utilization", $"{model.BudgetUtilizationPercentage.ToString("0.##", ReportCulture)}%")
                                }));
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text("Profit by Category").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(2.5f);
                                        columns.RelativeColumn(1.5f);
                                        columns.RelativeColumn(1.2f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("Category");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Profit");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Share");
                                    });

                                    if (model.ProfitByCategory.Any())
                                    {
                                        foreach (var item in model.ProfitByCategory)
                                        {
                                            table.Cell().Element(TableBodyCell).Text(item.Category);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(item.Profit));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text($"{item.Percentage.ToString("0.##", ReportCulture)}%");
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(3).Element(TableBodyCell).Text("No category profitability data for this period.");
                                    }
                                });
                            });
                        });

                        column.Item().Element(section =>
                        {
                            section.Column(sectionColumn =>
                            {
                                sectionColumn.Spacing(5);
                                sectionColumn.Item().Text($"Recent Transactions (up to {MaxSalesRows})").SemiBold().FontSize(11).FontColor("#0f172a");
                                sectionColumn.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(1.1f);
                                        columns.RelativeColumn(2.2f);
                                        columns.RelativeColumn(1.4f);
                                        columns.RelativeColumn(1.2f);
                                        columns.RelativeColumn(1.2f);
                                        columns.RelativeColumn(1.2f);
                                        columns.RelativeColumn(1.1f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Element(TableHeaderCell).Text("ID");
                                        header.Cell().Element(TableHeaderCell).Text("Product");
                                        header.Cell().Element(TableHeaderCell).Text("Category");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Sale");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Cost");
                                        header.Cell().Element(TableHeaderCell).AlignRight().Text("Profit");
                                        header.Cell().Element(TableHeaderCell).Text("Status");
                                    });

                                    if (transactionRows.Any())
                                    {
                                        foreach (var item in transactionRows)
                                        {
                                            table.Cell().Element(TableBodyCell).Text(item.TransactionId);
                                            table.Cell().Element(TableBodyCell).Text(item.ProductName);
                                            table.Cell().Element(TableBodyCell).Text(item.Category);
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(item.SalePrice));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(item.Cost));
                                            table.Cell().Element(TableBodyCell).AlignRight().Text(FormatCurrency(item.Profit));
                                            table.Cell().Element(TableBodyCell).Text(item.Status);
                                        }
                                    }
                                    else
                                    {
                                        table.Cell().ColumnSpan(7).Element(TableBodyCell).Text("No transactions available for the selected period.");
                                    }
                                });
                            });
                        });
                    });

                    page.Footer().AlignRight().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private static string FormatCurrency(decimal amount)
        {
            return $"PHP {amount.ToString("N2", ReportCulture)}";
        }

        private static string HumanizeDateRange(string? range)
        {
            return range?.Trim().ToLowerInvariant() switch
            {
                "today" => "Today",
                "yesterday" => "Yesterday",
                "last_7_days" => "Last 7 Days",
                "this_month" => "This Month",
                "this_year" => "This Year",
                "custom" => "Custom Range",
                _ => "This Month"
            };
        }

        private static string HumanizeToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "All";
            }

            var value = token.Trim().Replace('_', ' ');
            return string.Join(
                ' ',
                value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
        }

        private static void ComposeReportHeader(
            IContainer container,
            string title,
            string subtitle,
            DateTime generatedAt,
            byte[]? logoBytes)
        {
            container.Column(column =>
            {
                column.Spacing(4);
                column.Item().Row(row =>
                {
                    row.Spacing(8);

                    if (logoBytes != null && logoBytes.Length > 0)
                    {
                        row.ConstantItem(52).Height(36).Image(logoBytes);
                    }

                    row.RelativeItem().Column(textColumn =>
                    {
                        textColumn.Item().Text(title).SemiBold().FontSize(18).FontColor("#0f172a");
                        textColumn.Item().Text(subtitle).FontSize(10).FontColor("#475569");
                    });

                    row.ConstantItem(205).AlignRight().AlignMiddle().Text(text =>
                    {
                        text.Span("Generated: ").SemiBold();
                        text.Span(generatedAt.ToString("MMM dd, yyyy HH:mm", ReportCulture));
                    });
                });

                column.Item().LineHorizontal(1).LineColor("#cbd5e1");
            });
        }

        private static void ComposeMetricCard(IContainer container, string label, string value)
        {
            container
                .Border(1)
                .BorderColor("#cbd5e1")
                .Background("#f8fafc")
                .Padding(8)
                .Column(column =>
                {
                    column.Spacing(2);
                    column.Item().Text(label).FontSize(8).FontColor("#475569");
                    column.Item().Text(value).SemiBold().FontSize(12).FontColor("#0f172a");
                });
        }

        private static void ComposeInfoCard(IContainer container, string title, IEnumerable<(string Label, string Value)> items)
        {
            container
                .Border(1)
                .BorderColor("#cbd5e1")
                .Background("#f8fafc")
                .Padding(8)
                .Column(column =>
                {
                    column.Spacing(3);
                    column.Item().Text(title).SemiBold().FontSize(10).FontColor("#0f172a");

                    foreach (var item in items)
                    {
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Text(item.Label).FontColor("#475569");
                            row.RelativeItem().AlignRight().Text(item.Value).SemiBold().FontColor("#1f2937");
                        });
                    }
                });
        }

        private static IContainer TableHeaderCell(IContainer container)
        {
            return container
                .Background("#1f2937")
                .PaddingHorizontal(5)
                .PaddingVertical(4)
                .DefaultTextStyle(text => text.SemiBold().FontSize(8).FontColor(Colors.White));
        }

        private static IContainer TableBodyCell(IContainer container)
        {
            return container
                .BorderBottom(1)
                .BorderColor("#e2e8f0")
                .PaddingHorizontal(5)
                .PaddingVertical(4)
                .DefaultTextStyle(text => text.FontSize(8.5f).FontColor("#1f2937"));
        }

        private byte[]? TryReadReportLogo()
        {
            var webRootPath = _webHostEnvironment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
            {
                return null;
            }

            var logoPath = Path.Combine(webRootPath, "logo.png");
            return File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;
        }
    }
}
