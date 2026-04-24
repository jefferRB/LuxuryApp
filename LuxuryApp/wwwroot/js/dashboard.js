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
        new Chart(chartMesEl, {
            type: "bar",
            data: {
                labels: ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"],
                datasets: [{
                    label: "Citas",
                    data: citasMes
                }]
            }
        });
    }

    let fechaSemana = new Date();

    const dia = fechaSemana.getDay();
    const diff = dia === 0 ? -6 : 1 - dia;
    fechaSemana.setDate(fechaSemana.getDate() + diff);
    fechaSemana.setHours(0, 0, 0, 0);

    const ctxSemanaEl = document.getElementById("chartSemana");
    let chartSemana = null;

    if (ctxSemanaEl) {
        const ctx = ctxSemanaEl.getContext("2d");

        const gradient = ctx.createLinearGradient(0, 0, 0, 300);
        gradient.addColorStop(0, "rgba(13, 110, 253, 0.5)");
        gradient.addColorStop(1, "rgba(13, 110, 253, 0)");

        chartSemana = new Chart(ctx, {
            type: "line",
            data: {
                labels: semanaDias,
                datasets: [{
                    data: semanaCitas,
                    fill: true,
                    backgroundColor: gradient,
                    tension: 0.4
                }]
            }
        });
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
        chartSemana.update();

        document.getElementById("textoSemana").innerText =
            `Semana ${data.inicio} - ${data.fin}`;
    };

    const chartFuncEl = document.getElementById("chartFuncionarios");
    if (chartFuncEl) {
        new Chart(chartFuncEl, {
            type: "bar",
            data: {
                labels: funcionariosLabels,
                datasets: [{
                    data: funcionariosData
                }]
            }
        });
    }

    const chartServEl = document.getElementById("chartServicios");
    if (chartServEl) {
        const ctx = chartServEl.getContext("2d");

        const gradient = ctx.createLinearGradient(0, 0, 600, 0);
        gradient.addColorStop(0, "rgba(25, 135, 84, 0.9)");
        gradient.addColorStop(1, "rgba(25, 135, 84, 0.3)");

        new Chart(ctx, {
            type: "bar",
            data: {
                labels: serviciosLabels,
                datasets: [{
                    data: serviciosData,
                    backgroundColor: gradient
                }]
            },
            options: {
                indexAxis: "y"
            }
        });
    }
});
