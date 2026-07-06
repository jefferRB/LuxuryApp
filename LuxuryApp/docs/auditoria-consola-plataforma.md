# Auditoría — Consola de Administración de Plataforma (LuxuryCloud)

Fecha: 2026-07-04
Rol: Staff Software Architect (SaaS B2B multi-tenant)
Alcance: `Controllers/Platform/*`, `Services/Platform/*`, `Models/Platform/*`, `Views/Platform/*`, `Views/PlatformBillingHealth/*`, `Services/Billing/BillingHealthService.cs`, infraestructura transversal (Program.cs, identidad, tenancy, workers). Solo análisis; sin cambios de código.

---

## Inventario actual del módulo

| Pantalla | Ruta | Estado general |
|---|---|---|
| Dashboard principal | `/Platform` | Sobrecargado: gobierno comercial + WhatsApp + billing + usuarios en una sola página de 775 líneas |
| Usuarios | `/Platform/Usuarios` | Sólido: búsqueda, filtros, paginación (50), desactivación blindada |
| Auditoría | `/Platform/Auditoria` | Sólido: filtros + paginación sobre `PlatformAuditLog` |
| Ficha de tenant | `/Platform/Tenants/{id}/Ficha` | Muy buena: vista 360 (salud, billing, WhatsApp, reservas, usuarios, auditoría) |
| Códigos promocionales | `/Platform/PromotionalCodes` | Funcional; toggle sin auditoría |
| Conciliación recurrente | `/Platform/RecurringCheckouts` | Funcional; aprobación manual auditada vía `SaaSPaymentService` |
| Billing Health | `/Platform/BillingHealth` (+ `/json`) | La mejor pieza: fotografía operativa accionable |
| Resumen mensual | `/Platform/MonthlyReports` | Sólido: config por tenant, envío test/real con confirmación escrita |

Servicios: `PlatformMetricsService` (uso por tenant, 8 queries batch), `PlatformWhatsAppStatusService` (4 queries batch), `PlatformHealthService` (score con motivos), `PlatformAuditService`, `PlatformUserAdminService`, `PlatformTenantProfileService`, `BillingHealthService`, `PlatformMonthlyReportService`.

---

## Fase 1 — Auditoría

### Lo que está bien y debe conservarse tal cual

1. **Modelo de salud por tenant con motivos** (`PlatformHealthService`): estados `Saludable/Atención/Riesgo/SinAcceso` con razones acumuladas. Es exactamente el patrón correcto (estado accionable, no contador).
2. **Billing Health** (`BillingHealthService` + vista): responde preguntas operativas reales (webhooks sin procesar, dinero cobrado sin activar, renovaciones vencidas, última reconciliación) con umbrales de color por antigüedad. Además expone `/json` para monitoreo externo autenticado.
3. **Flujo de desactivación de usuarios** (`PlatformUserAdminService`): re-verificación de contraseña del SuperAdmin, confirmaciones escritas server-side, anti-IDOR (tenant esperado vs real), reglas de "último SuperAdmin"/"último admin del tenant", transacción, invalidación de sesión vía security stamp, rate-limit de intentos fallidos basado en auditoría. Nivel empresarial.
4. **Bitácora `PlatformAuditLog`**: append-only por convención, actor + IP + UserAgent + before/after JSON + motivo. Con acciones automáticas del reconciliador (`BillingReconciliationAlert`, `BillingAutoRepairApplied`).
5. **Reconciliación automática de Billing** (worker diario + reglas conservadoras: solo repara lo inequívoco, alerta lo ambiguo).
6. **Batching consciente**: `GetTenantUsageBatchAsync` (8 queries fijas) y `GetBatchStatusAsync` (4 queries) evitan N+1 explícitamente. La intención arquitectónica es correcta; el problema es de volumen, no de patrón.
7. **Ficha de tenant 360**: identidad, score, plan, add-ons, pagos, WhatsApp, reservas, usuarios, trazabilidad. Es la página que Stripe/Clerk llamarían "customer detail" y ya existe.

### Problemas encontrados

**Estructura y navegación**
- P1. El dashboard `/Platform` mezcla 4 dominios (gobierno comercial, WhatsApp, billing, usuarios) en una página monolítica. La navegación interna es ad-hoc: botones en el hero + links sueltos a RecurringCheckouts/BillingHealth. No hay navegación persistente del módulo; BillingHealth y RecurringCheckouts son "pantallas huérfanas" a las que solo se llega desde el dashboard.
- P2. La edición inline (formulario comercial por fila + modal WhatsApp por tenant) genera un DOM enorme: N formularios + N modales renderizados siempre. La edición pertenece a la Ficha, no a la lista.

**Información redundante o sin valor operativo**
- R1. KPI "Usuarios" (`TotalUsers`) y "Códigos promocionales" (`TotalPromotionalCodes`): contadores decorativos; no cambian ninguna decisión diaria.
- R2. Sección "Usuarios visibles": dice "usuarios más recientes" pero ordena por `IsPlatformSuperAdmin` + email (no existe fecha de creación). Información engañosa y duplicada con `/Platform/Usuarios`.
- R3. La tabla "WhatsApp por tenant" repite los N tenants de la tabla superior con otras columnas. Dos tablas de N filas en la misma página.
- R4. "Suscripciones activas / Base y add-ons" duplica parcialmente lo que BillingHealth y la Ficha ya muestran mejor.
- R5. Columnas técnicas crudas en el dashboard (CorrelationId, RecurringPlanId, Tx, Subscriber) pertenecen a la vista de detalle/conciliación, no al resumen.

**Información faltante (accionable)**
- F1. No hay MRR/ARR ni desglose comercial (el dato existe: `Plan.PrecioMensual`, `BillingCycle`, suscripciones y add-ons activos).
- F2. No hay señal agregada de "cuántos tenants están en cada estado de salud" (el estado se calcula por fila pero no se resume).
- F3. No hay trials por vencer, morosos, ni cobros fallidos en el dashboard principal (están enterrados en BillingHealth).
- F4. No hay salud técnica del sistema (BD, disco, correo, Meta/Tilopay alcanzables, workers vivos).
- F5. La primera pantalla no responde "¿hay algo roto HOY?": hay que leer 6 tablas.

**Arquitectura / mantenibilidad**
- A1. `PlatformController.Index` (300+ líneas) hace de agregador de todo; lógica de armado de viewmodels en el controlador (`ResolveCheckoutKind`, lookups `FirstOrDefault` O(N²)). Falta un `PlatformDashboardService`.
- A2. Códigos de add-on WhatsApp (WA400/WA800/WA1200) hardcodeados en la vista `Index.cshtml`; ya existe `PlanCodes.WhatsAppAddons` como catálogo.
- A3. `RecurringReconciliationController.ResolveAccess()` contiene una rama de acceso para "Administrador en Development" que es código muerto: el atributo de clase ya exige la política `PlatformSuperAdmin`. Confunde el modelo de seguridad al leerlo.
- A4. `PlatformAuditService.LogAsync` hace `SaveChangesAsync` sobre el DbContext compartido del request: si en el futuro alguien audita con cambios pendientes sin guardar, los comitea accidentalmente. Footgun latente.
- A5. 282 usos de `IgnoreQueryFilters` en 40 archivos. En Platform es necesario (cross-tenant), pero el patrón "opt-out disperso" depende de disciplina; ya está parcialmente mitigado por tests de whitelist (`EndpointBindingSecurityTests`).

**Rendimiento (dashboard principal)**
- E1. Carga TODOS los tenants sin paginación y ejecuta `ResolveAsync` por tenant dentro del loop (caché 2 min; en frío 1–3 queries por tenant). Con 100 tenants: ~25 + hasta 300 queries. Con 1.000: hasta ~3.000.
- E2. `latestSubscriptions` trae TODAS las suscripciones Tilopay sin `Take` y agrupa en memoria.
- E3. `GetBatchStatusAsync` (query 3) trae TODAS las filas de error de 30 días de todos los tenants a memoria antes de agrupar; query 4 trae todos los addons.
- E4. `BillingHealthService` materializa todas las suscripciones y addons (aceptable hoy; techo ~5–10k filas).
- E5. Lookups `tenants.FirstOrDefault(...)` dentro de `Select` en 4 lugares → O(N×M).

**Operación / auditoría**
- O1. `SafeAuditAsync` traga todas las excepciones sin loggear: un fallo persistente de auditoría pasaría inadvertido (pérdida silenciosa de trazabilidad).
- O2. `TogglePromotionalCode` no audita (activar/desactivar un instrumento que regala acceso).
- O3. Cambio de modo comercial a Exempt/Internal (regalar acceso permanente) se guarda con un submit simple, sin confirmación reforzada; sí queda auditado.
- O4. Sin retención/archivado: `EventosPago`, `WhatsAppMessageLogs`, `PlatformAuditLogs`, `Notificaciones` crecen sin límite. No existe ningún job de limpieza (solo hay `ExecuteDelete` en CobroService).

### Decisión por sección (conservar / modificar / eliminar)

| Sección actual | Decisión | Justificación |
|---|---|---|
| KPI Tenants | Conservar (enriquecer) | Útil como contexto; mejor "Tenants: N (X en riesgo)" |
| KPI Usuarios | Eliminar del dashboard | Decorativo; vive en /Usuarios |
| KPI Suscripciones activas | Modificar | Sustituir por desglose Activas/Trial/Morosas (ya existe en BillingHealth) |
| KPI Códigos promocionales | Eliminar del dashboard | Decorativo; vive en /PromotionalCodes |
| Tabla Tenants + edición inline | Modificar | Lista paginada/filtrable de solo lectura + acciones en Ficha |
| Tabla WhatsApp por tenant | Modificar | Fusionar en la lista de tenants (columna estado) + página propia "WhatsApp" que muestre SOLO tenants con add-on o con errores |
| Usuarios visibles | Eliminar | Redundante y con orden engañoso |
| Pagos recientes | Modificar | Conservar solo si estado ≠ Confirmado destaca; enlazar a conciliación |
| Checkouts pendientes | Conservar | Accionable (dinero en juego); mover detalle técnico a Conciliación |
| Eventos de webhooks | Modificar | En dashboard solo el contador de errores/pendientes; tabla completa en Billing Ops |
| Suscripciones y add-ons activos | Modificar | Mover a módulo Comercial; en dashboard solo agregados |
| BillingHealth | Conservar | Referencia de diseño; integrarlo a la navegación |
| RecurringCheckouts | Conservar | Limpiar código muerto de acceso dev |
| Usuarios / Auditoría / Ficha / MonthlyReports / Códigos | Conservar | Funcionan bien; ajustes menores |

---

## Fase 2 — Rediseño conceptual

### Arquitectura de información propuesta (navegación persistente del módulo)

```
Plataforma
├── Inicio            (solo lo accionable)
├── Tenants           (lista → Ficha 360, aquí vive TODA la edición)
├── Comercial         (MRR, trials, churn, renovaciones, add-ons)
├── Billing Ops       (checkouts pendientes + webhooks + conciliación + BillingHealth)
├── WhatsApp          (tenants con add-on, cuotas, errores, diagnóstico Meta)
├── Usuarios          (existente)
├── Códigos           (existente)
├── Resumen mensual   (existente)
├── Salud del sistema (nuevo — Fase 3)
└── Auditoría         (existente)
```

Regla: el dashboard **no muestra nada que no dispare una acción**. Cada tarjeta enlaza al módulo con el filtro aplicado.

### Dashboard "Inicio" — tarjetas propuestas

Fila 1 — ¿Está sano el sistema? (semáforos, no números)
1. **Salud técnica**: verde/amarillo/rojo agregado de los checks de Fase 3. Responde "¿hay procesos detenidos?".
2. **Dinero en riesgo**: pagos en ManualReview + renovaciones vencidas + morosos. Monto + conteo. Rojo si > 0.
3. **Webhooks**: errores/sin procesar 24h + minutos desde el último recibido. Responde "¿errores de webhooks?".
4. **WhatsApp**: tenants con errores 24h + tenants ≥90% de cuota. Responde "¿fallos en WhatsApp?".

Fila 2 — ¿Cómo está el negocio?
5. **MRR** + variación vs mes anterior.
6. **Tenants por salud**: Saludables / Atención / Riesgo / Sin acceso (chips clicables → lista filtrada).
7. **Trials**: activos + los que vencen en ≤7 días.
8. **Pendiente de acción humana**: checkouts por conciliar + reservas pendientes >3 por tenant + alertas de reconciliación abiertas.

Todo lo anterior existe ya como dato o query; es reagrupación, no infraestructura nueva. Un `PlatformDashboardService` debe componerlo (sacándolo del controlador) con caché de 30–60 s.

---

## Fase 3 — Observabilidad

Realidad del stack: 1 servidor Linux (Nginx + systemd), SQL Server, `IMemoryCache` (NO hay Redis), workers `IHostedService` in-process (no hay colas externas; las "colas" son estados en tablas: notificaciones pendientes, eventos sin procesar). No hay Serilog/OpenTelemetry/health checks hoy. Recomendación honesta: **no montar Prometheus/Grafana todavía**; primero exponer señales que ya existen.

### 3.1 Health checks estándar (ASP.NET Core `AddHealthChecks`)
- `/health/live`: proceso vivo (para systemd/uptime monitor externo).
- `/health/ready` (protegido o solo localhost): SQL Server (`SELECT 1` + latencia), espacio en disco (umbral 85%), DataProtection keys accesibles.

### 3.2 Página "Salud del sistema" (`/Platform/SystemHealth`) — mismo patrón que BillingHealth
| Señal | Fuente (ya existe) | Umbral |
|---|---|---|
| BD: latencia ping + conexiones | query trivial cronometrada | >500 ms amarillo |
| Disco | `DriveInfo` | >85% amarillo, >95% rojo |
| Workers vivos | tabla heartbeat (worker escribe timestamp por ciclo) | Reminder >5 min rojo; Reconciliación >26 h amarillo (igual que hoy) |
| Correo (Resend) | último envío OK / fallos 24h (log de envíos) | fallos>0 amarillo |
| WhatsApp Meta | errores outbound 24h + `TestConfigurationAsync` bajo demanda | errores>0 amarillo |
| Tilopay | último webhook recibido/procesado (ya en BillingHealth) | 48/96 h |
| Errores app recientes | tail de journald o tabla de errores 24h | >0 amarillo |
| Memoria/CPU proceso | `Process.GetCurrentProcess()` (WorkingSet, CPU) | informativo |

- **Heartbeat de workers es lo más importante**: hoy si `ReminderWorker` muere silenciosamente, nadie lo sabe hasta que un cliente reclama que no llegan recordatorios.
- Exponer `/Platform/SystemHealth/json` (como BillingHealth) y conectarle un monitor externo barato (UptimeRobot/Healthchecks.io) → alertas sin construir sistema de alertas propio.
- Logging estructurado: adoptar Serilog con sink a archivo con rotación (o journald) + enriquecimiento TenantId (el `BeginScope` ya existe en `TenantExecutionService`). OpenTelemetry: futuro, no ahora.
- No inventar métricas de Redis/colas que no existen; agregarlas el día que exista esa infraestructura.

---

## Fase 4 — Comercial

Nuevo módulo `/Platform/Commercial` respaldado por un `PlatformCommercialMetricsService` (snapshot cacheado 5–15 min; opcionalmente persistido mensualmente para histórico).

Definiciones (todas computables con el modelo actual):
- **MRR** = Σ `Plan.PrecioMensual` de suscripciones con estado efectivo Activa/Morosa (excluir Trial y Exempt/Internal) + Σ add-ons activos (mensualizado si `BillingCycle=Annual`). **Morosos se muestran como "MRR en riesgo"**, separado.
- **ARR** = MRR × 12 (etiquetarlo como proyección; no inventar contratos anuales).
- **Trials**: activos, vencen ≤7d, conversión (trial→pago) por cohorte mensual — la fecha de trial y el primer pago confirmado ya existen.
- **Churn mensual** = tenants que pasaron a Cancelada/Suspendida en el mes / activos al inicio. `HistorialSuscripcion` da la trazabilidad.
- **Cobros fallidos**: `PagosSuscripcion` Fallido/Cancelado 30d + reintentos pendientes.
- **Renovaciones**: próximas 7/30 días (`FechaProximoCobroUtc`) con monto esperado → "Próximos cobros".
- **Upgrades/Downgrades**: ya existe `PlanChangeIntent`/`PlanChangeService`; contar por mes y listar el pendiente de cancelación manual en proveedor (`PlanUpgradeRequiresProviderCancellation`).
- **Add-ons**: activos por tipo, MRR de add-ons, altas/bajas del mes.
- **Ingresos reales**: Σ pagos Confirmados por mes (línea 12 meses) — es caja real, complementa MRR.

Vista: 6 tarjetas (MRR, MRR en riesgo, ARR, Trials, Churn, Cobros fallidos) + tabla "Próximos cobros" + tabla "Movimientos del mes" (altas, upgrades, downgrades, cancelaciones). Sin gráficas complejas al inicio: números + deltas.

---

## Fase 5 — Tenants

### Lista (`/Platform` → futuro `/Platform/Tenants`)
Columnas: **Tenant** (nombre + owner) · **Salud** (badge con tooltip de motivos — ya existe) · **Plan efectivo + modo** · **Uso 30d compacto** (citas·cobros·reservas pendientes) · **WhatsApp** (un solo badge: Sin add-on / OK / Error / Cuota) · **Última actividad** → botón único "Ficha".
- Quitar de la lista: formularios inline, modales por fila, notas comerciales, timezone, detalle de cuota diaria (todo eso vive en la Ficha).
- Agregar: filtros por salud/estado/plan + búsqueda + paginación (mismo patrón ya probado en Usuarios) + orden por "peor salud primero".

### Ficha (ya excelente — cambios mínimos)
- Mover aquí la edición comercial y de WhatsApp (los forms/modales que hoy están en el dashboard).
- Agregar: MRR del tenant, fecha exacta de próximo cobro, botón "Ver auditoría completa" filtrada.
- `GetFichaAsync` ejecuta ~20 queries secuenciales: aceptable para una página de detalle; no tocar hasta que moleste.

Principio: **la lista diagnostica, la ficha opera**. Evitar información decorativa: si un dato no cambia una decisión (ej. timezone en la lista), no va.

---

## Fase 6 — Seguridad

### Controles verificados que ya están bien (mantener)
- Autorización fail-closed global (`RequireAuthenticatedUser` como filtro global) + política `PlatformSuperAdmin` por claim en TODOS los controladores Platform.
- Doble aislamiento: filtros globales EF por `ITenantEntity` + guardas de escritura en `SaveChanges` + **RLS real en SQL Server** (`TenantSecurityPolicy` con `fnTenantAccess` filter/block predicates vía `SESSION_CONTEXT`, seteado/limpiado por `TenantSessionConnectionInterceptor`).
- Validación de sesión por request (`SecurityStampValidatorOptions.ValidationInterval=Zero` + `TenantSessionSecurityValidator`: usuario existe, activo, no bloqueado, tenant del claim == tenant real, tenant activo).
- Cookies: HttpOnly, SameSite=Lax, Secure en prod, expiración 8h sliding. Antiforgery en todos los POST del módulo. Headers: nosniff, X-Frame-Options DENY, Referrer-Policy, Permissions-Policy, HSTS.
- Acciones peligrosas con re-autenticación por contraseña + confirmaciones escritas + rate-limit + auditoría (usuarios) y confirmación "ENVIAR" (envío real de correos).
- Webhooks Stripe/Meta con verificación de firma; idempotencia por índices únicos de evento/transacción.
- Suite de tests de aislamiento (`LuxuryApp.Tests/TenantIsolation/*`, whitelist de `IgnoreQueryFilters`, binding de `TenantId` con `BindNever`).

### Hallazgos (orden por severidad)

| # | Hallazgo | Severidad | Detalle / recomendación |
|---|---|---|---|
| S1 | **Sin MFA para PlatformSuperAdmin** | Alta | La cuenta más poderosa del sistema entra solo con contraseña. Identity ya trae TOTP (`AddDefaultTokenProviders`; columnas existen). Implementar TOTP **obligatorio** para superadmins; opcional para admins de tenant. |
| S2 | **Password mínimo 5 caracteres** (`Program.cs:135`) | Alta | Inaceptable para superadmin. Subir a 12 para superadmin (validator condicional) y ≥8 global. Lockout de 1 min / 3 intentos es corto: subir a 15 min progresivo. |
| S3 | **Revocación de privilegio no expulsa la sesión** | Media-Alta | `TenantSessionSecurityValidator` no compara el claim `platform_super_admin` contra `user.IsPlatformSuperAdmin` actual (solo lo usa para saltarse el check de tenant activo). Si se revoca el flag en BD, la sesión conserva el poder hasta logout/8h. Fix barato: en el validator, si el claim dice True y la BD dice false → rechazar principal. |
| S4 | **CSP ausente** | Media | Pendiente del audit de junio. Scripts inline en layouts/vistas lo bloquean. Ruta: nonces + `script-src 'self' 'nonce-…' + CDNs` empezando en Report-Only. |
| S5 | **Webhook Tilopay con token estático en query** | Media | Pendiente del audit de junio. Si Tilopay no soporta HMAC: token de alta entropía + rotación + allowlist de IP en Nginx + rate limit del endpoint. |
| S6 | **Auditoría best-effort silenciosa** | Media | `SafeAuditAsync` traga excepciones sin loggear (`PlatformController.cs:62`). Mínimo: `_logger.LogError`. Ideal: contador de fallos de auditoría visible en Salud del sistema. |
| S7 | **Auditoría no inmutable criptográficamente** | Media | Append-only por convención (sin endpoint de borrado), pero un acceso a BD la altera sin rastro. Bajo costo: hash encadenado (`RowHash = SHA256(prev.RowHash + campos)`) verificable; opcional exportación periódica externa. |
| S8 | Claves DataProtection sin cifrar en disco | Media | `PersistKeysToFileSystem` sin `ProtectKeysWith*`. En Linux single-server las opciones son limitadas: mínimo permisos 700 al directorio y respaldo; documentarlo. |
| S9 | Sin rate limiting en login ni en `/Platform` | Media | El limiter solo cubre `/reservar/*`. Identity lockout mitiga fuerza bruta por usuario, pero no password spraying. Añadir política por IP a `/Accounts/Acceso` (y opcionalmente allowlist de IP para `/Platform` en Nginx). |
| S10 | Código muerto de acceso dev en conciliación | Baja | `RecurringReconciliationController.ResolveAccess` rama Development+Administrador inalcanzable (la policy de clase ya bloquea). Eliminarla para no confundir el modelo de seguridad. |
| S11 | `TogglePromotionalCode` sin auditoría | Baja | Acción comercial sin rastro. Añadir `SafeAuditAsync`. |
| S12 | Diagnóstico Meta expone configuración en JSON al navegador | Baja | IDs de WABA/phone (no secretos). Aceptable interno; no ampliar el payload. |
| S13 | PII en logs (emails) | Baja | Pendiente del audit de junio; enmascarar. |
| S14 | SameSite=Lax global | Baja | Correcto para la app; para rutas `/Platform` podría emitirse cookie adicional Strict — solo si se busca endurecimiento extra; no urgente. |
| S15 | Sesión 8h sliding también para superadmin | Baja | Considerar timeout absoluto (p.ej. 12h) + idle 1h para superadmins. |

Principio de mínimo privilegio: hoy existe un solo nivel (SuperAdmin todo-poderoso). Con un solo operador es razonable; si el equipo crece, separar roles (Soporte solo-lectura vs Billing vs Owner) — diseño futuro, no ahora (evitar sobreingeniería).

Tenant escape: no encontré vías de escape en el módulo Platform (todas las mutaciones validan existencia del tenant y las lecturas cross-tenant son deliberadas con `IgnoreQueryFilters` bajo policy). El anti-IDOR de usuarios (tenant esperado vs real) es un buen patrón; replicarlo si se agregan más mutaciones.

---

## Fase 7 — Escalabilidad

| Escala | Diagnóstico |
|---|---|
| 100 tenants | Funciona. Dashboard lento en frío (hasta ~300 queries por el resolver por-tenant) pero tolerable. |
| 1.000 tenants | Dashboard inviable (~3.000 queries en frío, DOM con 1.000 modales, tablas sin paginar). `ReminderWorker` tarda: escaneo secuencial de todos los tenants cada minuto. `GetBatchStatusAsync`/`BillingHealth` materializan tablas completas. |
| 10.000 tenants | Rompe: además de lo anterior, 2 queries de validación de sesión por request × usuarios concurrentes castigan la BD; tablas de log sin retención (webhooks, WhatsApp, auditoría) en decenas de millones de filas; caché/estado en memoria impide escalar a 2+ instancias. |

### Cuellos de botella concretos y su solución (antes de que duelan)

1. **Dashboard O(N)** → paginación + filtros server-side (patrón Usuarios ya existente); resolver acceso comercial en batch (1 query de suscripciones vigentes + 1 de grants, en vez de `ResolveAsync` por tenant); tarjetas agregadas con `GROUP BY` en SQL.
2. **Modales/forms por fila** → mover edición a la Ficha (elimina el problema de DOM).
3. **Queries "cargar todo y agrupar en memoria"** (`latestSubscriptions`, errores WA, addons, BillingHealth) → `GROUP BY`/`ROW_NUMBER` en SQL o subqueries `Take(1)` por tenant; índices compuestos: `WhatsAppMessageLogs(TenantId, Direction, CreatedAtUtc)`, `EventosPago(EstadoProcesamiento, FechaRecepcionUtc)`, `PagosSuscripcion(Estado, FechaCreacionUtc)`, `Citas(TenantId, FechaHoraCita)`, `Cobros(TenantId, FechaCobro)`.
4. **`ReminderWorker` cada minuto × todos los tenants** → primero filtrar en 1 query los tenants con WhatsApp habilitado y con trabajo pendiente (hoy crea scope por tenant aunque no tenga nada); a mediano plazo, pasar de "scan por tenant" a "scan por trabajo" (query global de notificaciones vencidas). |
5. **Validación de sesión 2 queries/request** → correcta hoy; a futuro cache 30–60 s por userId con invalidación por security stamp (compromiso deliberado de ventana corta). No tocar hasta >1k usuarios concurrentes.
6. **Estado en memoria single-instance** (`TenantCommercialAccessCache`, `IMemoryCache`, workers in-process) → mientras haya 1 instancia, documentarlo como restricción explícita de despliegue. El día que se necesite 2ª instancia: Redis para caché compartido + lock distribuido (o instancia dedicada de workers). No introducir Redis antes de eso.
7. **Crecimiento sin retención** → job mensual de archivado/purga: webhooks procesados >12 meses, WhatsApp logs >6 meses (agregando conteos mensuales por tenant antes de purgar), auditoría nunca se borra (solo archivado externo).
8. **Miles de webhooks/pagos/mensajes al día**: el diseño actual (idempotencia por índice único + estados en tabla + reconciliación diaria) escala bien; el límite es el procesamiento síncrono en el request del webhook. Si el volumen crece: encolar en tabla (estado Recibido) y procesar en worker — el modelo de datos ya lo soporta (`EstadoProcesamiento`).

---

## Fase 8 — Roadmap de implementación

### Ola 1 — Crítico (seguridad y no-regresión operativa) · 1–2 semanas
| Ítem | Impacto | Complejidad | Riesgo | Dependencias |
|---|---|---|---|---|
| MFA TOTP obligatorio superadmin (S1) | Alto | Media | Bajo (Identity nativo) | — |
| Password policy + lockout (S2) | Alto | Baja | Bajo | Comunicar a usuarios existentes |
| Validator compara claim superadmin vs BD (S3) | Alto | Baja | Bajo | — |
| Log en `SafeAuditAsync` + auditar toggle de códigos (S6, S11) | Medio | Trivial | Nulo | — |
| Borrar código muerto de acceso dev (S10) | Medio | Trivial | Nulo | — |
| Heartbeat de workers + `/health` + monitor externo | Alto | Baja | Bajo | — |

Beneficio: cierra los huecos que un incidente real explotaría primero; da visibilidad de procesos detenidos (hoy ciegos).

### Ola 2 — Importante (consola operable y escalable) · 2–4 semanas
| Ítem | Impacto | Complejidad | Riesgo | Dependencias |
|---|---|---|---|---|
| `PlatformDashboardService` + dashboard accionable (Fase 2) | Alto | Media | Medio (no romper flujos: mantener rutas) | — |
| Lista de tenants paginada/filtrable; edición movida a Ficha | Alto | Media | Medio | Dashboard service |
| Resolver comercial en batch + fix queries E2/E3/E5 + índices | Alto | Media | Bajo (solo lectura) | — |
| Navegación persistente del módulo (sidebar/tabs) | Medio | Baja | Bajo | — |
| Página Salud del sistema (Fase 3) | Alto | Media | Bajo | Heartbeats Ola 1 |

### Ola 3 — Recomendado (comercial y hardening) · 1–2 meses
| Ítem | Impacto | Complejidad | Riesgo | Dependencias |
|---|---|---|---|---|
| Módulo Comercial (MRR/trials/churn/próximos cobros) | Alto | Media-Alta | Bajo (solo lectura) | Definiciones Fase 4 |
| CSP con nonces (S4) + hardening webhook Tilopay (S5) | Medio | Media | Medio (probar UI completa) | Inventario scripts inline |
| Retención/archivado de logs | Medio | Media | Medio (borra datos: dry-run + backup) | Definir políticas |
| Serilog estructurado + máscara de PII (S13) | Medio | Baja | Bajo | — |
| ReminderWorker: filtrar tenants con trabajo | Medio | Baja | Medio (probar recordatorios) | — |

### Ola 4 — Futuro (cuando el negocio lo pida)
- Hash-chain en auditoría (S7) y exportación externa.
- Roles de plataforma (soporte solo-lectura) cuando haya >1 operador.
- Redis + workers extraídos cuando se necesite 2ª instancia.
- Snapshot mensual persistido de métricas comerciales (histórico real de MRR/churn).
- OpenTelemetry/APM si el diagnóstico de latencias lo justifica.
- Webhooks a procesamiento asíncrono si el volumen lo exige.

### Reglas de ejecución
1. Nunca mezclar en un mismo PR cambios de seguridad con rediseño de UI.
2. Toda mejora de queries es de solo lectura → riesgo bajo, se puede validar comparando resultados contra la versión actual.
3. Mantener las rutas existentes (`/Platform`, `/Platform/BillingHealth`, etc.) para no romper marcadores ni hábitos.
4. Cada ola termina con: build + tests de aislamiento verdes + prueba manual de los 8 flujos del inventario.
