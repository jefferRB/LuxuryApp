# Plan de prueba E2E para Tilopay Repeat

## Objetivo

Validar el flujo recurrente de LuxuryCloud en sandbox de Tilopay sin usar tarjetas reales y sin publicar cambios a produccion.

## Estado real del sandbox hosted repeat

- En junio 2026 se confirmo que los campos correctos de retorno y webhook si existen dentro de `Datos opcionales` del plan recurrente en Tilopay Repeat.
- El plan `TEST_RECURRING` con `TilopayPlanId = 5834` ya se pudo activar de punta a punta por webhook real.
- El webhook `repeat_payment_success` activa la suscripcion, actualiza `PagosSuscripcion`, deja `EventosPago` trazable y habilita el acceso comercial.
- La ruta `/Billing/CheckoutReturn` debe usarse como retorno amigable. No es la fuente de verdad para activar, pero si debe mostrar el estado final sin error aunque el webhook ya haya procesado el pago.
- Si el correo escrito en Tilopay no coincide con el correo del pending local, LuxuryCloud no debe activar automaticamente: el evento queda en `PendingManualReview` con auditoria.

## Configuracion del plan TEST 5834 en Tilopay

Configurar estos campos en `Datos opcionales` del plan recurrente:

- `URL de agradecimiento`: `https://<public-base-url>/Billing/CheckoutReturn`
- `URL para cancelar`: `https://<public-base-url>/Billing/CheckoutCancel`
- `URL Webhook registro exitoso`: `https://<public-base-url>/api/webhooks/tilopay?access_token=<token>&event=repeat_registration`
- `URL Webhook pago realizado`: `https://<public-base-url>/api/webhooks/tilopay?access_token=<token>&event=repeat_payment_success`
- `URL Webhook pago rechazado`: `https://<public-base-url>/api/webhooks/tilopay?access_token=<token>&event=repeat_payment_failed`
- `URL Webhook cancelacion`: `https://<public-base-url>/api/webhooks/tilopay?access_token=<token>&event=repeat_subscription_cancelled`
- `URL Webhook reactivacion`: `https://<public-base-url>/api/webhooks/tilopay?access_token=<token>&event=repeat_subscription_reactivated`

Notas:

- No guardar `access_token` en codigo, capturas ni logs.
- `Payments:PublicBaseUrl` debe coincidir con el tunel activo del entorno local.
- `GET {PublicBaseUrl}/api/health/public-callback` debe responder `200 OK`.

## Estrategia operativa

- La activacion automatica depende del webhook real de Tilopay Repeat.
- `CheckoutReturn` solo muestra el estado final del tenant autenticado y nunca debe activar por si solo sin evidencia adicional.
- La conciliacion manual sigue disponible para `Development` o `PlatformSuperAdmin` cuando:
  1. el webhook no llega,
  2. el correo de Tilopay no coincide,
  3. hay multiples pendientes compatibles,
  4. el monto o la moneda no coinciden.
- La aprobacion manual reutiliza el mismo metodo central de activacion para no duplicar reglas.

## Preparar ambiente

1. Ejecutar la app en `Development`.
2. Confirmar que `TilopayRepeat:Enabled=true`.
3. Confirmar que `TilopayRepeat:UseHostedLinks=true`.
4. Confirmar que `TilopayRepeat:EnableTestRecurringPlan=true`.
5. Confirmar que `TilopayRepeat:UseRecurringCheckoutForPublicPlans=true` solo en `Development/Test`.
6. Confirmar que existe `Tilopay:WebhookAccessToken` por `user-secrets` o variable segura.
7. Confirmar que `Payments:PublicBaseUrl` apunta a una URL publica viva del entorno actual.
8. Confirmar que `GET {PublicBaseUrl}/api/health/public-callback` responde `200`.
9. Confirmar que el panel de Tilopay muestra `modo pruebas activado`.
10. Confirmar que todos los planes de Tilopay tienen `Costo de activacion = 0.00`.
11. Confirmar que `Prueba gratuita` y configuraciones de doble cobro estan desactivadas en Tilopay.

## Variables recomendadas

Para pruebas locales o por tunel, configurar estas variables seguras:

- `Payments__PublicBaseUrl=<url-publica-viva>`
- `Tilopay__WebhookAccessToken=<secret>`
- `TilopayRepeat__Enabled=true`
- `TilopayRepeat__UseHostedLinks=true`
- `TilopayRepeat__EnableTestRecurringPlan=true`
- `TilopayRepeat__UseRecurringCheckoutForPublicPlans=true`

## Advertencia de correo

- Antes de redirigir a Tilopay, LuxuryCloud debe mostrar el correo actual de la cuenta y advertir: `usa en Tilopay el mismo correo con el que creaste tu cuenta en LuxuryCloud`.
- Si Tilopay envia un correo distinto al pending abierto, el webhook queda en `PendingManualReview` con razon explicita.

## Hosted links requeridos

Antes de probar clientes reales en sandbox, confirmar estas keys:

- `TilopayRepeat:TestRecurring:CheckoutUrl`
- `TilopayRepeat:Basic:CheckoutUrl`
- `TilopayRepeat:Pro:CheckoutUrl`
- `TilopayRepeat:Business:CheckoutUrl`
- `TilopayRepeat:WhatsApp400:CheckoutUrl`
- `TilopayRepeat:WhatsApp800:CheckoutUrl`
- `TilopayRepeat:WhatsApp1200:CheckoutUrl`

Si una key falta, LuxuryCloud debe mostrar el error exacto del plan afectado sin bloquear los demas.

## Confirmaciones de monto por plan

En todos los casos, Tilopay debe mostrar `Costo de activacion = CRC 0.00` y el total de hoy debe coincidir con el monto mensual:

- `TEST_RECURRING`: `CRC 1000.00`
- `BASIC`: `CRC 8000.00`
- `PRO`: `CRC 20000.00`
- `BUSINESS`: `CRC 35000.00`
- `WA400`: `CRC 6000.00`
- `WA800`: `CRC 12000.00`
- `WA1200`: `CRC 18000.00`

Si Tilopay muestra un cobro inicial mayor, corregir el plan en Tilopay antes de seguir.

## Checklist de conciliacion manual sandbox

Usar cuando Tilopay muestre pago aprobado pero LuxuryCloud siga en `Pendiente`:

1. Abrir `/Platform/RecurringCheckouts`.
2. Buscar el pending correcto por tenant, email, plan y `CorrelationToken`.
3. Confirmar que el monto esperado coincide con el plan:
   - `TEST_RECURRING`: `CRC 1000.00`
   - `BASIC`: `CRC 8000.00`
   - `PRO`: `CRC 20000.00`
   - `BUSINESS`: `CRC 35000.00`
4. Copiar desde Tilopay el `transactionId` o numero de orden real aprobado.
5. Completar monto, moneda y observacion en la conciliacion interna.
6. Aprobar manualmente.
7. Confirmar en LuxuryCloud:
   - `PagoSuscripcion.Estado = Confirmado`
   - `Suscripcion.Estado = Activa`
   - `ProviderTransactionId` guardado
   - `FechaFin` y `FechaProximoCobroUtc` mensuales
   - `MaxFuncionarios` segun plan
8. Verificar que desaparece la pantalla de renovacion requerida y el tenant recupera acceso.

## Prueba TEST_RECURRING

1. Abrir `/Billing/Planes`.
2. Confirmar que aparece la seccion `Validacion interna`.
3. Elegir `Prueba Tilopay`.
4. Completar registro con email unico: `recurrent-test-testplan-<timestamp>@example.test`.
5. Marcar contrato.
6. Confirmar que no aparece el error de contrato.
7. Confirmar redireccion a `/Billing/ContinuarCheckout`.
8. Confirmar que la pantalla previa al checkout muestra la advertencia de usar el mismo correo en Tilopay y LuxuryCloud.
9. Confirmar redireccion final a `https://securepayment.tilopay.com/...`.
10. Confirmar en Tilopay `Costo de activacion = CRC 0.00` y `Total a pagar hoy = CRC 1000.00`.
11. Usar en Tilopay el mismo correo del registro de LuxuryCloud.
12. Pagar solo con tarjeta oficial de prueba del panel Tilopay.
13. Confirmar llegada de `repeat_registration` y `repeat_payment_success` en `/api/webhooks/tilopay`.
14. Confirmar que `/Billing/CheckoutReturn` no muestra `Home/Error`.
15. Revisar `PagosSuscripcion`, `EventosPago` y `Suscripciones`.
16. Confirmar:
    - `PlanCode = TEST_RECURRING`
    - `TilopayRecurringPlanId = 5834`
    - `Estado = Active`
    - `FechaFin` mensual
    - `FechaProximoCobro` mensual
    - `MaxFuncionarios = 1`
17. Iniciar sesion y confirmar acceso a `/Dashboard`.
18. Confirmar que `Mi Suscripcion` muestra `Activo`, vigencia mensual y limite de funcionarios `1`.
19. Intentar crear un segundo funcionario y confirmar que el sistema lo bloquea.

## Diagnostico rapido

Si el tenant queda en `Pendiente` o aparece un comportamiento inesperado:

1. Revisar `EventosPago` y confirmar si existe `repeat_payment_success`.
2. Revisar `PagosSuscripcion` y validar `TilopayRecurringPlanId`, `ClienteEmail`, `Monto`, `Moneda` y `Estado`.
3. Confirmar que el correo usado en Tilopay coincide exactamente con el correo del pending local.
4. Si el evento queda en `PendingManualReview`, abrir `/Platform/RecurringCheckouts` y revisar la razon.
5. Si `CheckoutReturn` falla, buscar el `TraceIdentifier` en logs y confirmar que la accion devuelva la vista `Exito`.
6. Si el webhook no llega, validar ngrok, `Payments:PublicBaseUrl` y los campos del plan recurrente en Tilopay.

## Prueba BASIC

1. Configurar `TilopayRepeat:Basic:CheckoutUrl`.
2. Crear cuenta nueva: `recurrent-test-basic-<timestamp>@example.test`.
3. Seleccionar `Basico`.
4. Confirmar checkout sandbox con `Costo de activacion = CRC 0.00` y `Total a pagar hoy = CRC 8000.00`.
5. Pagar con tarjeta oficial de prueba.
6. Confirmar suscripcion `Active`.
7. Confirmar `MaxFuncionarios = 1`.
8. Confirmar segundo funcionario bloqueado.

## Prueba PRO

1. Configurar `TilopayRepeat:Pro:CheckoutUrl`.
2. Crear cuenta nueva: `recurrent-test-pro-<timestamp>@example.test`.
3. Seleccionar `Pro`.
4. Confirmar checkout sandbox con `Costo de activacion = CRC 0.00` y `Total a pagar hoy = CRC 20000.00`.
5. Pagar con tarjeta oficial de prueba.
6. Confirmar suscripcion `Active`.
7. Confirmar `MaxFuncionarios = 3`.
8. Confirmar cuarto funcionario bloqueado.

## Prueba BUSINESS

1. Configurar `TilopayRepeat:Business:CheckoutUrl`.
2. Crear cuenta nueva: `recurrent-test-business-<timestamp>@example.test`.
3. Seleccionar `Business`.
4. Confirmar checkout sandbox con `Costo de activacion = CRC 0.00` y `Total a pagar hoy = CRC 35000.00`.
5. Pagar con tarjeta oficial de prueba.
6. Confirmar suscripcion `Active`.
7. Confirmar `MaxFuncionarios = 7`.
8. Confirmar octavo funcionario bloqueado.

## Cambio BASIC -> PRO

1. Iniciar sesion con tenant sandbox de `BASIC`.
2. Entrar a `/Billing/Planes`.
3. Elegir `Pro`.
4. Confirmar que se crea intento pendiente antes del pago.
5. Confirmar que el plan actual no cambia antes del webhook.
6. Pagar en checkout sandbox de `PRO`.
7. Confirmar webhook procesado.
8. Confirmar `PlanCode = PRO`.
9. Confirmar `MaxFuncionarios = 3`.
10. Confirmar cuarto funcionario bloqueado.

## Cambio PRO -> BUSINESS

1. Iniciar sesion con tenant sandbox de `PRO`.
2. Elegir `Business`.
3. Confirmar intento pendiente sin aplicar cambio inmediato.
4. Pagar checkout sandbox de `BUSINESS`.
5. Confirmar webhook procesado.
6. Confirmar `PlanCode = BUSINESS`.
7. Confirmar `MaxFuncionarios = 7`.
8. Confirmar octavo funcionario bloqueado.

## Pruebas WhatsApp add-on

### WA400

1. Configurar `TilopayRepeat:WhatsApp400:CheckoutUrl`.
2. Iniciar sesion con tenant base activo.
3. Elegir `WhatsApp 400`.
4. Confirmar checkout sandbox por `CRC 6000.00`.
5. Pagar con tarjeta de prueba.
6. Confirmar `TenantSubscriptionAddon` activo.
7. Confirmar `MonthlyMessageLimit = 400`.

### WA800

1. Configurar `TilopayRepeat:WhatsApp800:CheckoutUrl`.
2. Confirmar checkout sandbox por `CRC 12000.00`.
3. Confirmar `MonthlyMessageLimit = 800`.

### WA1200

1. Configurar `TilopayRepeat:WhatsApp1200:CheckoutUrl`.
2. Confirmar checkout sandbox por `CRC 18000.00`.
3. Confirmar `MonthlyMessageLimit = 1200`.

## Verificaciones de logs y tablas

Revisar:

- `PagosSuscripcion`
- `EventosPago`
- `Suscripciones`
- `TenantSubscriptionAddons`
- `/Platform/RecurringCheckouts`
- `artifacts/tilopay-dev-run.out.log`
- `artifacts/tilopay-dev-run.err.log`

Confirmar que los logs incluyen:

- `PlanCode`
- `TilopayPlanId`
- `ExpectedFirstChargeAmount`
- `CorrelationToken`
- URL de hosted link con `lc_email` redactado

Confirmar que no se guarda:

- PAN completo
- CVV
- tokens sensibles
- secretos

## Lo que hay que pedirle a Tilopay para produccion real

Para automatizacion 100% real y sin conciliacion manual, solicitar explicitamente:

1. API de creacion dinamica de suscripcion recurrente.
2. Webhook de pago aprobado por cada cobro recurrente.
3. `return/success/cancel URL` configurables por transaccion o por plan.
4. Metadata o correlation id propio (`lc_ref`, `orderId`, hash o equivalente) que vuelva en webhook y return.

Hasta que Tilopay entregue eso, la activacion automatica no puede garantizarse solo con hosted links estaticos.

## Antes de produccion

1. Confirmar SSL y dominio definitivo.
2. Confirmar `Payments__PublicBaseUrl=https://app.luxurycloud.app`.
3. Confirmar `https://app.luxurycloud.app/api/health/public-callback`.
4. Confirmar webhook accesible desde Tilopay.
5. Confirmar `ForwardedHeaders` detras de Nginx.
6. Confirmar cookies seguras.
7. Confirmar que `TEST_RECURRING` no aparece en produccion.
8. Confirmar que `TilopayRepeat__EnableTestRecurringPlan=false`.
9. Confirmar que todos los planes reales cobran el monto correcto sin activacion.
10. Tomar snapshot y respaldo antes de publicar.
