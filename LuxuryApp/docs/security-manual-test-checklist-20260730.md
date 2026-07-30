# Checklist manual de pruebas controladas

Ejecutar solo en local/staging con usuarios y tenants falsos. No usar produccion salvo checks pasivos/read-only.

## Registro y autenticacion

- Registrar un tenant valido y confirmar que queda en `PendingVerification`, `EmailConfirmed = 0`, sin sesion activa.
- Abrir el link de confirmacion y confirmar que pasa a `RequiresSubscription`.
- Enviar el formulario de registro con `CompanyWebsite` lleno y confirmar que no se crea tenant.
- Intentar registrar `u.t.i.d.o.s.ahe.re.68.1@gmail.com` y confirmar rechazo server-side.
- Intentar registrar nombre de negocio con `<script>alert(1)</script>` y confirmar rechazo.
- Enviar mas de 5 registros desde la misma IP en 10 minutos y confirmar HTTP 429.
- Enviar mas de 3 registros con el mismo email en 10 minutos y confirmar bloqueo por email.
- Probar login repetido desde la misma IP/email y confirmar lockout/rate limit sin enumerar cuentas.
- Probar forgot password para email existente y no existente: ambos deben mostrar mensaje generico.
- En staging, activar temporalmente `RegistrationSecurity:ExpirePendingTenantsEnabled=true` con tenants falsos antiguos y confirmar soft-disable solo de `PendingVerification` sin pago/verificacion/actividad.

## Multi-tenant / IDOR

- Crear tenant A y tenant B en staging.
- Con usuario de tenant B, intentar IDs de clientes/citas/cobros/servicios de tenant A en URLs y AJAX.
- Confirmar 404/403/null y que no se modifican datos.
- Probar POST con `TenantId` manipulado en body; debe ignorarse o bloquearse.

## CSRF y XSS

- Enviar POST MVC sin token antiforgery a modulos privados; debe fallar.
- Probar payloads XSS simples en nombre de negocio, cliente, notas internas, descripcion de servicio y notas de reserva.
- Verificar que se renderizan codificados y no ejecutan JavaScript.

## Rutas publicas

- Probar `/reservar/{slug}` con slug inexistente: 404/noindex.
- Enviar reservas publicas repetidas desde una IP de staging; debe activar rate limit.
- Intentar reserva duplicada con el mismo submission token; no debe crear duplicados.
- Probar `/sitio/{slug}` y redirects `/go/*`; no deben abrir redirects externos arbitrarios.

## Archivos / imagenes

- Subir archivo no imagen con extension de imagen.
- Subir imagen sobredimensionada.
- Confirmar rechazo, cuotas por tenant y storage key bajo carpeta del tenant.

## Webhooks

- TiloPay staging/local: POST sin token debe responder 401.
- TiloPay staging/local: repetir el mismo evento; debe ser idempotente.
- Meta local: POST sin `X-Hub-Signature-256` valido debe responder 401.
- Stripe local: POST sin firma valida debe responder 400.
- No usar endpoints ni credenciales reales de proveedor durante estas pruebas.

## Platform / Mission Control

- Entrar a `/Platform` sin platform superadmin: debe denegar.
- Confirmar que Mission Control muestra "Registros pendientes / sospechosos".
- Intentar establecer manualmente `PendingVerification` desde tenants: debe rechazarse.
- Confirmar que cambios comerciales dejan entrada en `PlatformAuditLogs`.
