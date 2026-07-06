# Arquitectura definitiva — Consola Interna de LuxuryCloud

**Documento de referencia arquitectónica oficial (pre-implementación)**
Fecha: 2026-07-05 · Rol: Principal Software Architect / Platform Engineer / SRE / Product Designer
Documento anterior: [auditoria-consola-plataforma.md](auditoria-consola-plataforma.md) — este documento la revisa críticamente y la reemplaza donde corresponde.

Horizonte de diseño: 5 años, decenas de miles de tenants, millones de transacciones/año, equipo interno que crece de 1 a ~5-7 personas. Sin código: solo arquitectura.

---

## 0. Método

La auditoría anterior partió de la implementación actual y propuso mejorarla. Este documento parte del problema ("operar un SaaS multi-tenant durante años con un equipo pequeño") y deriva la consola correcta desde primeros principios, usando la industria como evidencia. Al final (Fase 6) cada recomendación anterior se confirma, se corrige o se elimina.

---

## Fase 1 — Benchmarking: qué hacen igual todas las consolas maduras

Analizadas: Stripe, Shopify, Azure Portal, AWS Console, Cloudflare, Vercel, Supabase, Clerk, Auth0, Datadog, Microsoft 365 Admin Center. Lo relevante no son sus interfaces sino las decisiones que TODAS repiten, porque convergieron desde dominios distintos a las mismas soluciones.

### 1.1 Patrones universales

**P1 — La página de objeto es la unidad atómica, no el dashboard.**
Stripe es la demostración más pura: un pago, un cliente, una suscripción, un evento de webhook — cada uno tiene URL canónica y una página con el mismo esqueleto (identidad + estado → línea de tiempo → objetos relacionados → acciones). Azure/AWS lo llevan al extremo: miles de servicios distintos, una sola plantilla de "resource page" (Overview → Activity Log → Diagnose → Metrics). Clerk y Auth0 hacen lo mismo con usuarios. **Por qué:** el trabajo real de un operador es investigar casos concretos ("este pago", "este cliente"), y una plantilla uniforme significa que aprender a investigar un tipo de objeto enseña a investigar todos.

**P2 — Cada objeto tiene su línea de tiempo de eventos.**
El detalle de un pago en Stripe muestra cada webhook, cada intento, cada cambio de estado, en orden. Auth0 muestra cada login/fallo por usuario. Azure muestra el Activity Log por recurso. **Por qué:** el 90% del soporte es reconstruir "¿qué pasó con X?"; si la historia está pegada al objeto, la respuesta tarda segundos y viene con evidencia.

**P3 — Búsqueda universal por identificador, por encima de la navegación.**
Stripe (Cmd+K acepta IDs de cualquier objeto), Azure (barra global), Shopify, Clerk. **Por qué:** el operador llega a la consola con un dato en la mano (un email, un ID de transacción, el nombre de un negocio) sacado de un correo de soporte; navegar por menús hasta encontrarlo es tiempo perdido. La búsqueda es el puente entre "me reportaron algo" y "estoy viendo el expediente".

**P4 — El home es triage, nunca estadística.**
El home de Stripe muestra: cosas que requieren acción (disputas, revisiones), y el pulso del dinero de hoy. Shopify: "cosas por hacer". M365: Service Health + Message Center primero. Datadog directamente abre en monitores/incidentes. **Lo que nunca aparece en un home:** contadores totales sin acción asociada ("total de usuarios históricos"), tablas sin límite, formularios de configuración, IDs técnicos crudos. **Por qué:** el home se visita 20 veces al día en modo "¿hay algo roto?"; cada píxel que no responde esa pregunta es fricción multiplicada por 20.

**P5 — El trabajo pendiente vive en colas explícitas, no en tablas que hay que escanear.**
Disputas de Stripe, Reviews de fraude, colas de moderación de Shopify. Una cola = lista de objetos en estado accionable + antigüedad + conteo visible desde el home + acción de resolución. **Por qué:** una tabla dice "esto existe"; una cola dice "esto te espera, hace N días". La diferencia es la que hay entre información y operación.

**P6 — La salud del servicio es un accesorio permanente, con estados con vocabulario controlado.**
M365 y Azure tienen Service Health como elemento fijo; Datadog modela cada monitor con ciclo de vida (OK → Alert → Resolved); Cloudflare separa el status de la consola. Los colores significan siempre lo mismo (rojo = clientes/dinero afectados; ámbar = degradado; verde = sano). **Por qué:** el estado del sistema debe ser visible desde cualquier página sin navegar, y el color solo funciona como lenguaje si su semántica nunca cambia.

**P7 — Separación estricta entre observar y actuar.**
Leer es amplio y libre; mutar es escaso, confirmado, a veces re-autenticado, y siempre auditado (Auth0/Clerk con acciones peligrosas gateadas; Stripe con confirmaciones para reembolsos). **Por qué:** minimiza el radio de daño del error humano y hace que la auditoría cuente la historia completa.

**P8 — Escape hatch a los datos crudos.**
Stripe muestra el JSON de cada objeto; Supabase te deja ver la tabla; Datadog el evento crudo. **Por qué:** la UI nunca cubre el 100% de los casos; el operador avanzado necesita el dato completo sin ir a la base de datos.

**P9 — Divulgación progresiva.**
M365 (vista simplificada/avanzada), Azure (Overview primero, blades profundos después), Vercel/Supabase (mínimo arriba, detalle al hacer clic). **Por qué:** la misma consola sirve al novato y al experto si la profundidad es opt-in.

**P10 — Todo está acotado en el tiempo.**
24h / 7d / 30d consistentes en Stripe, Datadog, Cloudflare. Nada de "desde el inicio de los tiempos" salvo en expedientes. **Por qué:** los datos operativos solo significan algo relativo a una ventana; los totales históricos son decoración.

### 1.2 El anti-patrón que también enseña

Azure y AWS demuestran el costo del sprawl: cientos de entradas de navegación, curva de aprendizaje brutal. Se lo pueden permitir por su escala de equipo. **LuxuryCloud no.** La lección inversa: cada módulo de primer nivel que se agrega tiene un costo permanente de atención; la consola correcta para un equipo pequeño tiene un puñado de sustantivos, no un catálogo.

---

## Fase 2 — Filosofía de operación de LuxuryCloud

Cinco principios. Cada uno responde directamente a las preguntas obligatorias.

**F1 — Dos modos: triage e investigación. Nada más.**
La consola se usa en modo *triage* ("¿está sano el sistema?, ¿hay incidente crítico?, ¿qué necesita atención YA?") decenas de veces al día, y en modo *investigación* ("¿qué pasó con este cliente/pago?") algunas veces al día. El triage optimiza segundos (home + semáforo permanente); la investigación optimiza completitud (expediente + timeline + búsqueda). Todo elemento de UI debe declararse de uno de los dos modos; si no pertenece a ninguno, se elimina.

**F2 — Push para riesgo, pull para negocio.**
"¿Hay dinero en riesgo?" y "¿hay clientes afectados?" son push: la consola las grita (colas, semáforo, alertas externas). "¿Cuál es el estado comercial?" y "¿cuál es el crecimiento?" son pull: se consultan con ritmo semanal/mensual en su propia página. **Justificación:** el crecimiento nunca es una emergencia; mezclar MRR con incidentes en la misma pantalla entrena al operador a ignorar la pantalla (fatiga de dashboard). Esto **corrige la auditoría anterior**, que ponía MRR en el home.

**F3 — Toda anomalía es un ítem de trabajo con edad, no un número rojo.**
"3 webhooks con error" es información; "webhook de Tenant X con error hace 2 días" es trabajo. Las colas responden "¿qué necesita atención inmediata?" (ordenar por severidad × edad) y "¿qué puede esperar?" (lo que no está en cola, espera). La resolución de un ítem es el cambio de estado del objeto subyacente — no se construye un sistema de tickets aparte.

**F4 — Soporte basado en evidencia, nunca en memoria ni en SQL.**
Cada afirmación de un cliente ("no me llegó el recordatorio", "me cobraron mal") debe poder confirmarse o refutarse desde la consola con registros: mensajes, pagos, webhooks, cambios de estado, con timestamps. Si responder requiere SSH o una consulta SQL manual, es un defecto de la consola.

**F5 — Aburrida por defecto: diseñar el estado verde.**
Cuando no pasa nada, la consola debe decir explícitamente "todo sano, colas vacías" en una pantalla casi vacía. El estado vacío es el estado más frecuente y el más descuidado. Un operador debe poder cerrar el triage en 10 segundos con confianza total.

Respuesta directa a las 8 preguntas → elemento que la responde:

| Pregunta | Elemento | Latencia objetivo |
|---|---|---|
| ¿Está sano el sistema? | Semáforo permanente (todas las páginas) | 1 segundo |
| ¿Incidente crítico? | Banda de incidentes en Hoy | 5 segundos |
| ¿Dinero en riesgo? | Cola "Dinero en riesgo" con monto total | 5 segundos |
| ¿Clientes afectados? | Marcado de impacto en señales/colas | 10 segundos |
| ¿Qué necesita atención inmediata? | Colas ordenadas por severidad × edad | 10 segundos |
| ¿Qué puede esperar? | Todo lo que no está en cola | implícito |
| ¿Estado comercial? | Dinero → pestaña Negocio | 30 segundos (pull) |
| ¿Crecimiento? | Ídem, con historia mensual persistida | 30 segundos (pull) |

---

## Fase 3 — Personas

Perfiles y lo que cada uno necesita ver:

| Persona | Frecuencia | Pregunta dominante | Página de entrada |
|---|---|---|---|
| CEO | Semanal | ¿Crece el negocio? ¿Churn? | Dinero → Negocio |
| Operaciones | Diaria (20×) | ¿Algo roto? ¿Qué hago hoy? | Hoy |
| Soporte | Por ticket | ¿Qué pasó con este cliente? | Búsqueda → Expediente de tenant |
| Finanzas | Mensual | ¿Caja real vs MRR? ¿Fallidos? | Dinero → Operación + export |
| Ingeniería | Por incidente | ¿Dónde duele? ¿Desde cuándo? | Sistema |
| SRE | Continua (externa) | ¿Se cayó algo? | Endpoints JSON + monitor externo |
| Comercial | Semanal | ¿Trials? ¿Candidatos a upsell? | Dinero → Negocio (+ señal de cuota WhatsApp como oportunidad) |

**Decisión: una sola consola, sin vistas especializadas por rol.** Las personas difieren en el *punto de entrada*, no en la pantalla. Justificación:

1. Hoy todas las personas son literalmente la misma persona; construir 7 dashboards sería mantener 7 mentiras.
2. Las consolas de referencia pequeñas (Vercel, Clerk, Supabase) tienen una sola IA; solo M365/Azure segmentan por rol, y lo hacen porque tienen decenas de miles de administradores.
3. La segmentación correcta a futuro es por **permisos** (quién puede actuar vs solo leer), no por pantallas. La arquitectura debe permitirlo sin rediseño: cada área de navegación es un scope natural de autorización (Soporte = leer Tenants/Registro; Finanzas = leer Dinero; Ops = actuar). Esto exige desde ya URLs limpias por área y cero lógica de negocio en las vistas.

Detalle no obvio: la misma señal sirve a dos personas con significados distintos. "Tenant al 90% de cuota WhatsApp" es *riesgo operativo* para Ops (mensajes van a fallar) y *oportunidad comercial* para Comercial (candidato a upgrade de paquete). La señal se calcula una vez y se muestra en ambos contextos — un ejemplo del principio "una fuente de verdad por hecho".

---

## Fase 4 — Escenarios reales (validación del diseño)

Cada escenario se camina sobre la arquitectura propuesta (Fase 5). Formato: qué muestra la consola → qué acciones permite → tiempo hasta la causa. Se contrasta con el estado actual.

### E1. Meta deja de responder
- **Consola:** el probe de WhatsApp falla y la tasa de error outbound salta → semáforo global a rojo con etiqueta "WhatsApp". En Hoy: "WhatsApp caído desde 14:02 — 0/34 mensajes entregados — error 5xx de Meta — N tenants afectados". Clic → Sistema/WhatsApp: desglose por código de error, últimos envíos exitosos, tenants con recordatorios pendientes en riesgo.
- **Acciones:** ejecutar diagnóstico Meta (ya existe `TestConfigurationAsync`); **pausar el canal outbound** (interruptor de canal — capacidad nueva) para no quemar cuota/reputación contra una API caída; al restablecerse, reanudar y dejar que el worker drene pendientes.
- **Tiempo hasta la causa:** < 1 minuto. **Hoy:** se descubriría tenant por tenant en una tabla, o cuando reclamen los clientes.

### E2. Tilopay deja de enviar webhooks
- **Consola:** señal de frescura ("minutos desde el último webhook" vs línea base ya existente en BillingHealth) pasa a ámbar/rojo → Hoy: "Sin webhooks de Tilopay hace 6h; 3 checkouts esperando confirmación". Clic → Dinero/Webhooks: último recibido, último procesado, checkouts pendientes acumulándose con edad.
- **Acciones:** disparar el pase de reconciliación **ahora** (hoy solo corre cada 24h — el disparo manual es capacidad nueva); conciliar manualmente con evidencia del panel Tilopay (flujo ya existente en RecurringCheckouts).
- **Tiempo:** < 1 minuto en saberlo. **Hoy:** se sabe cuando un cliente paga y no se activa.

### E3. Un worker se detiene
- **Consola:** heartbeat vencido → semáforo rojo → Hoy: "ReminderWorker sin latido hace 12 min (último: 14:32)". La señal enlaza su **runbook** (reinicio systemd, qué verificar después).
- **Acciones:** la consola no reinicia procesos (correcto: eso es del sistema operativo); da diagnóstico + procedimiento. Tras el reinicio el heartbeat se pone verde solo.
- **Tiempo:** < 1 minuto. **Hoy: invisible** — el hueco más peligroso de toda la plataforma actual, porque los recordatorios simplemente dejan de salir en silencio.

### E4. SQL Server aumenta su latencia
- **Consola:** el check de BD cronometra un ping por ciclo y persiste muestras → ámbar con sparkline de latencia (última hora/día). Sistema muestra desde cuándo degrada, si coincide con un pase de reconciliación o con carga horaria.
- **Acciones:** evidencia para decidir (índice, plan de mantenimiento, recursos del servidor). Sin APM la consola no dirá *cuál query*; decir "desde cuándo y cuánto" ya reduce el diagnóstico de horas a minutos. APM es decisión futura consciente.
- **Tiempo:** minutos hasta acotar la ventana del problema.

### E5. Un tenant consume recursos excesivos
- **Consola:** señales de outlier a nivel de negocio: cuota WhatsApp al 100%, ráfaga anómala de reservas públicas (el rate limiter por IP ya existe; falta la vista), volumen de citas/cobros fuera de percentil. El expediente del tenant muestra uso vs límites del plan.
- **Acciones:** ajustar cuota, contactar, o suspender (gateado + auditado).
- **Honestidad arquitectónica:** sin telemetría por tenant a nivel de infraestructura (CPU/queries), la v1 detecta outliers *de negocio*, no de infraestructura. Suficiente para el 95% de los casos reales de este dominio (spam de reservas, abuso de mensajería).

### E6. "No me llegó el recordatorio de mi cita"
- **Consola:** búsqueda universal por nombre del negocio → Expediente → pestaña WhatsApp → timeline de mensajes del día: "Recordatorio de cita #123: programado 08:00, intentado 08:01, error 131047 (ventana 24h cerrada)" o "entregado 08:01". La evidencia ya existe (`WhatsAppMessageLogs` + estados de notificación); falta la vista cronológica.
- **Acciones:** responder al cliente con evidencia; si es un patrón, la señal agregada de errores lo habría mostrado antes.
- **Tiempo:** < 2 minutos con respuesta documentada. **Hoy:** solo se ve "el último error" del tenant; reconstruir un caso concreto exige SQL.

### E7. "Me cobraron mal"
- **Consola:** búsqueda → Expediente → pestaña Dinero → timeline financiero: pago X (monto, fecha, método), webhook que lo confirmó, periodo aplicado a la suscripción, plan vigente en ese momento, add-ons. Todo correlacionado (los tokens de correlación ya existen en el modelo).
- **Acciones:** veredicto con evidencia; si procede reembolso, se ejecuta en Tilopay (fuera de la consola) y se registra la resolución como nota auditada en el expediente.
- **Tiempo:** < 3 minutos. **Hoy:** posible pero disperso entre dashboard, BillingHealth y SQL.

### E8. Un webhook queda pendiente/en error
- **Consola:** cola "Webhooks por resolver" con edad; el ítem abre el expediente del evento: payload crudo (escape hatch), error, intentos de correlación, objetos relacionados (pago/suscripción si los hay).
- **Acciones:** **reprocesar** (seguro por diseño: la idempotencia por índice único de evento ya existe) o derivar a conciliación manual.
- **Tiempo:** < 1 minuto por ítem. **Hoy:** aparece en una tabla de 25 filas del dashboard, sin acción directa ni payload visible.

### E9. Falla una renovación
- **Consola:** el tenant entra a la cola "Dinero en riesgo": plan, monto, días de gracia restantes (cuenta regresiva), intentos de cobro. Si la gracia expira, pasa a "suspendido" con marca de impacto en cliente.
- **Acciones:** reintentar/conciliar, contactar al cliente desde el expediente, o dejar que la suspensión automática actúe (ya existe).
- **Tiempo:** el caso llega solo a la cola; cero descubrimiento manual.

**Conclusión de la fase:** los 9 escenarios se resuelven con exactamente 5 mecanismos: semáforo, colas, expedientes con timeline, señales con runbook, y acciones gateadas. Ningún escenario pidió un sexto mecanismo — esa es la señal de que la arquitectura es completa y mínima.

---

## Fase 5 — Arquitectura definitiva

### 5.1 Los cinco mecanismos (el esqueleto real de la consola)

La consola no se define por sus páginas sino por cinco patrones que toda página instancia:

1. **Semáforo global** — agregado de señales, visible en TODAS las páginas de la consola (verde/ámbar/rojo + conteo de ítems en cola). Un clic lleva a Hoy.
2. **Cola de trabajo** — lista de objetos en estado accionable, con edad, ordenada por severidad × antigüedad, con conteo en Hoy y acción de resolución. La resolución es el cambio de estado del objeto; no hay tickets.
3. **Expediente (página de objeto)** — URL canónica + plantilla uniforme: identidad y estado → línea de tiempo de eventos → objetos relacionados (links) → acciones gateadas → datos crudos (JSON). Objetos con expediente: tenant, suscripción, pago, evento de webhook, código promocional, usuario.
4. **Señal de salud** — check nombrado con: estado, evidencia (valor medido + umbral), timestamp, y **runbook** (qué hacer si está en rojo). Registro único de ~12 señales; el semáforo y la página Sistema son dos vistas del mismo registro.
5. **Acción gateada** — toda mutación sigue el patrón ya probado en desactivación de usuarios: confirmación proporcional al riesgo → (re-auth si es destructiva) → auditoría → resultado visible. Uniforme en toda la consola.

### 5.2 Navegación: 5 áreas + herramientas

```
┌────────────────────────────────────────────────────────────┐
│  ● Semáforo   [ Búsqueda universal…            ]   Usuario  │  ← permanente
├────────────────────────────────────────────────────────────┤
│  Hoy · Tenants · Dinero · Sistema · Registro        ⚙      │
└────────────────────────────────────────────────────────────┘

Hoy       → triage: incidentes activos + colas con conteo/edad + estado verde explícito
Tenants   → lista paginada/filtrable (diagnóstico) → Expediente 360 (operación)
Dinero    → pestañas: Operación (pagos, webhooks, conciliación, colas de dinero)
                      Negocio  (MRR, ARR, trials, churn, upgrades, próximos cobros)
Sistema   → señales técnicas: BD, disco, workers, correo, WhatsApp, Tilopay, retención
Registro  → auditoría inmutable (humana + automática), filtrable, exportable
⚙ Herramientas → códigos promocionales, resumen mensual, configuración de consola
```

Mapa mental del operador → área: *¿algo roto?* Hoy · *¿este cliente?* Tenants · *¿plata?* Dinero · *¿infra?* Sistema · *¿quién hizo qué?* Registro. Cinco sustantivos que se aprenden en un día.

Decisiones de navegación y su porqué:

- **WhatsApp no es módulo de primer nivel.** Es un canal. Su salud vive en Sistema (señal), su configuración por tenant vive en el Expediente, su cuota como oportunidad vive en Dinero/Negocio, y sus errores viven como cola en Hoy. Elevarlo a módulo (como hacía la propuesta anterior) mezcla cuatro intenciones distintas en una página sin dueño claro.
- **Comercial y Billing Ops son una sola área (Dinero) con dos pestañas.** Un cobro fallido es simultáneamente un problema operativo y un hueco en el MRR; separarlos en módulos obliga a saltar de contexto justo cuando la conexión importa. La pestaña Negocio queda "limpia" para el ritmo pull del CEO sin el ruido operativo.
- **Códigos y Resumen mensual se degradan a Herramientas.** Son configuración/instrumentos, no operación diaria. Mantienen sus rutas actuales; solo pierden protagonismo en la navegación.
- **Registro (auditoría) es primer nivel.** En un SaaS operado por humanos con poderes globales, "¿quién hizo qué?" es una de las cinco preguntas fundamentales, y dará servicio a compliance cuando haya clientes más grandes.

### 5.3 La página Hoy

```
[si todo verde]  ✓ Todo sano · 0 ítems en cola · último chequeo hace 40 s
                 (pantalla casi vacía, deliberadamente)

[si hay problemas]
① INCIDENTES (rojo: dinero o clientes afectados)
   ▸ WhatsApp caído desde 14:02 · 12 tenants afectados        → Sistema/WhatsApp
② COLAS DE TRABAJO (conteo + ítem más viejo)
   ▸ Dinero en riesgo ······· 3 (₡184.000 · el más viejo: 4 d)  → Dinero/Operación
   ▸ Webhooks por resolver ·· 2 (el más viejo: 6 h)
   ▸ Checkouts por conciliar  1 (2 h)
   ▸ WhatsApp con errores ··· 4 tenants (24 h)
   ▸ Reservas desatendidas ·· 2 tenants (>3 pendientes)
   ▸ Trials por vencer ≤7d ·· 5
③ PULSO (una línea, sin gráficas): pagos confirmados hoy · mensajes enviados hoy · tenants activos hoy
```

Sin MRR, sin totales históricos, sin tablas de 25 filas. El pulso (③) existe solo para detectar el "silencio anómalo" (cero pagos un día 1° es una alerta en sí misma).

### 5.4 El Expediente de tenant (evolución de la Ficha actual)

La Ficha 360 actual ya es correcta; se convierte en la instancia canónica del patrón Expediente con dos mejoras estructurales:

1. **Timeline unificado del tenant** (la mejora de mayor palanca de toda la consola): un solo feed cronológico que mezcla pagos, webhooks correlacionados, cambios de plan/estado, acciones de plataforma (auditoría), mensajes WhatsApp y correos enviados. Los datos ya existen en 6 tablas; la vista los une. Los escenarios E6 y E7 se resuelven enteros aquí.
2. **Toda la edición vive aquí** (comercial, WhatsApp, cuotas) — nunca en las listas. Las listas diagnostican; el expediente opera.

### 5.5 Arquitectura detrás de la UI (decisiones estructurales, sin código)

- **AD-1 · La consola permanece dentro del monolito.** No se extrae a una app separada. El modelo de seguridad actual (política PlatformSuperAdmin + RLS en SQL + misma BD) es una fortaleza; separar la consola duplicaría autenticación, secretos y despliegue sin beneficio a esta escala. *Cláusula de futuro:* los servicios de consola devuelven DTOs/snapshots serializables (nunca entidades EF a las vistas), de modo que un frontend separado sea posible en 5 años sin reescribir la capa de datos.
- **AD-2 · Capa de snapshot: el costo de la consola es O(1) respecto al número de tenants.** Hoy, semáforo y colas leen de un snapshot refrescado cada 30–60 s (worker o caché bajo demanda), nunca de un fan-out de N queries por vista. Las listas son queries indexadas, paginadas, con filtros server-side. Los expedientes sí consultan en vivo (frescura donde importa). Esta es la regla que hace válida la consola con 100 o con 50.000 tenants.
- **AD-3 · Registro de señales como sustrato único.** Una tabla de heartbeats/mediciones + un registro declarativo de señales (nombre, medición, umbrales, runbook). El semáforo, Hoy, Sistema y los endpoints JSON son *vistas* del mismo registro. Prohíbe el anti-patrón "cada página de salud con sus umbrales copiados" (hoy BillingHealth y una futura SystemHealth los duplicarían).
- **AD-4 · Historia comercial persistida desde ya.** Snapshot mensual (y opcionalmente diario) de MRR, tenants por estado, trials, churn. **El MRR histórico no se puede reconstruir después con fidelidad** (los planes y estados cambian); cada mes sin snapshot es historia perdida. Es la decisión con mayor asimetría costo/beneficio del documento: una tabla y un job mensual hoy, contra la imposibilidad de mostrar "crecimiento de 3 años" en 2029.
- **AD-5 · Retención de datos como arquitectura, no como limpieza.** Cada tabla de eventos declara su política: webhooks procesados 12 meses (luego archivo), mensajes WhatsApp 6 meses (agregando conteos mensuales por tenant antes de purgar — el timeline del expediente conserva el agregado), auditoría nunca se purga (solo archivado externo). A millones de transacciones/año esto no es opcional.
- **AD-6 · Interruptores de canal (kill switches).** WhatsApp outbound y correo pueden pausarse/reanudarse desde Sistema (gateado + auditado). Un canal externo caído no debe poder quemar cuota, reputación ni reintentos infinitos. Barato de construir, imposible de improvisar durante el incidente.
- **AD-7 · Reconciliación disparable a demanda.** El pase diario ya existe; el operador puede ejecutarlo manualmente desde Dinero (E2/E9). El diseño conservador del reconciliador (repara lo inequívoco, alerta lo ambiguo) se conserva intacto.
- **AD-8 · Server-rendered Razor se mantiene.** Ninguno de los cinco mecanismos requiere SPA. El semáforo se actualiza con un fragmento ligero con polling; el resto es navegación clásica. Cero reescritura de stack de UI.

### 5.6 Flujo operativo diario (el "día en la vida" que valida el diseño)

1. **08:00** — Abrir Hoy. Verde y colas vacías → cerrar (10 segundos). Algo en cola → vaciar por orden: dinero primero, luego webhooks, luego WhatsApp, luego reservas/trials.
2. **Durante el día** — Los tickets de soporte entran por búsqueda universal → expediente → timeline → respuesta con evidencia.
3. **Viernes** — Dinero/Negocio: MRR, trials, churn, próximos cobros de la semana entrante (15 minutos).
4. **Día 1 del mes** — Snapshot comercial automático; revisión mensual con historia real.
5. **Nunca** — Escanear tablas "por si acaso": si algo importa, está en una cola o en el semáforo. Este "nunca" es el criterio de éxito del diseño.

### 5.7 Por qué esta organización es superior

- **A la consola actual:** la actual tiene una página por *fuente de datos* (tabla de tenants, tabla de WhatsApp, tabla de eventos…); esta tiene una página por *intención del operador*. La actual exige escanear para descubrir problemas; esta los empuja a colas con edad. La actual responde "¿qué pasó con este cliente?" con SQL; esta con un timeline.
- **A la propuesta de mi auditoría anterior:** aquella conservaba 10 módulos feature-sliced y un home con métricas de negocio mezcladas con incidentes. Esta reduce a 5 áreas intent-sliced, degrada canales y herramientas, saca el negocio del triage, y añade los tres mecanismos que aquella no vio: búsqueda universal, timeline por objeto y colas con semántica de edad. La anterior mejoraba el dashboard; esta define una consola.

---

## Fase 6 — Validación crítica de la auditoría anterior

Revisión recomendación por recomendación. Veredictos: ✅ se mantiene · 🔄 se reemplaza · ⬆️ se refuerza · ❌ se elimina.

| # | Recomendación anterior | Veredicto | Justificación |
|---|---|---|---|
| 1 | Ola 1 completa de seguridad (MFA TOTP, password 12+, fix del validator de claim, log en SafeAudit, borrar código muerto, heartbeats + /health) | ✅ | Independiente de la arquitectura de UI. Sigue siendo lo primero que se implementa; nada aquí la contradice. |
| 2 | Dashboard de 8 tarjetas en 2 filas (salud + negocio, con MRR en el home) | 🔄 | **Me contradigo deliberadamente.** Poner MRR en el home viola F2 (push vs pull) y entrena a ignorar la pantalla. El home correcto es Hoy: incidentes + colas + estado verde. El MRR vive en Dinero/Negocio. |
| 3 | Navegación de 10 módulos (Inicio, Tenants, Comercial, Billing Ops, WhatsApp, Usuarios, Códigos, Resumen mensual, Salud, Auditoría) | 🔄 | Diez entradas para un equipo de 1–5 personas es sprawl (lección Azure/AWS). Se reemplaza por 5 áreas + Herramientas. WhatsApp deja de ser módulo (era la decisión más débil de la auditoría: mezclaba salud de canal, config por tenant y upsell en una página). Usuarios se integra en Tenants/Herramientas según contexto. |
| 4 | Módulo Comercial separado de Billing Ops | 🔄 | Un cobro fallido es evento operativo Y comercial a la vez. Un área (Dinero) con pestañas Operación/Negocio conserva la limpieza para el CEO sin romper la adyacencia para Ops. |
| 5 | "La lista diagnostica, la ficha opera" + mover edición a la Ficha | ⬆️ | Se mantiene y se generaliza: la Ficha se convierte en el patrón Expediente aplicado a *todos* los objetos (pagos, webhooks, códigos), con la mejora mayor: **timeline unificado por tenant**, que la auditoría no propuso. |
| 6 | Página SystemHealth separada, conviviendo con BillingHealth | 🔄 | Dos páginas de salud hechas a mano = umbrales duplicados que divergen. Se reemplaza por el registro único de señales (AD-3); Sistema y las señales de dinero en Hoy son vistas del mismo sustrato. El endpoint `/json` de BillingHealth fue la idea correcta y se generaliza. |
| 7 | `PlatformDashboardService` para sacar la agregación del controlador | ⬆️ | Se mantiene, pero elevado a AD-2: no basta mover el fan-out de queries a un servicio; hay que eliminarlo con la capa de snapshot. Un servicio que hace 3.000 queries sigue siendo el mismo problema con mejor nombre. |
| 8 | Resolver comercial en batch, índices compuestos, fix de queries en memoria | ✅ | Necesario bajo cualquier arquitectura; con AD-2 su urgencia baja para el home (que ya no hace fan-out) pero sigue siendo imprescindible para listas y expedientes. |
| 9 | Retención/archivado como ítem de Ola 3 | ⬆️ | Sube de "mantenimiento recomendado" a decisión arquitectónica (AD-5) con políticas explícitas por tabla. A horizonte de 5 años y millones de eventos, es estructural. |
| 10 | Monitoreo externo (Healthchecks.io/UptimeRobot) sobre endpoints JSON | ✅ | Se mantiene tal cual: alertas sin construir sistema de alertas. |
| 11 | Serilog + máscara de PII; OpenTelemetry como futuro | ✅ | Se mantiene. La consola propuesta reduce además la necesidad de leer logs crudos (las señales cubren el 90%). |
| 12 | Roles de plataforma como "futuro, cuando haya >1 operador" | ⬆️ | Se mantiene el "cuándo", pero la Fase 3 añade el "cómo": las 5 áreas son los scopes naturales de permisos, y las URLs/servicios deben diseñarse desde ya alineados a áreas para que activar roles sea configuración, no rediseño. |
| 13 | MFA, CSP, hardening Tilopay, DataProtection, rate limiting en login | ✅ | Sin cambios; ortogonales a este documento. |
| 14 | Snapshot mensual de métricas comerciales como "Ola 4 / futuro" | 🔄 | **Error de priorización en la auditoría.** La historia no se puede reconstruir retroactivamente; cada mes de espera es historia perdida para siempre. Sube a la primera ola de implementación (AD-4). Es probablemente la corrección más valiosa de esta validación. |
| 15 | Tarjeta "Pendiente de acción humana" en el home | ⬆️ | Era la semilla correcta; se formaliza como el patrón Cola de trabajo (mecanismo 2), con edad, orden por severidad y semántica de resolución. |

### Oportunidades nuevas (ausentes en la auditoría anterior)

1. **Búsqueda universal** — la omisión más importante. Es el mecanismo #1 de soporte en todas las consolas de referencia y no aparecía ni una vez en la auditoría.
2. **Timeline unificado por tenant** — convierte los dos escenarios de soporte más frecuentes (E6, E7) de "consulta SQL" a "2 minutos con evidencia".
3. **Runbooks adjuntos a señales** — una señal roja sin "qué hacer" solo transfiere ansiedad; con runbook transfiere procedimiento. Preparan además el onboarding del segundo operador.
4. **Kill switches de canal** (AD-6) — imposibles de improvisar durante el incidente que los necesita.
5. **Diseño explícito del estado verde** (F5) — el estado más frecuente de la consola no estaba diseñado.
6. **Decisión explícita de permanencia en el monolito** (AD-1) — la auditoría lo asumía en silencio; un documento de arquitectura a 5 años debe decidirlo con argumentos y cláusula de salida.
7. **Impersonación ("ver como el tenant")** — patrón estándar en Clerk/Shopify/Auth0 para soporte. Se registra como capacidad candidata **futura**, condicionada a: re-auth, banner permanente visible, sesión de solo lectura y auditoría completa. No entra en las primeras olas por su riesgo.

### Lo que NO cambia aunque la ambición sea a 5 años (anti-sobreingeniería)

- **No** Prometheus/Grafana/OTel hoy: el registro de señales + monitor externo cubre la operación actual; se adopta APM cuando un incidente real lo justifique.
- **No** Redis ni colas externas hoy: se introducen con la segunda instancia de la app, no antes (las "colas" de dominio ya viven correctamente en SQL con estados e idempotencia).
- **No** SPA ni app de administración separada: AD-1/AD-8.
- **No** sistema de tickets interno: las colas resuelven sobre el estado real de los objetos.
- **No** dashboards por persona: una consola, puntos de entrada distintos, permisos después.

---

## Métricas de éxito de la consola (criterios verificables)

| Métrica | Objetivo |
|---|---|
| Tiempo de triage con todo sano | ≤ 10 segundos |
| Tiempo de "reporte de cliente → evidencia en pantalla" | ≤ 2 minutos |
| Tiempo de "canal externo caído → detectado" | ≤ 5 minutos (sin esperar reclamo) |
| Trabajo pendiente descubierto por escaneo de tablas | 0 (todo llega por cola o semáforo) |
| Costo del home | O(1) respecto al número de tenants |
| Preguntas de las 8 fundamentales sin respuesta en consola | 0 |

## Orden de implementación sugerido (reemplaza el roadmap anterior donde corresponde)

1. **Ola 1 (sin cambios):** seguridad + heartbeats + /health + monitor externo, **más** el snapshot comercial mensual (AD-4, subido de prioridad).
2. **Ola 2 (redefinida):** registro de señales + página Hoy con colas + semáforo permanente + búsqueda universal + lista de tenants paginada con edición movida al Expediente.
3. **Ola 3 (redefinida):** timeline unificado del tenant + Dinero (Operación/Negocio) + expedientes de pago/webhook + reconciliación a demanda + kill switches.
4. **Ola 4:** retención/archivado (AD-5), CSP/hardening pendientes, herramientas degradadas, impersonación si se justifica.

Regla invariable: cada ola mantiene las rutas existentes funcionando, cierra con los tests de aislamiento verdes y no mezcla seguridad con UI en un mismo cambio.

---

*Este documento es la referencia arquitectónica oficial de la consola interna. Cualquier implementación futura debe citar qué sección materializa; cualquier desviación debe registrarse aquí con su justificación.*
