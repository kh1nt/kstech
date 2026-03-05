
document.addEventListener("DOMContentLoaded", function () {
    // Sales Trend Chart (Line Chart)
    const ctxSalesTrend = document.getElementById("salesTrendChart").getContext("2d");
    const currencyFormatter = new Intl.NumberFormat("en-PH", {
        style: "currency",
        currency: "PHP",
        maximumFractionDigits: 0
    });

    // Create Gradient for Sales Trend
    let gradientTrend = ctxSalesTrend.createLinearGradient(0, 0, 0, 300);
    gradientTrend.addColorStop(0, "rgba(35, 100, 96, 0.2)"); // Brand Primary color (Teal) opacity
    gradientTrend.addColorStop(1, "rgba(35, 100, 96, 0)"); // Transparent

    new Chart(ctxSalesTrend, {
        type: "line",
        data: {
            labels: Array.from({ length: 24 }, (_, i) => i + 1), // 1 to 24
            datasets: [
                {
                    label: "Sales",
                    data: [
                        2200, 2400, 2100, 2800, 3100, 3200, 3000, 3500, 3800, 3400,
                        3100, 3200, 3800, 4200, 4000, 4500, 4800, 4200, 4100, 4700,
                        5000, 5200, 4900, 5400, 5800, 6000
                    ],
                    borderColor: "#236460", // brand-primary
                    backgroundColor: gradientTrend,
                    borderWidth: 2,
                    tension: 0.4, // Smooth curve
                    pointRadius: 0,
                    pointHoverRadius: 6,
                    fill: true,
                },
            ],
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: false,
                },
                tooltip: {
                    backgroundColor: "#1f2937",
                    titleColor: "#fff",
                    bodyColor: "#fff",
                    padding: 10,
                    callbacks: {
                        label: function (context) {
                            return currencyFormatter.format(context.parsed.y);
                        },
                    },
                },
            },
            scales: {
                x: {
                    grid: {
                        display: false,
                    },
                    ticks: {
                        color: "#9ca3af",
                        maxTicksLimit: 12
                    },
                },
                y: {
                    grid: {
                        color: "rgba(107, 114, 128, 0.1)", // Light gray grid lines
                        drawBorder: false,
                    },
                     ticks: {
                        color: "#9ca3af", // muted-dark
                        callback: function(value, index, values) {
                            return currencyFormatter.format(value / 1000) + "k";
                        }
                    }
                },
            },
        },
    });

    // Sales by Channel Chart (Doughnut)
    const ctxSalesChannel = document.getElementById("salesChannelChart").getContext("2d");
    new Chart(ctxSalesChannel, {
        type: "doughnut",
        data: {
            labels: ["Online Store", "Retail POS", "B2B Partners"],
            datasets: [
                {
                    data: [55, 30, 15],
                    backgroundColor: [
                        "#236460", // Online Store (Brand Primary)
                        "#a9f090", // Retail POS (Brand Secondary)
                        "#6AD2FF", // B2B Partners (Info Blue)
                    ],
                    borderWidth: 0,
                    hoverOffset: 4,
                 },
            ],
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            cutout: "75%",
             plugins: {
                legend: {
                    display: false, 
                },
                tooltip: {
                    callbacks: {
                        label: function (context) {
                            return context.label + ": " + context.parsed + "%";
                        },
                    },
                },
            },
        },
    });
});
