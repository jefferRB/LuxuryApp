# LuxuryCloud / LuxuryApp - Project Context

## Product Vision

LuxuryCloud is a multi-tenant SaaS platform designed for salons, barbershops, beauty businesses, independent professionals, and small service-based businesses.

The main goal is to help business owners understand their finances, organize their schedules, manage clients, control services, track income and expenses, and make better decisions with a simple, elegant, and easy-to-use digital system.

LuxuryCloud should feel premium, modern, clean, fast, and simple. The target users are not necessarily technical people, so every screen must be easy to understand, visually clear, and practical for daily use.

## Target Users

Primary users:
- Barbershops
- Beauty salons
- Nail salons
- Spas
- Independent stylists, barbers, nail artists, and beauty professionals
- Small businesses that need appointments, client history, income, expenses, and business control

The system must help users:
- Know how much money they are making
- Understand cash, card, and SINPE movements
- Organize appointments
- Track clients and visit history
- Control products/services
- Review business indicators without complex reports
- Reduce manual work through automation

## Core Product Principles

1. Simple first:
   - Avoid complex interfaces.
   - Use clear labels, cards, badges, and summaries.
   - Make the system understandable for non-technical business owners.

2. Premium but practical design:
   - The UI should look modern, elegant, clean, and professional.
   - Use soft shadows, rounded cards, glass/premium surfaces where appropriate.
   - Do not overload screens with too many colors or unnecessary visual noise.

3. Mobile-first responsiveness:
   - Every module must work well on desktop, tablet, and mobile.
   - Tables should become mobile-friendly cards or alternative layouts on small screens.
   - Avoid horizontal overflow, broken navbar wrapping, cramped buttons, and unusable tables.

4. Business clarity:
   - Dashboards and modules should clearly show totals, summaries, and useful indicators.
   - Income, expenses, appointments, clients, and services should always be easy to interpret.

5. Production-safe changes:
   - Do not break existing flows.
   - Avoid unnecessary refactors.
   - Preserve current working behavior unless the task explicitly asks to change it.
   - Prefer small, safe, incremental changes.

## Technical Stack

The project is an ASP.NET Core Razor Pages / MVC-style SaaS application using:
- C#
- ASP.NET Core
- Razor views
- Entity Framework Core
- SQL Server
- Bootstrap/CSS custom styling
- JavaScript where needed for UI behavior
- Multi-tenant architecture with TenantId and Row-Level Security concepts
- Production deployment on a Linux server with Nginx and systemd
- Payment integration with Tilopay
- Email integration with Resend
- Planned/active WhatsApp add-on logic for appointment confirmations and reminders

## Multi-Tenant / Security Rules

This is a multi-tenant SaaS. Tenant isolation is critical.

Always respect:
- TenantId filtering
- Current tenant context
- Row-Level Security assumptions
- No cross-tenant data access
- No queries that expose data from other tenants
- No shared global state that could mix tenant data
- No UI elements for features the tenant does not have enabled

When adding features:
- Validate that all queries are tenant-safe.
- Do not trust client-side values for TenantId.
- Use server-side authorization and tenant validation.
- Keep sensitive configuration in secrets/environment variables, not hardcoded.

## Design System Direction

The desired UI style is:
- Premium SaaS
- Clean dashboard cards
- Elegant rounded containers
- Soft shadows
- Clear hierarchy
- Responsive layouts
- Simple user flows
- Modern but not complicated

Keep:
- Navbar and layout consistent
- Theme/background system compatible with existing styles
- Cards readable over marble/futuristic/glass backgrounds
- Text contrast accessible
- Buttons easy to identify
- Mobile layouts intentionally designed, not just squeezed desktop tables

Avoid:
- Cluttered UI
- Overly technical labels
- Random colors
- Broken responsive behavior
- Large empty spaces on mobile
- Horizontal scrolling unless absolutely unavoidable
- Duplicated UI logic
- Adding product images unless explicitly requested

## Important Modules

Current/important modules include:
- Dashboard financiero
- Dashboard analítico / Información
- Clientes
- Funcionarios
- Calendario / Agenda
- Ingresos
- Egresos
- Productos / Inventario
- Cobros
- Servicios
- Configuración / Cuenta
- WhatsApp add-on features
- Subscription / plans / tenant onboarding
- Inversionistas y distribución de ganancias
- Bloqueos recurrentes de horario

## WhatsApp Add-on Rules

WhatsApp functionality must be conditional by tenant plan/add-on.

If the tenant has WhatsApp enabled:
- Show WhatsApp status panels, message state, reminders, confirmations, pending messages, sent messages, confirmed messages, etc.
- Calendar can show WhatsApp-related appointment status.
- WhatsApp workflows can be visible and usable.

If the tenant does NOT have WhatsApp enabled:
- Do not show WhatsApp panels, WhatsApp status, message KPIs, WhatsApp buttons, or WhatsApp-specific columns.
- The normal calendar and appointment flow must continue working cleanly.
- It is acceptable to show only normal appointments/clients without WhatsApp-related UI.

Appointment confirmation rule:
- Do not send all confirmations immediately when future appointments are created.
- Confirmation should be scheduled for 24 hours before the appointment.
- If the appointment is created within the next 24 hours, send the confirmation immediately.
- This avoids spam when many future recurring appointments are created.

## Finance Module Rules

Income and expense modules must prioritize business clarity.

For income:
- Show useful totals and payment method breakdowns when available.
- Efectivo, Tarjeta, and SINPE are important indicators for Costa Rican businesses.

For expenses:
- Show total expenses clearly.
- Also show breakdown indicators for Efectivo, Tarjeta, and SINPE when applicable.
- Avoid extra database queries if the loaded model already contains the needed data.
- Keep desktop layout stable unless the task explicitly asks to change desktop.
- Mobile layout should be clean and not have strange empty spaces.

## Investor Distribution Rules (Inversionistas)

Módulo multi-tenant para repartir la ganancia del negocio entre inversionistas. Ningún dato es
específico de un tenant: funciona para cualquiera.

Entidades (`Models/Inversionistas`, todas `ITenantEntity`):
`TenantInvestor`, `InvestorAgreement`, `InvestorProfitPolicy` (+ `InvestorPolicyExpenseCategory`),
`InvestorStatement`, `InvestorStatementAdjustment`, `InvestorDistributionPayment`,
`InvestorStatementEmailLog`.

Fórmula única (`InvestorProfitCalculationService`):

```
ingresos cobrados sin IVA − gastos elegibles − liquidaciones ± ajustes − pérdida anterior
= ganancia distribuible;   ganancia distribuible × % = participación
```

Reglas que no se negocian:
- Ingresos, IVA y liquidaciones se leen de `ILiquidacionSemanalService`, que ya usa el motor fiscal.
  **Nunca** se recalcula IVA ni comisiones dentro del módulo.
- Redondeo: `FiscalMath.Redondear` (2 decimales, half-even), igual que todo el sistema.
- Se excluyen SIEMPRE de los gastos la categoría `Pago Funcionarios` (ya va en liquidaciones) y
  `Distribución a inversionistas` (si contara, pagarle al inversionista reduciría su propia
  participación).
- Solo se cuentan cobros reales del periodo. Las citas nunca entran.

Ciclo de vida del estado de cuenta:
`Draft → Finalized → Sent → PartiallyPaid → Paid`, más `Voided`.
- Solo `Draft` se recalcula y admite ajustes.
- `Finalized` congela el snapshot: editar cobros o gastos históricos ya no lo mueve.
- Correcciones posteriores: ajuste auditado, anulación, o reapertura explícita con motivo.
- Finalización protegida con transacción `Serializable` + relectura (anti doble finalización).
- Generación idempotente por índice único filtrado `(TenantId, InvestorId, Periodo) WHERE Estado <> Voided`.
- Pagos: no se puede superar el saldo pendiente; una corrección crea un movimiento compensatorio
  negativo con motivo, sin borrar el pago original.

Acuerdos:
- Los acuerdos activos que se solapan no pueden sumar más de 100 %.
- Un cambio de porcentaje entra en vigor al **inicio de un periodo** financiero; a mitad se rechaza
  con el mensaje que indica la fecha válida. Nunca se edita el acuerdo pasado: se cierra y se crea
  una versión nueva.

Seguridad: solo `Administrador`. Un inversionista **no** es usuario del sistema y no hay portal.
Todo (creación, cambio de %, finalización, anulación, envío, ajustes, pagos) va a `PlatformAuditLog`.

Correo y PDF: se generan SIEMPRE desde el snapshot finalizado, nunca recalculando. No incluyen
nombres de clientes, datos de colaboradores ni información de otros inversionistas.

## Recurring Schedule Rules (Bloqueos de horario)

Bloques repetitivos de indisponibilidad (ej. almuerzo 1:00–2:00 p. m., lunes a sábado).

Entidades (`Models/Horarios`): `RecurringScheduleRule`, `RecurringScheduleRuleTarget`,
`RecurringScheduleException`.

- **La regla es la fuente de verdad.** No se crean citas falsas ni se materializan ocurrencias:
  `RecurringScheduleOccurrenceCalculator` las expande al vuelo (función pura).
- **Disponibilidad única**: `IFuncionarioAvailabilityService` combina citas, descansos y bloqueos.
  La consumen `CalendarCommandService` (crear/editar/mover/redimensionar) y
  `BookingAvailabilityService` (reservas públicas). No debe existir una segunda validación de
  solapamiento en ningún controlador.
- **Zona horaria**: `HoraInicio`/`HoraFin` son hora LOCAL del negocio (America/Costa_Rica), igual
  que `Cita.FechaHoraCita`. Nunca se guardan como UTC.
- **Alcance global = dinámico**: con "todos los colaboradores" no se guarda una fila por
  colaborador; un colaborador nuevo queda cubierto sin tocar la regla.
- **Conflictos**: al crear/editar se buscan las citas que coinciden y se muestran. Las citas
  existentes NUNCA se mueven, cancelan ni borran; la regla solo impide nuevas reservas. Activar con
  conflictos exige confirmación y queda auditado.
- **Cambios hacia el futuro**: editar una regla ya vigente cierra la versión anterior
  (`VigenteHasta`) y crea una nueva enlazada por `ReglaOrigenId`. Eliminar es baja lógica.
- **Excepciones** por fecha (y opcionalmente colaborador): omitir, cambiar horario o excluir a
  alguien. Nunca modifican la regla general.

## Calendar Rules

The Calendar module should be very usable because it is central to the business.

It should:
- Clearly show today’s appointments.
- Make appointment state easy to understand.
- Keep appointment creation/editing simple.
- Avoid UI overload.
- Respect WhatsApp add-on visibility rules.
- Remain responsive and touch-friendly on mobile.

## Coding Guidelines

When editing code:
- First inspect existing files and patterns.
- Reuse existing services, models, view models, CSS utilities, and conventions.
- Avoid large rewrites unless necessary.
- Keep changes scoped to the requested module.
- Do not introduce duplicate business logic.
- Prefer strongly typed view models over ViewBag when practical.
- Avoid unnecessary database queries.
- Validate nulls and empty states.
- Keep Spanish UI labels consistent with the existing app.
- Preserve existing working desktop designs when the task is only about responsive/mobile fixes.

## EF Core / Database Guidelines

When database changes are required:
- Use migrations.
- Review generated migration before assuming it is correct.
- Avoid destructive schema changes unless explicitly requested.
- Ensure nullable vs required fields match production realities.
- Consider existing production data.
- Keep tenant safety in mind.
- Avoid N+1 queries.
- Use Include/Select intentionally.

## Razor / CSS Guidelines

For Razor views:
- Keep markup readable.
- Avoid mixing too much business logic into views.
- Use view models or precomputed values when appropriate.
- Preserve accessibility basics: labels, button text, contrast, and focus states.

For CSS:
- Prefer module-specific CSS when the change is only for one module.
- Avoid global CSS changes that can unintentionally break other screens.
- Use media queries intentionally.
- Desktop and mobile layouts can be different when needed.
- Tables on mobile should usually become cards or compact lists.

## Production Mindset

This project is already being used in real production scenarios.

Before finishing any task:
- Mention files changed.
- Explain the business impact.
- Explain any risk or required migration.
- Suggest what should be tested.
- Verify responsive behavior when the task involves UI.
- Do not claim something is done if it was not verified.

## Communication Style

When responding:
- Be direct and practical.
- Explain technical decisions clearly.
- Use Spanish when talking to the project owner.
- Give commands step by step when deployment or database changes are involved.
- When providing prompts for another Claude Code session, make them complete, precise, and production-safe.