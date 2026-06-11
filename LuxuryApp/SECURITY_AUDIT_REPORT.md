# Security Audit Report - LuxuryCloud

Fecha: 2026-06-11
Rama: `codex-security-audit`
Alcance: repositorio local LuxuryApp/LuxuryCloud, sin pruebas destructivas ni contra producción.

## Resumen ejecutivo

No se confirmaron vulnerabilidades críticas explotables de cross-tenant/IDOR en los módulos revisados. La aplicación tiene controles fuertes ya existentes: autorización global por defecto, controladores sensibles con rol `Administrador`, filtros globales por `TenantId`, guardas en `SaveChanges`, validación de relaciones tenant-scoped, RLS vía `SESSION_CONTEXT` y pruebas automatizadas de aislamiento.

Se corrigieron hallazgos defensivos confirmados:
- CSRF faltante en el POST de búsqueda de clientes.
- XSS DOM en `mensajes.js`.
- Inyección HTML en correos de recuperación de contraseña.
- Hardening de cookies y headers básicos de producción.

Quedan como pendientes/requieren verificación manual:
- Endurecer autenticación/replay de webhook Tilopay si el proveedor soporta firma o timestamp.
- Definir CSP con nonces/hashes; hoy hay scripts inline.
- Reducir PII en logs operativos.
- Revisar Nginx/Production real.
- Planificar actualización de paquetes obsoletos aunque NuGet no reporta vulnerabilidades.

## Hallazgos

### 1. POST de búsqueda de clientes sin antiforgery

Severidad: Baja
Archivo/línea: `Controllers/DataBase/ClientesController.cs:171-173`
Estado: corregido

Vulnerabilidad encontrada: `Buscar` aceptaba `HttpPost` sin validación antiforgery. Aunque es una búsqueda y no modifica datos, devuelve información de clientes del tenant autenticado.

Riesgo real para LuxuryCloud: un sitio externo podría forzar al navegador autenticado a enviar búsquedas POST. Por Same-Origin Policy no leería la respuesta, pero el endpoint quedaba fuera del estándar de protección exigido para todos los POST.

Reproducción segura en local/staging:
1. Iniciar sesión como administrador.
2. Enviar un POST a `/Clientes/Buscar` sin `RequestVerificationToken`.
3. Antes del fix el endpoint procesaba la búsqueda; después del fix debe rechazar el POST sin token.

Fix recomendado: aplicar `AutoValidateAntiforgeryToken` para validar POST y mantener GET funcional.
Fix aplicado: `[AutoValidateAntiforgeryToken]` agregado a `Buscar`.

Prueba agregada: `EndpointBindingSecurityTests.MutatingControllerActions_ShouldRequireAntiforgeryOrExplicitIgnore`.

### 2. XSS DOM en módulo de mensajes simulado

Severidad: Media
Archivo/línea: `wwwroot/js/mensajes.js:25`, `wwwroot/js/mensajes.js:100-175`, `wwwroot/js/mensajes.js:325`
Estado: corregido

Vulnerabilidad encontrada: textos de chat y entrada del usuario se insertaban con `innerHTML` sin escape. El archivo se carga desde `_Layout`, por lo que el riesgo es global aunque la pantalla sea simulada.

Riesgo real para LuxuryCloud: si un usuario ingresaba HTML/script como mensaje, o si en el futuro este módulo consumía datos reales de clientes/WhatsApp, podía ejecutarse JavaScript en la sesión autenticada.

Reproducción segura en local/staging:
1. Abrir la pantalla que use el chat de mensajes.
2. Enviar como mensaje un payload HTML inofensivo, por ejemplo `<img src=x onerror=alert(1)>`.
3. Antes del fix el HTML podía interpretarse; después se renderiza como texto escapado.

Fix recomendado: usar `textContent`/DOM API o escapar todo texto antes de interpolarlo en `innerHTML`.
Fix aplicado: helper `escapeHtml`, escape de nombre/teléfono/mensajes/reply preview y normalización de clase `sent/received`.

### 3. Inyección HTML en correo de recuperación de contraseña

Severidad: Media
Archivo/línea: `Services/Account/AccountEmailService.cs:76-111`
Estado: corregido

Vulnerabilidad encontrada: `displayName` y `resetLink` se interpolaban directo en HTML del correo.

Riesgo real para LuxuryCloud: un nombre de usuario manipulado podía inyectar HTML en emails enviados por LuxuryCloud. En clientes de correo modernos el JavaScript suele estar limitado, pero sigue siendo un vector de phishing, tracking o alteración visual del correo.

Reproducción segura en local/staging:
1. Crear usuario con nombre como `<img src=x onerror=alert(1)>`.
2. Solicitar recuperación de contraseña.
3. Revisar el HTML generado del email.
4. Después del fix aparece escapado como texto.

Fix recomendado: codificar con `HtmlEncoder` todos los valores dinámicos antes de insertarlos en HTML.
Fix aplicado: `HtmlEncoder.Default.Encode` para nombre y enlace.

Prueba agregada: `AccountEmailServiceSecurityTests.BuildResetEmailHtml_ShouldEncodeUserControlledFields`.

### 4. Cookies y headers de producción no estaban endurecidos explícitamente

Severidad: Media
Archivo/línea: `Program.cs:91-96`, `Program.cs:247-254`
Estado: corregido parcialmente

Vulnerabilidad encontrada: la cookie de autenticación dependía de defaults para `SecurePolicy`, `HttpOnly`, `SameSite` y expiración. También faltaban headers básicos contra MIME sniffing, clickjacking y fuga de referrer.

Riesgo real para LuxuryCloud: en una mala configuración HTTPS/proxy, una cookie sin `Secure` explícito puede viajar por HTTP. La ausencia de headers facilita clickjacking o exposición innecesaria de URLs.

Reproducción segura en local/staging:
1. Ejecutar en ambiente Production/staging.
2. Inspeccionar `Set-Cookie` y headers de una respuesta autenticada.
3. Confirmar `HttpOnly`, `SameSite=Lax`, `Secure` en Production y headers `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`.

Fix recomendado: configurar cookies explícitamente y agregar headers conservadores.
Fix aplicado: cookie `HttpOnly`, `SameSite=Lax`, `SecurePolicy=Always` fuera de Development, expiración de 8 horas con sliding expiration, y headers básicos en Production.

Pendiente: CSP no se aplicó porque la app usa scripts inline en `_Layout.cshtml` y varias vistas. Requiere migración a nonces/hashes para no romper UI.

### 5. Webhook Tilopay autenticado por token estático en query

Severidad: Alta
Archivo/línea: `Controllers/Billing/TilopayWebhookController.cs:48-56`, `Models/Saas/OpcionesTilopay.cs:11-12`
Estado: pendiente / requiere verificación manual con Tilopay

Vulnerabilidad encontrada: el webhook valida un `WebhookAccessToken` enviado en query string. La comparación es constante (`SecureEquals`) y los logs de query redaccionan claves con `token`, pero el secreto sigue viajando como parte de la URL.

Riesgo real para LuxuryCloud: URLs con tokens pueden quedar en historial, proxies, herramientas de monitoreo o logs del proveedor. Si el token se filtra, un atacante podría intentar enviar payloads falsos. La idempotencia por `ProveedorEventId` reduce duplicados, pero no sustituye autenticidad criptográfica.

Reproducción segura en local/staging:
1. Configurar un token falso en staging.
2. POST a `/api/webhooks/tilopay?access_token=<token-staging>` con payload de prueba.
3. Confirmar que con token correcto entra al procesador y con token incorrecto responde `401`.
4. Probar replay con el mismo `EventId`; debe marcar duplicado o no duplicar cobros.

Fix recomendado: si Tilopay lo soporta, validar firma HMAC del body con timestamp y ventana corta de replay. Si no lo soporta, usar token de alta entropía rotado, allowlist de IPs en Nginx/WAF, rate limiting, HTTPS estricto y alertas por rechazos.

Controles existentes confirmados: índice único `EventoPago(Proveedor, ProveedorEventId)` en `ApplicationDbContext.cs:598`, índices únicos de transacción/referencia en `ApplicationDbContext.cs:580-592`, pruebas de billing/webhook en `BillingSecurityTests` y `RecurringCheckoutConfigurationTests`.

### 6. PII en logs operativos

Severidad: Media
Archivo/línea: `Services/Account/AccountEmailService.cs:33-70`, `Controllers/Billing/TilopayWebhookController.cs:101-159`, `Controllers/Identity/AccountsController.cs:193-198`
Estado: pendiente

Vulnerabilidad encontrada: se registran correos, montos o identificadores de pago en algunos logs. Los logs detallados de Tilopay están limitados a Development, pero `AccountEmailService` registra email de destinatario en cualquier ambiente.

Riesgo real para LuxuryCloud: exposición de datos personales o financieros en logs, backups de logs o servicios de observabilidad.

Reproducción segura en local/staging:
1. Solicitar recuperación de contraseña.
2. Revisar logs de aplicación local/staging.
3. Confirmar si aparece email completo.

Fix recomendado: enmascarar emails (`j***@dominio.com`), mantener IDs internos/correlation IDs, evitar payloads completos y documentar retención de logs.

### 7. Arquitectura de filtro global permite leer todo cuando no hay tenant resuelto

Severidad: Media
Archivo/línea: `Datos/ApplicationDbContext.cs:37-60`, `Datos/ApplicationDbContext.cs:642-655`
Estado: requiere verificación manual

Vulnerabilidad encontrada: el filtro global permite todas las filas cuando `CurrentTenantId == Guid.Empty`. Esto habilita contextos de sistema, webhooks y vistas públicas, pero aumenta el impacto de cualquier endpoint anónimo o background job que consulte entidades tenant-scoped sin filtro manual.

Riesgo real para LuxuryCloud: no se confirmó endpoint explotable durante esta revisión. El riesgo es de defensa en profundidad: un futuro endpoint público que use `_context.Clientes`, `_context.Cobros`, etc. sin tenant podría exponer datos de todos los tenants.

Reproducción segura en local/staging:
1. Crear un test con `TestTenantProvider.TenantId = Guid.Empty`.
2. Consultar una entidad `ITenantEntity` con datos de dos tenants.
3. Confirmar que devuelve ambos.

Fix recomendado: reemplazar el bypass implícito por un scope explícito de sistema, por ejemplo `TenantExecutionService`, y hacer que la ausencia de tenant falle por defecto en lecturas tenant-scoped. Mantener excepciones explícitas solo para Platform, webhooks y background jobs revisados.

Controles existentes: whitelist de `IgnoreQueryFilters` en `EndpointBindingSecurityTests`, guardas de escritura en `ApplicationDbContextTenantIsolationTests`, y `TenantExecutionService` para tareas multi-tenant.

### 8. CSP pendiente por scripts inline

Severidad: Baja
Archivo/línea: `Views/Shared/_Layout.cshtml:13-75`, varias vistas con scripts inline
Estado: pendiente

Vulnerabilidad encontrada: no hay `Content-Security-Policy`. Se agregaron headers básicos, pero CSP requiere trabajo de compatibilidad porque hay scripts inline y CDN externos.

Riesgo real para LuxuryCloud: sin CSP, un XSS que atraviese encoding tiene mayor capacidad de ejecutar JavaScript arbitrario.

Reproducción segura en local/staging:
1. Inspeccionar headers de cualquier respuesta HTML.
2. Confirmar ausencia de `Content-Security-Policy`.

Fix recomendado: migrar scripts inline a archivos versionados o aplicar nonces/hashes; después usar una CSP inicial tipo `script-src 'self' 'nonce-...' https://cdn.jsdelivr.net https://code.jquery.com`.

### 9. Paquetes obsoletos sin vulnerabilidad reportada

Severidad: Baja
Archivo/línea: `LuxuryApp.csproj`, `LuxuryApp.Tests/LuxuryApp.Tests.csproj`
Estado: pendiente

Vulnerabilidad encontrada: NuGet no reporta paquetes vulnerables, pero hay actualizaciones disponibles.

Riesgo real para LuxuryCloud: paquetes antiguos pueden perder fixes no catalogados como vulnerabilidad o mejoras de seguridad.

Reproducción segura en local/staging:
1. Ejecutar `dotnet list package --vulnerable --include-transitive`.
2. Ejecutar `dotnet list package --outdated`.

Fix recomendado: planificar actualización controlada de EF/Identity 10.0.2 -> 10.0.9, Polly 8.6.6 -> 8.7.0, Resend 0.2.1 -> 0.5.1, Stripe.net 51.0.0 -> 52.0.0 y paquetes de test, con regresión de auth, pagos y migraciones.

## Controles verificados

- Autorización global: `Program.cs:135-139` exige usuario autenticado por defecto.
- Controladores sensibles: pruebas verifican rol `Administrador` en Calendar, Clientes, Finanzas, Funcionarios, Productos, Billing y Roles.
- Platform admin: `PlatformController` exige política `PlatformSuperAdmin`; probado en `ControllerAuthorizationTests`.
- Tenant isolation: filtros por `ITenantEntity` y `HistorialSuscripcion`; probado en `ApplicationDbContextTenantIsolationTests`.
- Escritura cross-tenant: `ApplyTenantGuards` bloquea modificaciones/eliminaciones de otro tenant y relaciones cruzadas; probado.
- `TenantId` overposting: propiedades `TenantId` con `BindNever`; probado en `EndpointBindingSecurityTests`.
- `IgnoreQueryFilters`: usos restringidos por test a Billing, Platform, servicios SaaS/payment autorizados.
- Billing return: `BillingSecurityTests.BillingSuccess_ShouldNotExposePaymentFromAnotherTenant` cubre IDOR por referencia de pago.
- WhatsApp inbox/chat: `WhatsAppInboxServiceTests.GetCitaChatAsync_ShouldReturnNullForAnotherTenantCita` cubre acceso por `citaId` de otro tenant.
- Stripe webhook: valida firma con `EventUtility.ConstructEvent`.
- Meta WhatsApp webhook: valida `X-Hub-Signature-256` con HMAC SHA-256.
- Idempotencia pagos: índices únicos por evento, transacción y referencia en `ApplicationDbContext.cs:580-598`.
- Secretos en `appsettings*.json`: revisión enmascarada no encontró claves reales evidentes; credenciales críticas aparecen vacías o como configuración no secreta.

## Pruebas y comandos ejecutados

- `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables en `LuxuryApp`.
- `dotnet list LuxuryApp.Tests/LuxuryApp.Tests.csproj package --vulnerable --include-transitive`: sin paquetes vulnerables en tests.
- `dotnet list package --outdated`: actualizaciones disponibles en EF/Identity, Polly, Resend y Stripe.net.
- `dotnet list LuxuryApp.Tests/LuxuryApp.Tests.csproj package --outdated`: actualizaciones disponibles en EF Sqlite, Microsoft.NET.Test.Sdk y xUnit runner.
- `dotnet test LuxuryApp.Tests/LuxuryApp.Tests.csproj -p:OutDir=artifacts/test-out/ -p:UseAppHost=false`: 368/368 tests correctos.

Notas de verificación:
- El primer `dotnet test` normal falló porque `LuxuryApp.exe`/`LuxuryApp.dll` estaban bloqueados por el proceso local `LuxuryApp (28376)` y Visual Studio. Se usó `OutDir` aislado para no detener el servidor local.
- Warnings no corregidos en esta rama: nullability en `Emails/EmailSender.cs` y `ForwardedHeadersOptions.KnownNetworks` obsoleto en `Program.cs`.

## Recomendaciones próximas

1. Validar Nginx/Production real: TLS 1.2+, HSTS, redirect 80->443, `proxy_set_header X-Forwarded-Proto`, límites de tamaño para webhooks, rate limiting y logs sin query strings sensibles.
2. Endurecer Tilopay: firma HMAC/timestamp si existe soporte; si no, allowlist/rate limiting/rotación de token.
3. Diseñar CSP incremental con nonces/hashes.
4. Enmascarar PII en logs de emails, pagos y webhooks.
5. Abrir una rama separada para upgrades de paquetes y regresión de pagos/auth/migraciones.
