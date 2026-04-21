using LuxuryApp.Models.Marketing;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.Saas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.PublicSite
{
    public sealed class PublicSiteContentService : IPublicSiteContentService
    {
        private readonly ApplicationDbContext _context;
        private readonly OpcionesPago _paymentOptions;

        public PublicSiteContentService(
            ApplicationDbContext context,
            IOptions<OpcionesPago> paymentOptions)
        {
            _context = context;
            _paymentOptions = paymentOptions.Value;
        }

        public IReadOnlyCollection<MarketingMetricViewModel> GetHeroMetrics() =>
        [
            new() { Value = "1", Label = "panel para citas, caja, clientes y equipo" },
            new() { Value = "7", Label = "modulos conectados para operar sin hojas sueltas" },
            new() { Value = "24/7", Label = "visibilidad comercial para decidir con mas control" }
        ];

        public IReadOnlyCollection<MarketingModuleViewModel> GetModules() =>
        [
            new()
            {
                Id = "ganancias",
                Eyebrow = "Ganancias claras",
                Title = "Gestiona la rentabilidad diaria sin esperar al cierre del mes",
                Summary = "Visualiza cuanto entra por servicios, productos y equipo desde un solo frente operativo.",
                Solution = "LuxuryApp ordena la caja diaria y la convierte en una lectura simple para tomar decisiones rapidas.",
                Icon = "bi-graph-up-arrow",
                MockupTitle = "Resumen de ganancias",
                PrimaryMetric = "+18%",
                PrimaryLabel = "margen semanal",
                SecondaryMetric = "CRC 486k",
                SecondaryLabel = "ingreso del dia",
                TertiaryMetric = "12",
                TertiaryLabel = "servicios cobrados",
                Tags = [ "servicios", "productos", "equipo" ]
            },
            new()
            {
                Id = "finanzas",
                Eyebrow = "Ingresos y egresos",
                Title = "Controla entradas y salidas con una vista financiera que si ayuda a operar",
                Summary = "Evita perder dinero por gastos invisibles y detecta variaciones antes de que afecten la liquidez.",
                Solution = "Cada movimiento queda clasificado para que el negocio vea flujo, categorias y rentabilidad real.",
                Icon = "bi-cash-stack",
                MockupTitle = "Flujo del negocio",
                PrimaryMetric = "92%",
                PrimaryLabel = "egresos categorizados",
                SecondaryMetric = "6",
                SecondaryLabel = "alertas activas",
                TertiaryMetric = "CRC 1.2M",
                TertiaryLabel = "flujo semanal",
                Tags = [ "caja", "gastos", "margen" ],
                ReverseLayout = true
            },
            new()
            {
                Id = "calendario",
                Eyebrow = "Agenda centralizada",
                Title = "Organiza citas y calendario con una agenda preparada para un negocio de servicios",
                Summary = "Reduce huecos, cruces y olvidos con una vista pensada para la operacion diaria del salon o barberia.",
                Solution = "La agenda conecta equipo, horarios y clientes para mover el dia con menos friccion.",
                Icon = "bi-calendar2-week",
                MockupTitle = "Agenda del dia",
                PrimaryMetric = "34",
                PrimaryLabel = "citas previstas",
                SecondaryMetric = "4",
                SecondaryLabel = "espacios libres",
                TertiaryMetric = "3",
                TertiaryLabel = "reagendadas",
                Tags = [ "agenda", "horarios", "recordatorios" ]
            },
            new()
            {
                Id = "funcionarios",
                Eyebrow = "Equipo alineado",
                Title = "Administra funcionarios, roles y productividad desde la misma plataforma",
                Summary = "Consulta actividad, pagos y carga operativa del equipo sin depender de reportes manuales.",
                Solution = "El modulo de funcionarios mantiene la operacion visible para ordenar turnos, rendimiento y seguimiento.",
                Icon = "bi-people-fill",
                MockupTitle = "Rendimiento del equipo",
                PrimaryMetric = "9",
                PrimaryLabel = "funcionarios activos",
                SecondaryMetric = "87%",
                SecondaryLabel = "ocupacion promedio",
                TertiaryMetric = "CRC 74k",
                TertiaryLabel = "pago semanal",
                Tags = [ "equipo", "ocupacion", "pagos" ],
                ReverseLayout = true
            },
            new()
            {
                Id = "clientes",
                Eyebrow = "Clientes fidelizados",
                Title = "Gestiona clientes con contexto real para vender mejor y atender mejor",
                Summary = "Consolida historial, frecuencia y contacto para que cada visita tenga seguimiento comercial.",
                Solution = "LuxuryApp vuelve accionable la informacion del cliente para mejorar retencion y experiencia.",
                Icon = "bi-person-hearts",
                MockupTitle = "Relacion con clientes",
                PrimaryMetric = "68%",
                PrimaryLabel = "clientes recurrentes",
                SecondaryMetric = "146",
                SecondaryLabel = "perfiles activos",
                TertiaryMetric = "21",
                TertiaryLabel = "cumpleanos del mes",
                Tags = [ "historial", "retencion", "contacto" ]
            },
            new()
            {
                Id = "inventario",
                Eyebrow = "Inventario bajo control",
                Title = "Controla productos e inventario sin perder visibilidad entre ventas y reposicion",
                Summary = "Sabe que se vende, que rota y que debes reponer antes de afectar el servicio al cliente.",
                Solution = "El inventario deja de ser reactivo y pasa a ser parte del control operativo del negocio.",
                Icon = "bi-box2-heart",
                MockupTitle = "Inventario comercial",
                PrimaryMetric = "18",
                PrimaryLabel = "productos criticos",
                SecondaryMetric = "126",
                SecondaryLabel = "items en stock",
                TertiaryMetric = "11",
                TertiaryLabel = "mas vendidos",
                Tags = [ "stock", "rotacion", "ventas" ],
                ReverseLayout = true
            },
            new()
            {
                Id = "dashboard",
                Eyebrow = "Vision ejecutiva",
                Title = "Visualiza dashboard financiero e informacion del negocio con lectura inmediata",
                Summary = "Une operacion, agenda y finanzas en una misma capa visual para decidir con contexto completo.",
                Solution = "Ideal para quienes quieren dirigir el negocio con menos intuicion y mas evidencia diaria.",
                Icon = "bi-speedometer2",
                MockupTitle = "Centro de control",
                PrimaryMetric = "7",
                PrimaryLabel = "indicadores clave",
                SecondaryMetric = "3",
                SecondaryLabel = "modulos conectados",
                TertiaryMetric = "1",
                TertiaryLabel = "vista ejecutiva",
                Tags = [ "dashboard", "KPIs", "decision" ]
            }
        ];

        public async Task<IReadOnlyCollection<MarketingPlanCardViewModel>> GetPlanCardsAsync(
            CancellationToken cancellationToken = default)
        {
            var plans = await BuildAvailablePlansQuery()
                .AsNoTracking()
                .Include(plan => plan.PlanFeatures)
                    .ThenInclude(planFeature => planFeature.Feature)
                .OrderBy(plan => plan.PrecioMensual)
                .ToListAsync(cancellationToken);

            return plans.Select(MapPlanCard).ToArray();
        }

        public Task<Plan?> FindAvailablePlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
            BuildAvailablePlansQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(plan => plan.Id == planId, cancellationToken);

        public async Task<string?> GetPlanNameAsync(Guid? planId, CancellationToken cancellationToken = default)
        {
            if (!planId.HasValue || planId.Value == Guid.Empty)
            {
                return null;
            }

            return await BuildAvailablePlansQuery()
                .AsNoTracking()
                .Where(plan => plan.Id == planId.Value)
                .Select(plan => plan.Nombre)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private IQueryable<Plan> BuildAvailablePlansQuery() =>
            _context.Planes.Where(plan =>
                plan.Activo &&
                (!plan.EsPlanValidacion || _paymentOptions.EnableValidationPlans));

        private static MarketingPlanCardViewModel MapPlanCard(Plan plan)
        {
            var highlights = plan.PlanFeatures
                .Select(FormatFeature)
                .Where(highlight => !string.IsNullOrWhiteSpace(highlight))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();

            if (highlights.Length == 0)
            {
                highlights =
                [
                    "Agenda comercial del equipo",
                    "Control de ingresos y egresos",
                    plan.MaxFuncionarios.HasValue
                        ? $"Hasta {plan.MaxFuncionarios.Value} funcionarios"
                        : "Funcionarios ilimitados",
                    "Dashboard operativo"
                ];
            }

            var isFeatured = string.Equals(plan.Nombre, "Empresarial", StringComparison.OrdinalIgnoreCase) ||
                !plan.MaxFuncionarios.HasValue;

            return new MarketingPlanCardViewModel
            {
                Id = plan.Id,
                Name = plan.Nombre,
                BillingLabel = "por mes",
                MonthlyPrice = plan.PrecioMensual,
                CurrencyCode = string.IsNullOrWhiteSpace(plan.Moneda) ? "CRC" : plan.Moneda,
                StaffLabel = plan.MaxFuncionarios.HasValue
                    ? $"Hasta {plan.MaxFuncionarios.Value} funcionarios"
                    : "Funcionarios ilimitados",
                Summary = plan.EsPlanValidacion
                    ? "Plan controlado para validar el primer cobro real con riesgo financiero minimo."
                    : isFeatured
                        ? "Pensado para operaciones con mayor ritmo, mas equipo y necesidad de visibilidad completa."
                        : "Una base profesional para ordenar la operacion comercial desde el primer dia.",
                BadgeText = plan.EsPlanValidacion
                    ? "Validacion"
                    : isFeatured
                        ? "Mas elegido"
                        : null,
                IsFeatured = isFeatured,
                IsValidationPlan = plan.EsPlanValidacion,
                Highlights = highlights
            };
        }

        private static string FormatFeature(PlanFeature planFeature)
        {
            var featureName = planFeature.Feature?.Nombre?.Trim();
            if (string.IsNullOrWhiteSpace(featureName))
            {
                return string.Empty;
            }

            return planFeature.Limite.HasValue
                ? $"{featureName} hasta {planFeature.Limite.Value}"
                : featureName;
        }
    }
}
