document.addEventListener("DOMContentLoaded", () => {
    function formatLocalDate(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, "0");
        const day = String(date.getDate()).padStart(2, "0");
        return `${year}-${month}-${day}`;
    }

    if (!window.dashboardData) {
        console.error("No hay datos del dashboard");
        return;
    }

    function getChartTokens() {
        return window.luxuryGetPrivateThemeTokens
            ? window.luxuryGetPrivateThemeTokens()
            : {
                chartText: "#334155",
                chartMuted: "#64748b",
                chartGrid: "rgba(148, 163, 184, 0.24)"
            };
    }

    function buildAxisOptions(indexAxis = "x") {
        const tokens = getChartTokens();

        return {
            responsive: true,
            maintainAspectRatio: false,
            color: tokens.chartText,
            indexAxis,
            scales: {
                x: {
                    ticks: { color: tokens.chartMuted },
                    grid: { color: tokens.chartGrid }
                },
                y: {
                    ticks: { color: tokens.chartMuted },
                    grid: { color: tokens.chartGrid }
                }
            },
            plugins: {
                legend: {
                    labels: { color: tokens.chartText }
                }
            }
        };
    }

    function registerChart(chart) {
        return window.luxuryRegisterChart
            ? window.luxuryRegisterChart(chart)
            : chart;
    }

    function destroyExistingChart(canvas) {
        if (!canvas) {
            return;
        }

        if (window.luxuryDestroyChartForCanvas) {
            window.luxuryDestroyChartForCanvas(canvas);
            return;
        }

        if (window.Chart && typeof window.Chart.getChart === "function") {
            const existingChart = window.Chart.getChart(canvas);
            existingChart?.destroy();
        }
    }

    const {
        citasMes,
        semanaDias,
        semanaCitas,
        funcionariosLabels,
        funcionariosData,
        serviciosLabels,
        serviciosData
    } = window.dashboardData;

    const chartMesEl = document.getElementById("chartMes");
    if (chartMesEl) {
        destroyExistingChart(chartMesEl);
        registerChart(new Chart(chartMesEl, {
            type: "bar",
            data: {
                labels: ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"],
                datasets: [{
                    label: "Citas",
                    data: citasMes
                }]
            },
            options: buildAxisOptions()
        }));
    }

    let fechaSemana = new Date();

    const dia = fechaSemana.getDay();
    const diff = dia === 0 ? -6 : 1 - dia;
    fechaSemana.setDate(fechaSemana.getDate() + diff);
    fechaSemana.setHours(0, 0, 0, 0);

    const ctxSemanaEl = document.getElementById("chartSemana");
    let chartSemana = null;

    if (ctxSemanaEl) {
        destroyExistingChart(ctxSemanaEl);
        const ctx = ctxSemanaEl.getContext("2d");

        const gradient = ctx.createLinearGradient(0, 0, 0, 300);
        gradient.addColorStop(0, "rgba(13, 110, 253, 0.5)");
        gradient.addColorStop(1, "rgba(13, 110, 253, 0)");

        chartSemana = registerChart(new Chart(ctx, {
            type: "line",
            data: {
                labels: semanaDias,
                datasets: [{
                    label: "Citas",
                    data: semanaCitas,
                    fill: true,
                    backgroundColor: gradient,
                    tension: 0.4
                }]
            },
            options: buildAxisOptions()
        }));
    }

    window.cambiarSemana = async function (dias) {
        if (!chartSemana) {
            return;
        }

        fechaSemana.setDate(fechaSemana.getDate() + dias);
        fechaSemana.setHours(0, 0, 0, 0);

        const fecha = formatLocalDate(fechaSemana);
        const response = await fetch(`/Informacion/ObtenerCitasSemana?semana=${fecha}`);
        const data = await response.json();

        chartSemana.data.labels = data.dias;
        chartSemana.data.datasets[0].data = data.citas;
        window.luxuryRefreshCharts?.();
        chartSemana.update();

        document.getElementById("textoSemana").innerText =
            `Semana ${data.inicio} - ${data.fin}`;
    };

    const chartFuncEl = document.getElementById("chartFuncionarios");
    if (chartFuncEl) {
        destroyExistingChart(chartFuncEl);
        registerChart(new Chart(chartFuncEl, {
            type: "bar",
            data: {
                labels: funcionariosLabels,
                datasets: [{
                    label: "Citas",
                    data: funcionariosData
                }]
            },
            options: buildAxisOptions()
        }));
    }

    const chartServEl = document.getElementById("chartServicios");
    if (chartServEl) {
        destroyExistingChart(chartServEl);
        const ctx = chartServEl.getContext("2d");

        const gradient = ctx.createLinearGradient(0, 0, 600, 0);
        gradient.addColorStop(0, "rgba(25, 135, 84, 0.9)");
        gradient.addColorStop(1, "rgba(25, 135, 84, 0.3)");

        registerChart(new Chart(ctx, {
            type: "bar",
            data: {
                labels: serviciosLabels,
                datasets: [{
                    label: "Servicios",
                    data: serviciosData,
                    backgroundColor: gradient
                }]
            },
            options: buildAxisOptions("y")
        }));
    }
});
