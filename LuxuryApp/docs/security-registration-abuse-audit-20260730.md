# Auditoria de seguridad - abuso de registro LuxuryCloud

Fecha: 2026-07-30  
Alcance: revision pasiva de codigo local y hardening local. Sin pruebas destructivas, sin llamadas a TiloPay reales, sin WhatsApps reales.

## Resumen

El riesgo confirmado principal estaba en el registro publico: el POST creaba inmediatamente un `Tenant` activo y un `AppUsuario` activo, iniciaba sesion y solo despues enviaba al usuario a seleccion/checkout cuando no tenia acceso comercial. Eso permitia que bots dejaran tenants persistentes en estado "sin acceso comercial" aunque nunca confirmaran identidad ni pagaran.

Controles ya existentes y verificados en codigo:

- Autorizacion global por defecto en MVC.
- Platform/Mission Control protegido por `PlatformSuperAdmin`.
- CSRF en POSTs MVC sensibles.
- Filtros globales y guardas por `TenantId`.
- `/reservar/{slug}` y `/sitio/{slug}` con rate limiting por IP.
- Meta webhook con HMAC `X-Hub-Signature-256`.
- Stripe webhook con firma.
- TiloPay webhook con token estatico y procesamiento idempotente, aunque sigue pendiente firma HMAC si el proveedor la soporta.

## Hallazgos priorizados

### P0

- Registro publico abusado por bots: tenant/admin se creaban antes de verificar email o pago.
- Rate limiting faltante en registro, login y forgot password.
- Webhook TiloPay protegido por token en query, no por firma del body.
- Riesgo permanente a revisar: cualquier `IgnoreQueryFilters` o consulta sin tenant en rutas anonimas.

### P1

- Email confirmation no era requisito para activar el flujo comercial.
- No habia honeypot ni Captcha/Turnstile opcional.
- Validacion de email/nombre de negocio era demasiado basica para abuso automatizado.
- Mission Control no separaba registros pendientes/sospechosos de clientes reales.

### P2

- Reportes/alertas de patrones sospechosos pueden crecer con IP hash por registro y dashboard dedicado.
- CSP global completa sigue pendiente por scripts inline.
- Limpieza de tenants basura debe ejecutarse solo con revision manual y rollback/soft-disable.

## Cambios implementados

- Nuevos registros quedan con `TenantCommercialAccessMode.PendingVerification`.
- El usuario admin nuevo queda `EmailConfirmed = false`.
- No hay auto-login al completar registro cuando se requiere confirmacion.
- Se envia email de confirmacion con token de Identity.
- Al confirmar email, el tenant pasa a `RequiresSubscription`, se invalida cache comercial y el usuario puede continuar a planes/checkout.
- Rate limiting por IP en middleware para `Registration`, `Authentication`, `PasswordReset` y `Webhook`.
- Rate limiting adicional por IP/email en `AccountsController`.
- Honeypot invisible `CompanyWebsite` en el formulario de registro.
- Validacion server-side mas estricta para email y nombre de negocio.
- Turnstile opcional por `RegistrationSecurity:Turnstile`.
- Mission Control agrega cola "Registros pendientes / sospechosos".
- Worker `PendingTenantExpirationWorker` para soft-expirar registros `PendingVerification` antiguos sin pago, verificacion ni actividad. Queda apagado por defecto con `RegistrationSecurity:ExpirePendingTenantsEnabled=false`.
- Scripts SQL read-only y plan de limpieza con `ROLLBACK` por defecto.

## Scripts

- `Scripts/SecuritySuspiciousTenantAudit.sql`: solo lectura; identifica tenants/usuarios recientes, emails raros, falta de pago, usuarios sin confirmar, multiples tenants por email, IP/UserAgent de aceptacion de contrato y auditoria plataforma.
- `Scripts/SafeTenantCleanupPlan.sql`: preview transaccional; propone soft-disable de tenants pending antiguos sin pago ni actividad, termina con `ROLLBACK`.

## Riesgos residuales

- TiloPay: mantener token de alta entropia y rotacion. Si TiloPay soporta firma/timestamp, migrar a HMAC del body y ventana anti-replay.
- Captcha/Turnstile esta apagado por defecto; requiere claves en configuracion para activarse.
- Expiracion automatica de pendientes esta implementada pero apagada por defecto. Activarla solo despues de validar candidatos con el SQL read-only y definir ventana operativa.
- `AspNetUsers` no tiene `CreatedAt`; para investigacion de usuarios recientes se infiere por `Tenants.FechaCreacion`.
- El rate limit en memoria no es distribuido entre multiples instancias. Para scale-out, mover contadores a Redis/WAF/API gateway.
