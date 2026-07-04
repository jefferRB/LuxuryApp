# Calculadora dinámica de suscripción (1–11 funcionarios × Mensual/Anual)

> Entregable #1: resumen técnico de arquitectura + plan de ejecución.
> Estado: F0 ✅ · F1 ✅ · F2 ✅ · F3 ✅ · F4 ✅ — COMPLETO.
> Suite: 546/546 verde. Migraciones `AddPlanBillingCycle` + `AddPlanChangeIntent` APLICADAS en BD dev. Render verificado en /Billing/Planes.

## F4 — Aumentar funcionarios (upgrade) ✅
Investigación (Bewe cobra ~US$20 por funcionario extra; best practice = upgrade inmediato, prorrateo
ideal pero TiloPay hosted links no lo soportan). Implementado: upgrade = nuevo checkout recurrente del
plan mayor → al confirmar, la suscripción salta al plan nuevo (límite + ciclo) y la suscripción
proveedor ANTERIOR se marca `PendingManualCancellation` con **alerta en PlatformAuditLog**
(`PlanUpgradeRequiresProviderCancellation`) para cancelarla a mano en TiloPay (no hay API). Nunca dos
suscripciones proveedor activas en silencio.
- `Models/Saas/PlanChangeIntent.cs` (+ enums) — entidad tenant-scoped; índice único filtrado
  `[Estado]=0` ⇒ **un solo cambio Pending por tenant** (anti doble-cambio). Migración `AddPlanChangeIntent`.
- `Services/SaaS/PlanChangeService.cs` — `CreateOrReuseAsync` (mismo destino reutiliza, otro rechaza),
  `ApplyAppliedAsync` (idempotente; marca Applied + flag cancelación + escribe alerta).
- `CheckoutCalculadora` crea/reutiliza el intento cuando es cambio (sub activa con otro plan recurrente).
- `SaaSPaymentService.ApproveRecurringPaymentAsync` (usado por webhook success y aprobación manual)
  dispara `ApplyAppliedAsync` tras activar ⇒ un solo punto cubre ambos caminos.
- Tests `SubscriptionPurchaseAndUpgradeTests` (45): comprar 22 planes + upgrade +1 en 20, anti
  doble-cambio, pago fallido mantiene plan, aprobación duplicada aplica una vez.

### Runbook prod actualizado
Aplicar AMBAS migraciones (`AddPlanBillingCycle`, `AddPlanChangeIntent`) + `Scripts/SeedCalculatorPlans.sql`.
Monitorear alertas: `SELECT * FROM PlatformAuditLogs WHERE Action='PlanUpgradeRequiresProviderCancellation'`
⇒ cancelar manualmente esas suscripciones viejas en el panel de TiloPay.
> Modelo de datos: reusar `Planes` + config. Upgrade: V1 completo.

## Progreso
- **F0 DONE**: `BillingCycle` enum, `PlanCodes` (LC_M_/LC_A_ + BuildCalculatorCode), `TilopayRepeatOptions.Calculator`,
  `SubscriptionPricingCatalog` (+ DI). Tests `SubscriptionPricingCatalogTests` (22 mapeos + montos + errores) verdes.
- **F1 DONE**: `TilopayRepeat:Calculator` (22) en `appsettings.json` y `appsettings.Production.json` (JSON válido).
  `Scripts/SeedCalculatorPlans.sql` idempotente (MERGE por Codigo).
- **F2 DONE**: `Plan.BillingCycle` + `Plan.MonthlyEquivalentAmount`; migración `AddPlanBillingCycle` (aditiva,
  default 0=Monthly, decimal(18,2)); `ResolveNextBillingPeriod`/`ActivarSuscripcionRecurrenteAsync` cicladas
  (anual=AddYears(1), guarda equivalente mensual). Tests `AnnualBillingCycleTests` verdes.
- Suite: 489 pass / 6 FAIL **pre-existentes** ajenos al feature (Excel Cobros/Egresos/Funcionarios, BindNever,
  IgnoreQueryFilters whitelist) — provienen del working tree no commiteado, no de la calculadora. Se arregló además
  un helper de test roto (`CreateBillingController` faltaba `ISubscriptionSummaryService`) y una expectativa stale
  (`CheckoutReturn` → `/Billing/Suscripcion`).
- **PENDIENTE aplicar en BD**: migración `AddPlanBillingCycle` + `Scripts/SeedCalculatorPlans.sql` (no ejecutados aún).

## Contexto

Hoy `/Billing/Planes` muestra tarjetas BASIC/PRO/BUSINESS. Queremos reemplazar esa
presentación por **una sola calculadora**: el cliente elige cantidad de funcionarios
(1–11) y ciclo (Mensual/Anual), ve precio dinámico/ahorro/equivalente mensual/próxima
renovación, y cada combinación apunta a un plan recurrente real de TiloPay producción
(LC_M_01..LC_M_11, LC_A_01..LC_A_11). No tocar datos históricos, no romper suscripciones
existentes, idempotencia y aislamiento multi-tenant intactos.

## Decisión de arquitectura

**Reusar la tabla `Planes` + `TilopayRepeat` (config), NO crear tabla paralela.**
Razón: todo el flujo probado (checkout → `PagoSuscripcion.PlanId` FK → webhook resuelve
`id_plan`→config→`Plan` por `Codigo` → `ActivarSuscripcionRecurrenteAsync`) ya es idempotente
y seguro. Una tabla nueva que evite `Planes` obligaría a recablear correlación de webhook,
activación y FKs = el refactor riesgoso que el dueño prohibió.

Para **no confundir mensual con anual** se añaden 2 columnas a `Plan`:
- `BillingCycle` (enum `Monthly`/`Annual`, default `Monthly` → preserva planes legacy).
- `MonthlyEquivalentAmount` (decimal nullable): solo display. `PrecioMensual` pasa a
  significar "monto que cobra TiloPay por ciclo" (mensual=mensual, anual=total anual).

`Suscripcion.PrecioMensual` ya existe; al activar anual guardaremos ahí el
`MonthlyEquivalentAmount` (no el total anual) para que reportes/portal no se ensucien, y
el cobro real/vigencia se derivan de `BillingCycle`.

## Tabla final de pricing (fuente de verdad)

Mensual: charge = precio mensual; equivalente mensual = igual.

| Code | Workers | Cycle | Charge CRC | Equiv/mes | TilopayPlanId | Hosted link (sufijo `tp.cr/l/`) |
|------|---------|-------|-----------:|----------:|--------------:|---------------------------------|
| LC_M_01 | 1 | Monthly | 8 000 | 8 000 | 6119 | TmpFeE9RPT18MQ== |
| LC_M_02 | 2 | Monthly | 15 000 | 15 000 | 6126 | TmpFeU5nPT18MQ== |
| LC_M_03 | 3 | Monthly | 20 000 | 20 000 | 6127 | TmpFeU53PT18MQ== |
| LC_M_04 | 4 | Monthly | 25 000 | 25 000 | 6128 | TmpFeU9BPT18MQ== |
| LC_M_05 | 5 | Monthly | 30 000 | 30 000 | 6129 | TmpFeU9RPT18MQ== |
| LC_M_06 | 6 | Monthly | 35 000 | 35 000 | 6130 | TmpFek1BPT18MQ== |
| LC_M_07 | 7 | Monthly | 40 000 | 40 000 | 6131 | TmpFek1RPT18MQ== |
| LC_M_08 | 8 | Monthly | 45 000 | 45 000 | 6132 | TmpFek1nPT18MQ== |
| LC_M_09 | 9 | Monthly | 50 000 | 50 000 | 6133 | TmpFek13PT18MQ== |
| LC_M_10 | 10 | Monthly | 55 000 | 55 000 | 6134 | TmpFek5BPT18MQ== |
| LC_M_11 | 11 | Monthly | 60 000 | 60 000 | 6135 | TmpFek5RPT18MQ== |
| LC_A_01 | 1 | Annual | 81 600 | 6 800 | 6136 | TmpFek5nPT18MQ== |
| LC_A_02 | 2 | Annual | 153 000 | 12 750 | 6137 | TmpFek53PT18MQ== |
| LC_A_03 | 3 | Annual | 204 000 | 17 000 | 6139 | TmpFek9RPT18MQ== |
| LC_A_04 | 4 | Annual | 255 000 | 21 250 | 6140 | TmpFME1BPT18MQ== |
| LC_A_05 | 5 | Annual | 306 000 | 25 500 | 6141 | TmpFME1RPT18MQ== |
| LC_A_06 | 6 | Annual | 336 000 | 28 000 | 6142 | TmpFME1nPT18MQ== |
| LC_A_07 | 7 | Annual | 360 000 | 30 000 | 6143 | TmpFME13PT18MQ== |
| LC_A_08 | 8 | Annual | 378 000 | 31 500 | 6144 | TmpFME5BPT18MQ== |
| LC_A_09 | 9 | Annual | 390 000 | 32 500 | 6145 | TmpFME5RPT18MQ== |
| LC_A_10 | 10 | Annual | 429 000 | 35 750 | 6146 | TmpFME5nPT18MQ== |
| LC_A_11 | 11 | Annual | 468 000 | 39 000 | 6147 | TmpFME53PT18MQ== |

Ahorro anual = (precio_mensual×12) − charge_anual. Equiv/mes = charge_anual ÷ 12 (todos enteros).
**Validación de montos:** el webhook ya compara el monto aprobado contra
`ExpectedFirstChargeAmount` (= `MonthlyPrice` de config = charge por ciclo). Para anual el
config `MonthlyPrice` = total anual ⇒ la maquinaria de montos exactos funciona sin cambios.

## Nota clave sobre TiloPay (no secreto, sí config)

- Los hosted links e IDs **no son secretos** → van en `appsettings*.json` bajo `TilopayRepeat`
  (mismo patrón que Basic/Pro/Business/TestProdBasic100). El `access_token`/credenciales
  siguen SOLO en config segura (`Tilopay:*`), nunca en código.
- **No existe API de cancelación saliente** en `TilopayService` (solo login/createLinkPayment/
  consult). Por eso el upgrade cancela la suscripción vieja de forma **manual con alerta admin**.
- Único cambio de comportamiento para anual: la **vigencia** (`AddMonths(1)`→`AddYears(1)` según
  ciclo). El `NextBillingDateUtc` del webhook sigue teniendo prioridad cuando viene.

## Fases de ejecución

### Fase 0 — Catálogo + resolución (zero-riesgo, aditivo)
- `Models/Saas/PlanCodes.cs`: agregar `LC_M_01..11`, `LC_A_01..11` + arrays + helpers
  `IsCalculatorPlanCode`, `Build(workers, cycle)`.
- `Models/Saas/BillingCycle.cs` (nuevo enum) y `TilopayRepeatPlanOption.BillingCycle` +
  `MonthlyEquivalentAmount`. Agregar colección `Calculator: List<TilopayRepeatPlanOption>` a
  `TilopayRepeatOptions`, e incluirla en `GetAllPlans()`/`FindByCode`/`FindByRecurringPlanId`/
  `IsManagedPlanCode`/`ResolveSectionKey` (sin romper las propiedades fijas existentes).
- `Services/SaaS/SubscriptionPricingCatalog.cs` (nuevo): `Resolve(workers, cycle)` →
  `PricingOption` (Code, WorkerCount, BillingCycle, ChargeAmount, MonthlyEquivalentAmount,
  Currency, TilopayRecurringPlanId, CheckoutUrl, IsActive, IsPublic, SortOrder) +
  validación "configurada o error visible" (links/montos faltantes ⇒ no comprable).
- **Tests (mandatorios #1–22 + montos):** `SubscriptionPricingCatalogTests`.

### Fase 1 — Config + datos
- `appsettings.json` / `appsettings.Production.json`: 22 entradas `TilopayRepeat:Calculator[]`
  con `Code/TilopayPlanId/MonthlyPrice(=charge)/MonthlyEquivalentAmount/MaxFuncionarios/
  BillingCycle/CheckoutUrl/UsesRecurringCheckout=true/IsPublic=true`. `Enabled=true` en prod.
- `Scripts/SeedCalculatorPlans.sql` (idempotente, patrón de `TestProdBasic100Plan.sql`):
  upsert de 22 filas `Planes` (Codigo, MaxFuncionarios=workers, PrecioMensual=charge,
  BillingCycle, MonthlyEquivalentAmount). GUIDs deterministas.

### Fase 2 — Ciclo anual en activación
- `Plan.BillingCycle` + `Plan.MonthlyEquivalentAmount` + **migración EF** `AddPlanBillingCycle`
  (aditiva, defaults seguros). Snapshot revisado antes de aplicar.
- `Services/Stripe/SuscripcionService.cs`: `ResolveNextBillingPeriod` recibe `BillingCycle`
  (anual ⇒ `AddYears(1)`). `ActivarSuscripcionRecurrenteAsync` usa `plan.BillingCycle` y
  guarda `Suscripcion.PrecioMensual = MonthlyEquivalentAmount` para anual.
- Tests de vigencia mensual vs anual.

### Fase 3 — Calculadora UI + checkout por (workers, cycle)
- `Views/Billing/Planes.cshtml` (público) y `Suscripcion.cshtml` (privado): una sola
  calculadora (range 1–11 con marcadores, segmented Mensual/Anual, panel dinámico JS sin
  recarga: cantidad, límite, pago hoy, equiv/mes, ahorro, próxima renovación, código interno).
  Botón con estado: "Suscribirme"/"Cambiar plan"/"Aumentar funcionarios". `wwwroot/css/subscription.css`.
- Ocultar BASIC/PRO/BUSINESS legacy de la UI pública (sin borrarlos).
- `BillingController`: nuevo `POST CheckoutCalculadora(int workers, string cycle)` →
  resuelve `PricingOption` server-side (ignora monto del cliente), valida auth/tenant/rango/
  ciclo/IsPublic/IsActive, deriva `Plan` por `Code` y llama `CreateRecurringCheckoutAsync`
  (reusa pending por TenantId+Code+Amount+Currency ⇒ anti doble-click; botón se deshabilita en
  front como defensa secundaria). Validar que el link existe o error de config (no comprable).

### Fase 4 — Aumentar funcionarios (upgrade) — V1 completo
- `Models/Saas/PlanChangeIntent.cs` (nuevo) + migración `AddPlanChangeIntent`
  (TenantId, FromPlanId, ToPlanCode, ToWorkerCount, BillingCycle, Estado
  [Pending/Paid/Applied/Failed/Cancelled], PagoSuscripcionId, índice único parcial
  "1 intent abierto por tenant" ⇒ anti doble-cambio).
- Estados nuevos en `Suscripcion`: `PendingProviderCancellation`/`Superseded` (o flag) para
  la suscripción vieja mientras se cancela manualmente en TiloPay.
- Reglas: no permitir bajar de los funcionarios activos existentes (cuenta real de
  `Funcionarios`); calculadora con mínimo = actuales. Al confirmarse el pago nuevo: subir
  `MaxFuncionarios`, marcar la vieja `PendingProviderCancellation` y **emitir alerta admin**
  (extender `INotificationService` + `PlatformAuditLog`) "cancelar suscripción anterior en
  TiloPay". Nunca mostrar "todo bien" con 2 suscripciones proveedor activas.
- Aplicar una sola vez por `ProviderTransactionId`; pago fallido ⇒ se mantiene plan actual.

### Fase 5 — Pruebas, runbook y verificación
- Idempotencia: doble/triple click, 2 pestañas, registration/success duplicados, return antes/
  después de webhook, failed→retry, success tardío de intento viejo, tx repetida no extiende 2x.
- Seguridad: no auth ⇒ no checkout; cross-tenant; manipular workers/PlanCode/amount; email no
  es identificador; monto/moneda/PlanId incorrectos ⇒ ManualReview; anual no activa mensual.
- Runbook (publicar con app detenida), queries de verificación prod, comandos journalctl/nginx.

## Riesgos / pendientes
- Suscripciones existentes BASIC/PRO/BUSINESS y TEST_PROD_BASIC_100 quedan intactas (legacy,
  ocultas de UI). Dray sin tocar.
- Cancelación de la suscripción vieja en upgrade es **manual** (sin API) ⇒ depende de que el
  admin actúe sobre la alerta; el sistema marca el estado y alerta, no improvisa.
- Cambios anuales a mitad de ciclo (downgrade/prorrateo): manual, fuera de V1 automático.
- El proyecto de tests debe compilar (hubo aviso de tests untracked rotos) — validar antes.

## Verificación end-to-end
1. `dotnet test` del proyecto `LuxuryApp.Tests` (con la app detenida) — verde, incl. los 22
   tests de resolución + montos + vigencia anual + idempotencia.
2. Levantar la app y abrir `/Billing/Planes`: mover el range 1→11 y togglear Mensual/Anual,
   confirmar precios/ahorro/equiv/renovación/código sin recarga, móvil y temas.
3. Checkout real de bajo riesgo (p.ej. LC_M_01) en sandbox/prod controlado: pending creado →
   repeat_registration → repeat_payment_success → suscripción activa + `MaxFuncionarios` = 1.
4. Queries prod: opciones (`Planes` LC_*), pending (`PagosSuscripcion`), `EventosPago`,
   `Suscripciones` con `BillingCycle`/`FechaProximoCobroUtc` a 12 meses para anual.
