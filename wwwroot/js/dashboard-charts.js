(() => {
    "use strict";

    const filterSelect = document.getElementById("filter");
    const startDateInput = document.getElementById("startDate");
    const endDateInput = document.getElementById("endDate");

    if (filterSelect && startDateInput && endDateInput) {
        filterSelect.addEventListener("change", () => {
            if (filterSelect.value !== "custom") {
                startDateInput.value = "";
                endDateInput.value = "";
            }
        });

        const switchToCustomRange = () => {
            if (startDateInput.value || endDateInput.value) {
                filterSelect.value = "custom";
            }
        };

        startDateInput.addEventListener("change", switchToCustomRange);
        endDateInput.addEventListener("change", switchToCustomRange);
    }

    const chartDataElement = document.getElementById("dashboard-chart-data");
    if (!chartDataElement || typeof window.Chart === "undefined") {
        return;
    }

    let payload;
    try {
        payload = JSON.parse(chartDataElement.textContent || "{}");
    } catch {
        return;
    }

    const spendingLabels = payload?.SpendingByCategory?.Labels ?? [];
    const spendingAmounts = payload?.SpendingByCategory?.Amounts ?? [];
    const monthlyLabels = payload?.IncomeVsExpense?.Labels ?? [];
    const monthlyIncome = payload?.IncomeVsExpense?.Income ?? [];
    const monthlyExpense = payload?.IncomeVsExpense?.Expense ?? [];

    const currencyFormatter = new Intl.NumberFormat("en-PH", {
        style: "currency",
        currency: "PHP",
        minimumFractionDigits: 2,
        maximumFractionDigits: 2
    });

    const spendingCanvas = document.getElementById("spendingByCategoryChart");
    if (spendingCanvas && spendingLabels.length > 0 && spendingAmounts.length > 0) {
        const piePalette = [
            "#0f766e",
            "#14b8a6",
            "#f59e0b",
            "#f97316",
            "#3b82f6",
            "#16a34a",
            "#ec4899",
            "#64748b",
            "#84cc16",
            "#dc2626"
        ];

        new Chart(spendingCanvas, {
            type: "doughnut",
            data: {
                labels: spendingLabels,
                datasets: [
                    {
                        data: spendingAmounts,
                        backgroundColor: spendingLabels.map((_, index) => piePalette[index % piePalette.length]),
                        borderColor: "#ffffff",
                        borderWidth: 2
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: "bottom"
                    },
                    tooltip: {
                        callbacks: {
                            label: (context) => `${context.label}: ${currencyFormatter.format(context.parsed)}`
                        }
                    }
                }
            }
        });
    }

    const incomeExpenseCanvas = document.getElementById("incomeExpenseChart");
    if (incomeExpenseCanvas && monthlyLabels.length > 0) {
        new Chart(incomeExpenseCanvas, {
            type: "line",
            data: {
                labels: monthlyLabels,
                datasets: [
                    {
                        label: "Income",
                        data: monthlyIncome,
                        borderColor: "#0f766e",
                        backgroundColor: "rgba(15, 118, 110, 0.18)",
                        fill: true,
                        tension: 0.28,
                        borderWidth: 2
                    },
                    {
                        label: "Expense",
                        data: monthlyExpense,
                        borderColor: "#c2410c",
                        backgroundColor: "rgba(194, 65, 12, 0.12)",
                        fill: true,
                        tension: 0.28,
                        borderWidth: 2
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: {
                    mode: "index",
                    intersect: false
                },
                scales: {
                    y: {
                        beginAtZero: true,
                        ticks: {
                            callback: (value) => currencyFormatter.format(Number(value))
                        }
                    }
                },
                plugins: {
                    tooltip: {
                        callbacks: {
                            label: (context) => `${context.dataset.label}: ${currencyFormatter.format(context.parsed.y)}`
                        }
                    }
                }
            }
        });
    }
})();
