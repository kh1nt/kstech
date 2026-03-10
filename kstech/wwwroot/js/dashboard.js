document.addEventListener("DOMContentLoaded", () => {
  const payloadElement = document.getElementById("dashboardChartData");
  const salesCanvas = document.getElementById("salesChart");
  const pieCanvas = document.getElementById("pieChart");

  if (
    !payloadElement ||
    !salesCanvas ||
    !pieCanvas ||
    typeof Chart === "undefined"
  ) {
    return;
  }

  let payload = {};
  try {
    payload = JSON.parse(payloadElement.textContent || "{}");
  } catch {
    payload = {};
  }

  const salesLabels = Array.isArray(payload.salesLabels)
    ? payload.salesLabels
    : [];
  const salesData = Array.isArray(payload.salesData)
    ? payload.salesData.map((value) => Number(value) || 0)
    : [];

  const pieLabels = Array.isArray(payload.pieLabels) ? payload.pieLabels : [];
  const pieData = Array.isArray(payload.pieData)
    ? payload.pieData.map((value) => Number(value) || 0)
    : [];

  const colors = {
    primary: "#236460",
    secondary: "#a9f090",
    tertiary: "#6AD2FF",
    quaternary: "#fcd34d",
    accent: "#fca5a5",
    muted: "#CBD5E1",
    text: "#9ca3af",
    grid: "rgba(107, 114, 128, 0.15)",
  };

  const currencyFormatter = new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  });

  const safeSalesLabels = salesLabels.length > 0 ? salesLabels : ["No Data"];
  const safeSalesData = salesData.length > 0 ? salesData : [0];

  const salesContext = salesCanvas.getContext("2d");
  if (!salesContext) {
    return;
  }

  const salesFillGradient = salesContext.createLinearGradient(0, 0, 0, 260);
  salesFillGradient.addColorStop(0, "rgba(35, 100, 96, 0.28)");
  salesFillGradient.addColorStop(1, "rgba(35, 100, 96, 0.03)");

  new Chart(salesContext, {
    type: "line",
    data: {
      labels: safeSalesLabels,
      datasets: [
        {
          label: "Sales",
          data: safeSalesData,
          borderColor: colors.primary,
          backgroundColor: salesFillGradient,
          fill: true,
          tension: 0.35,
          pointRadius: 3,
          pointHoverRadius: 4,
          pointBackgroundColor: "#ffffff",
          pointBorderColor: colors.primary,
          pointBorderWidth: 2,
        },
      ],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: {
        mode: "index",
        intersect: false,
      },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (context) =>
              `${context.dataset.label}: ${currencyFormatter.format(
                Number(context.parsed.y || 0),
              )}`,
          },
        },
      },
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: colors.text },
        },
        y: {
          beginAtZero: true,
          grid: { color: colors.grid },
          ticks: {
            color: colors.text,
            callback: (value) =>
              currencyFormatter.format(Number(value || 0)),
          },
        },
      },
    },
  });

  const hasPieData = pieData.some((value) => value > 0);
  const safePieLabels = hasPieData ? pieLabels : ["No sales data"];
  const safePieData = hasPieData ? pieData : [1];
  const safePieColors = hasPieData
    ? [
        colors.primary,
        colors.secondary,
        colors.tertiary,
        colors.quaternary,
        colors.accent,
      ]
    : [colors.muted];

  new Chart(pieCanvas.getContext("2d"), {
    type: "doughnut",
    data: {
      labels: safePieLabels,
      datasets: [
        {
          data: safePieData,
          backgroundColor: safePieColors,
          borderWidth: 0,
          hoverOffset: 4,
        },
      ],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      cutout: "72%",
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (context) => {
              if (!hasPieData) {
                return "No sales data in selected range";
              }

              return `${context.label}: ${context.parsed}%`;
            },
          },
        },
      },
    },
  });
});
