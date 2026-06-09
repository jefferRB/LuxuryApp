# Tilopay Repeat para LuxuryCloud

## Regla comercial obligatoria

LuxuryCloud no calcula el monto final del cobro cuando usa hosted links de Tilopay Repeat.
El total que ve el cliente en el checkout hosted lo define la configuracion del plan en el dashboard de Tilopay.

Si Tilopay muestra `Costo de activacion` mayor a `0.00`, el plan esta mal configurado para LuxuryCloud y debe corregirse antes de probar con clientes reales.

Configuracion correcta por plan:

| PlanCode | TilopayPlanId | Monto por pago inicial | Monto recurrente mensual | Moneda | Frecuencia |
| --- | ---: | ---: | ---: | --- | --- |
| `TEST_RECURRING` | `5834` | `0.00` | `1000.00` | `CRC` | `Mensual` |
| `BASIC` | `5828` | `0.00` | `8000.00` | `CRC` | `Mensual` |
| `PRO` | `5829` | `0.00` | `20000.00` | `CRC` | `Mensual` |
| `BUSINESS` | `5830` | `0.00` | `35000.00` | `CRC` | `Mensual` |
| `WA400` | `5831` | `0.00` | `6000.00` | `CRC` | `Mensual` |
| `WA800` | `5832` | `0.00` | `12000.00` | `CRC` | `Mensual` |
| `WA1200` | `5833` | `0.00` | `18000.00` | `CRC` | `Mensual` |

## Configuracion esperada en el dashboard Tilopay

- `Monto por pago inicial` = `0.00`
- `Monto de cobro recurrente` = precio mensual real del plan
- `Prueba gratuita` desactivada
- `Modalidades con diferentes costos` desactivada
- `Estado` activo
- `Moneda` = `CRC`
- `Frecuencia` = mensual

## Lo que valida LuxuryCloud

- Cada plan recurrente define internamente `ExpectedFirstChargeAmount = MonthlyPrice`.
- El webhook recurrente compara el `Amount` recibido contra ese valor esperado.
- Si el monto o la moneda no coinciden, el evento queda en `PendingManualReview` y no activa la suscripcion automaticamente.
- Los logs del checkout recurrente incluyen `PlanCode`, `TilopayPlanId`, `ExpectedFirstChargeAmount`, `CorrelationToken` y la URL del hosted link con `lc_email` redactado.

## Limitacion importante del hosted link

LuxuryCloud agrega `lc_ref`, `lc_plan` y `lc_email` al hosted link para ayudar a correlacionar el pago, pero el comportamiento final depende de que Tilopay preserve esos parametros o los refleje en webhook/retorno.

En pruebas del hosted link corto `tp.cr`, la redireccion del navegador termina en `https://securepayment.tilopay.com/?code=...` y esa URL final ya no expone los parametros `lc_*`.
Por eso no se debe asumir que la correlacion por query string estara disponible en el retorno del navegador.

Si el webhook no devuelve `lc_ref` ni otra referencia suficiente:

- LuxuryCloud intenta correlacionar por `subscriberId`
- luego por `email + recurringPlanId + intento pendiente reciente`
- si encuentra multiples coincidencias o no hay evidencia suficiente, deja el evento en `PendingManualReview`

No se debe inventar una relacion tenant-pago sin evidencia verificable.

## Configuracion recomendada para Development

- `TilopayRepeat:Enabled=true`
- `TilopayRepeat:UseHostedLinks=true`
- `TilopayRepeat:EnableTestRecurringPlan=true`
- `TilopayRepeat:UseRecurringCheckoutForPublicPlans=false` mientras no se valide el TEST 5834
- `Payments:PublicBaseUrl=https://<tu-url-publica-dev>`
- `Tilopay:WebhookAccessToken=<token-seguro>`
- `TilopayRepeat:TestRecurring:CheckoutUrl=https://tp.cr/l/TlRnek5BPT18MQ==`

## Configuracion recomendada para Production en DigitalOcean

- `Payments__PublicBaseUrl=https://app.luxurycloud.app`
- `Tilopay__WebhookAccessToken=<token-seguro>`
- `TilopayRepeat__Enabled=false` hasta validar TEST
- `TilopayRepeat__UseHostedLinks=true`
- `TilopayRepeat__EnableTestRecurringPlan=false`
- `TilopayRepeat__UseRecurringCheckoutForPublicPlans=false` hasta aprobacion final
- Hosted links reales configurados solo despues de validar que no exista `Costo de activacion`

## Checklist antes de activar planes reales

- Confirmar que el plan TEST 5834 cobra hoy `1000.00` y no `2000.00`
- Confirmar que Tilopay no muestra `Costo de activacion`
- Confirmar que el webhook llega a `https://app.luxurycloud.app/api/webhooks/tilopay`
- Confirmar que `Payments__PublicBaseUrl` coincide con el dominio publico detras de Nginx
- Confirmar `UseForwardedHeaders` activo y cookies seguras detras del proxy
- Confirmar que el tenant queda `Active` solo despues de pago aprobado
- Confirmar que un pago fallido o abandonado no activa acceso
