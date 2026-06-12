# LuxuryCloud Security Deploy Checklist

Checklist defensivo para validar produccion despues del deploy. No pegar salidas con datos reales en tickets o chats; usar conteos, headers y estados.

## HTTPS, proxy y headers

- Confirmar que el sitio publico responde por HTTPS:
  - `curl -I https://TU_DOMINIO/`
  - Esperado: `200`, `302` controlado o la respuesta normal de la app.
- Confirmar redirect HTTP a HTTPS:
  - `curl -I http://TU_DOMINIO/`
  - Esperado: `301` o `308` hacia `https://TU_DOMINIO/...`.
- Confirmar HSTS en HTTPS:
  - `curl -I https://TU_DOMINIO/ | grep -i strict-transport-security`
  - Esperado: header `Strict-Transport-Security` presente en Production.
- Confirmar headers basicos:
  - `curl -I https://TU_DOMINIO/ | grep -Ei 'x-content-type-options|x-frame-options|referrer-policy|permissions-policy'`
  - Esperado: headers presentes segun la configuracion actual.
- Confirmar `X-Forwarded-Proto` desde Nginx hacia ASP.NET Core:
  - En Nginx debe existir `proxy_set_header X-Forwarded-Proto $scheme;`.
  - Probar una ruta autenticada y verificar que no haya loops de redirect HTTP/HTTPS.
- Confirmar `ASPNETCORE_ENVIRONMENT=Production` y `AllowedHosts` restringido al dominio real.

## Logs sin PII ni secretos

- Revisar logs recientes sin imprimir coincidencias sensibles completas:
  - `sudo journalctl -u luxurycloud -S "15 minutes ago" --no-pager | grep -Eci 'token=|access_token|verify_token|password|authorization|RawBody|ResponseBody|Payload \\{'`
  - Esperado: `0`.
- Revisar posibles emails completos en logs recientes:
  - `sudo journalctl -u luxurycloud -S "15 minutes ago" --no-pager | grep -Eci '[[:alnum:]._%+-]+@[[:alnum:].-]+\\.[[:alpha:]]{2,}'`
  - Esperado: `0` o solo dominios/valores enmascarados previamente aprobados.
- Revisar posibles telefonos largos en logs recientes:
  - `sudo journalctl -u luxurycloud -S "15 minutes ago" --no-pager | grep -Eci '\\+?[0-9][0-9 .()/-]{7,}[0-9]'`
  - Esperado: `0` o solo sufijos enmascarados.
- Confirmar que access logs de Nginx no guarden query strings sensibles para `/api/webhooks/tilopay`.
  - Preferido: usar `$uri` en lugar de `$request_uri` en el formato de access log, o un formato dedicado/redactado para webhooks.
- Despues de probar recuperacion de password, confirmar que no aparece `token=` ni el email completo en `journalctl`.
- Despues de probar checkout/webhook, confirmar que solo aparecen `TraceIdentifier`, `TenantId`, `PaymentAttemptId`, estados y sufijos de referencias.

## Webhook Tilopay

- Confirmar que `/api/webhooks/tilopay` solo acepta POST:
  - `curl -I https://TU_DOMINIO/api/webhooks/tilopay`
  - Esperado: no debe procesar cobros con GET/HEAD.
- Confirmar rate limiting en Nginx para `/api/webhooks/tilopay`.
  - Ejemplo esperado:
    - `limit_req_zone $binary_remote_addr zone=tilopay_webhook:10m rate=30r/m;`
    - `location = /api/webhooks/tilopay { limit_req zone=tilopay_webhook burst=20 nodelay; ... }`
- Confirmar que intentos con token invalido responden `401` y loguean solo `TraceIdentifier` y `Path`.
- Confirmar que un replay del mismo webhook no duplica cobros ni suscripciones; validar por `paymentAttemptId`/evento y estado, no por payload completo.

## Revision post-deploy

- Ejecutar smoke test de login, recuperacion de password, seleccion de plan y retorno de checkout en staging/produccion controlada.
- Revisar errores recientes:
  - `sudo journalctl -u luxurycloud -p warning -S "30 minutes ago" --no-pager`
- Revisar conteos sensibles otra vez despues del smoke test:
  - Emails completos: debe ser `0`.
  - Telefonos completos: debe ser `0`.
  - Tokens/query strings sensibles: debe ser `0`.
- Si aparece una coincidencia sensible, no copiar la linea completa. Registrar hora, flujo operativo, `TraceIdentifier` y corregir el logger antes de continuar el rollout.
