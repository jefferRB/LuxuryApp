/* =============================================================================
   PlatformCommercialGovernanceVerification.sql — Verificación READ-ONLY del
   refactor de gobierno comercial de plataforma (Mission Control / ficha de tenant).
   SOLO SELECT. No modifica nada, no requiere migración.

   Cubre la evidencia de:
     A) Contacto principal (owner) por tenant, con la MISMA regla del código:
        Administrador > Funcionario, nunca por orden alfabético de correo.
     B) Plan forzado y límite de funcionarios efectivo por tenant.
     C) Que el selector de plan base sólo pueda ofrecer planes base válidos
        (ningún WA400/WA800/WA1200, legacy/prueba separados).
     D) compra1 / compra2 intactos (plan base y add-on pagados por TiloPay).
     E) Luxe: acceso exento + plan forzado de 5 + WhatsApp ManualGrant/Barter.
     F) Que no haya dinero en riesgo por accesos manuales.

   NOTA: la regla de owner del código desempata por Registrado → activo → correo
   confirmado → correo alfabético. Aquí se replica igual para que el SQL y la app
   den el MISMO resultado; si difieren, hay un bug en uno de los dos.
   ============================================================================= */

SET NOCOUNT ON;
DECLARE @Now datetime2 = SYSUTCDATETIME();

PRINT '=== A) CONTACTO PRINCIPAL (owner) POR TENANT ===';

/* Clasificación de cuentas igual que TenantOwnerResolver: un Administrador es
   Administrador aunque además tenga rol Funcionario; FuncionarioId también marca
   cuenta de funcionario. */
WITH UserRolesFlat AS (
    SELECT u.Id                AS UserId,
           u.TenantId,
           u.Email,
           u.Name,
           u.State,
           u.EmailConfirmed,
           u.IsPlatformSuperAdmin,
           u.FuncionarioId,
           MAX(CASE WHEN r.Name = 'Administrador' THEN 1 ELSE 0 END) AS IsAdmin,
           MAX(CASE WHEN r.Name = 'Registrado'    THEN 1 ELSE 0 END) AS IsRegistrado,
           MAX(CASE WHEN r.Name = 'Funcionario'   THEN 1 ELSE 0 END) AS HasFuncionarioRole,
           STRING_AGG(r.Name, ', ') AS Roles
    FROM dbo.AspNetUsers u
    LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
    LEFT JOIN dbo.AspNetRoles     r  ON r.Id = ur.RoleId
    GROUP BY u.Id, u.TenantId, u.Email, u.Name, u.State, u.EmailConfirmed,
             u.IsPlatformSuperAdmin, u.FuncionarioId
),
Classified AS (
    SELECT *,
           CASE
               WHEN IsAdmin = 1 THEN 'Administrador'
               WHEN FuncionarioId IS NOT NULL OR HasFuncionarioRole = 1 THEN 'Funcionario'
               ELSE 'Otro'
           END AS Kind
    FROM UserRolesFlat
    WHERE IsPlatformSuperAdmin = 0   -- las cuentas de plataforma no representan al negocio
),
Ranked AS (
    SELECT *,
           ROW_NUMBER() OVER (
               PARTITION BY TenantId
               ORDER BY
                   /* Nivel de preferencia (1 = mejor). Nunca un Funcionario por
                      encima de un Administrador. */
                   CASE
                       WHEN Kind = 'Administrador' AND State = 1 AND IsRegistrado = 1 THEN 1
                       WHEN Kind = 'Administrador' AND State = 1                      THEN 2
                       WHEN Kind = 'Administrador'                                    THEN 3
                       WHEN Kind = 'Otro'          AND State = 1                      THEN 4
                       ELSE 5
                   END,
                   /* Desempate estable dentro del nivel. */
                   IsRegistrado    DESC,
                   State           DESC,
                   EmailConfirmed  DESC,
                   Email           ASC
           ) AS OwnerRank
    FROM Classified
)
SELECT 'A1. Owner resuelto por regla' AS Check_,
       t.Nombre                    AS TenantName,
       o.Email                     AS OwnerEmail,
       o.Name                      AS OwnerName,
       o.Kind                      AS OwnerKind,
       o.Roles                     AS OwnerRoles,
       o.State                     AS OwnerActivo,
       /* Comparación con el bug anterior: primer correo alfabético del tenant. */
       alfa.Email                  AS Owner_ReglaVieja_Alfabetica,
       CASE WHEN o.Email = alfa.Email THEN 'igual'
            ELSE 'CORREGIDO (antes mostraba el alfabético)' END AS Efecto,
       CASE WHEN o.Kind <> 'Administrador'
            THEN 'ALERTA: el contacto no es administrador' ELSE '' END AS Advertencia
FROM dbo.Tenants t
LEFT JOIN Ranked o
       ON o.TenantId = t.Id AND o.OwnerRank = 1
OUTER APPLY (
    SELECT TOP 1 u2.Email
    FROM dbo.AspNetUsers u2
    WHERE u2.TenantId = t.Id
    ORDER BY u2.Email
) alfa
ORDER BY t.Nombre;

/* A2. Tenants con inconsistencias de cuentas (lo que la ficha muestra como alerta). */
SELECT 'A2. Inconsistencias de cuentas' AS Check_,
       t.Nombre AS TenantName,
       SUM(CASE WHEN c.Kind = 'Administrador' THEN 1 ELSE 0 END) AS Admins,
       SUM(CASE WHEN c.Kind = 'Administrador' AND c.State = 1 THEN 1 ELSE 0 END) AS AdminsActivos,
       SUM(CASE WHEN c.Kind = 'Funcionario'  THEN 1 ELSE 0 END) AS Funcionarios,
       SUM(CASE WHEN c.Kind = 'Otro'         THEN 1 ELSE 0 END) AS Otros,
       CASE
           WHEN SUM(CASE WHEN c.Kind = 'Administrador' THEN 1 ELSE 0 END) = 0
               THEN 'SIN ADMINISTRADOR'
           WHEN SUM(CASE WHEN c.Kind = 'Administrador' AND c.State = 1 THEN 1 ELSE 0 END) = 0
               THEN 'ADMIN(ES) DESACTIVADO(S)'
           WHEN SUM(CASE WHEN c.Kind = 'Administrador' THEN 1 ELSE 0 END) > 1
               THEN 'VARIOS ADMINISTRADORES'
           ELSE 'ok'
       END AS Diagnostico
FROM dbo.Tenants t
LEFT JOIN (
    SELECT u.TenantId,
           u.State,
           CASE
               WHEN MAX(CASE WHEN r.Name = 'Administrador' THEN 1 ELSE 0 END) = 1 THEN 'Administrador'
               WHEN u.FuncionarioId IS NOT NULL
                 OR MAX(CASE WHEN r.Name = 'Funcionario' THEN 1 ELSE 0 END) = 1   THEN 'Funcionario'
               ELSE 'Otro'
           END AS Kind
    FROM dbo.AspNetUsers u
    LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
    LEFT JOIN dbo.AspNetRoles     r  ON r.Id = ur.RoleId
    WHERE u.IsPlatformSuperAdmin = 0
    GROUP BY u.Id, u.TenantId, u.State, u.FuncionarioId
) c ON c.TenantId = t.Id
GROUP BY t.Id, t.Nombre
HAVING SUM(CASE WHEN c.Kind = 'Administrador' THEN 1 ELSE 0 END) <> 1
    OR SUM(CASE WHEN c.Kind = 'Administrador' AND c.State = 1 THEN 1 ELSE 0 END) = 0
ORDER BY t.Nombre;

/* A3. Cuentas con rol Administrador Y Funcionario a la vez (AppRoles dice que no deben mezclarse). */
SELECT 'A3. Admin + Funcionario en la misma cuenta' AS Check_,
       t.Nombre AS TenantName, u.Email, STRING_AGG(r.Name, ', ') AS Roles, u.FuncionarioId
FROM dbo.AspNetUsers u
JOIN dbo.Tenants t ON t.Id = u.TenantId
JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
GROUP BY t.Nombre, u.Email, u.FuncionarioId
HAVING SUM(CASE WHEN r.Name = 'Administrador' THEN 1 ELSE 0 END) = 1
   AND SUM(CASE WHEN r.Name = 'Funcionario'   THEN 1 ELSE 0 END) = 1
ORDER BY t.Nombre;


PRINT '=== B) PLAN FORZADO Y LIMITE EFECTIVO ===';

/* Réplica de TenantCommercialAccessResolver: el plan forzado manda para exento/interno;
   la suscripción manda para RequiresSubscription. */
SELECT 'B1. Plan y limite efectivo por tenant' AS Check_,
       t.Nombre AS TenantName,
       CASE t.CommercialAccessMode
            WHEN 0 THEN 'RequiresSubscription'
            WHEN 1 THEN 'Exempt'
            WHEN 2 THEN 'Internal'
            WHEN 3 THEN 'PendingVerification'
            ELSE CONVERT(varchar(20), t.CommercialAccessMode)
       END AS CommercialAccessMode,
       fp.Codigo    AS ForcedPlanCode,
       fp.Nombre    AS ForcedPlanName,
       fp.MaxFuncionarios AS ForcedPlanLimit,
       s.CodigoPlan AS SubscriptionPlanCode,
       COALESCE(s.MaxFuncionarios, sp.MaxFuncionarios) AS SubscriptionLimit,
       /* Límite EFECTIVO: lo que el código usa para display Y enforcement. */
       CASE
           WHEN t.CommercialAccessMode IN (1, 2) AND fp.Id IS NOT NULL AND fp.Activo = 1
               THEN fp.MaxFuncionarios
           ELSE COALESCE(s.MaxFuncionarios, sp.MaxFuncionarios)
       END AS LimiteEfectivo,
       fa.ActivosCount AS FuncionariosActivos,
       /* Este es exactamente el bug reportado: plan forzado 3/5 y la cuenta mostraba 7. */
       CASE
           WHEN t.CommercialAccessMode IN (1, 2)
            AND fp.MaxFuncionarios IS NOT NULL
            AND COALESCE(s.MaxFuncionarios, sp.MaxFuncionarios) IS NOT NULL
            AND fp.MaxFuncionarios <> COALESCE(s.MaxFuncionarios, sp.MaxFuncionarios)
               THEN 'DIVERGENCIA plan forzado vs suscripcion (antes se mostraba el de la suscripcion)'
           ELSE ''
       END AS Nota,
       CASE
           WHEN fa.ActivosCount > CASE
                   WHEN t.CommercialAccessMode IN (1, 2) AND fp.Id IS NOT NULL AND fp.Activo = 1
                       THEN fp.MaxFuncionarios
                   ELSE COALESCE(s.MaxFuncionarios, sp.MaxFuncionarios)
               END
               THEN 'EXCEDIDO'
           ELSE ''
       END AS Capacidad
FROM dbo.Tenants t
LEFT JOIN dbo.Planes fp ON fp.Id = t.ForcedPlanId
OUTER APPLY (
    SELECT TOP 1 s2.PlanId, s2.CodigoPlan, s2.MaxFuncionarios
    FROM dbo.Suscripciones s2
    WHERE s2.TenantId = t.Id
    ORDER BY COALESCE(s2.FechaUltimaActualizacionUtc, s2.FechaInicio) DESC, s2.FechaInicio DESC
) s
LEFT JOIN dbo.Planes sp ON sp.Id = s.PlanId
OUTER APPLY (
    SELECT COUNT(*) AS ActivosCount
    FROM dbo.Funcionarios f
    WHERE f.TenantId = t.Id AND f.Activo = 1
) fa
ORDER BY t.Nombre;

/* B2. Configuración inválida: plan forzado que NO es plan base (add-on WhatsApp).
      Debe devolver 0 filas; si devuelve algo, ese tenant quedó sin límite confiable. */
SELECT 'B2. Plan forzado que es add-on WhatsApp (debe ser 0 filas)' AS Check_,
       t.Nombre AS TenantName, fp.Codigo AS ForcedPlanCode, fp.Nombre AS ForcedPlanName,
       fp.MaxFuncionarios, fp.LimiteMensajesMensual
FROM dbo.Tenants t
JOIN dbo.Planes fp ON fp.Id = t.ForcedPlanId
WHERE fp.Codigo IN ('WA400', 'WA800', 'WA1200')
ORDER BY t.Nombre;

/* B3. Plan forzado legacy/prueba: permitido para migración, pero debe verse. */
SELECT 'B3. Plan forzado legacy o de prueba (migrar a LC_M_/LC_A_)' AS Check_,
       t.Nombre AS TenantName, fp.Codigo AS ForcedPlanCode, fp.MaxFuncionarios, fp.EsPlanValidacion
FROM dbo.Tenants t
JOIN dbo.Planes fp ON fp.Id = t.ForcedPlanId
WHERE fp.Codigo IN ('BASIC', 'PRO', 'BUSINESS', 'TEST_RECURRING', 'TEST_PROD_BASIC_100')
   OR fp.EsPlanValidacion = 1
ORDER BY t.Nombre;

/* B4. Exento/interno SIN plan forzado válido: no tienen acceso ni límite. */
SELECT 'B4. Exento/interno sin plan forzado valido' AS Check_,
       t.Nombre AS TenantName, t.CommercialAccessMode, t.ForcedPlanId,
       CASE WHEN t.ForcedPlanId IS NULL THEN 'sin plan forzado'
            ELSE 'plan forzado inactivo o inexistente' END AS Diagnostico
FROM dbo.Tenants t
LEFT JOIN dbo.Planes fp ON fp.Id = t.ForcedPlanId AND fp.Activo = 1
WHERE t.CommercialAccessMode IN (1, 2)
  AND t.Activo = 1
  AND fp.Id IS NULL
ORDER BY t.Nombre;


PRINT '=== C) CONTENIDO DEL SELECTOR DE PLAN BASE ===';

/* C1. Clasificación de TODO el catálogo activo, igual que PlanCatalogRules.
      "Selector base" debe contener SOLO BaseCommercial; "Avanzado" legacy/validación;
      los add-ons WhatsApp NO deben aparecer en ninguno de los dos. */
SELECT 'C1. Clasificacion del catalogo' AS Check_,
       p.Codigo, p.Nombre, p.MaxFuncionarios, p.LimiteMensajesMensual,
       p.BillingCycle, p.EsPlanValidacion,
       CASE
           WHEN p.EsPlanValidacion = 1                                THEN 'Validation'
           WHEN p.Codigo IN ('WA400','WA800','WA1200')                THEN 'WhatsAppAddon'
           WHEN p.Codigo IN ('TEST_RECURRING','TEST_PROD_BASIC_100')  THEN 'Validation'
           WHEN p.Codigo LIKE 'LC[_]M[_]%' OR p.Codigo LIKE 'LC[_]A[_]%' THEN 'BaseCommercial'
           WHEN p.Codigo IN ('BASIC','PRO','BUSINESS')                THEN 'LegacyBase'
           ELSE 'Unknown'
       END AS PlanCatalogKind,
       CASE
           WHEN p.EsPlanValidacion = 1                                THEN 'Avanzado'
           WHEN p.Codigo IN ('WA400','WA800','WA1200')                THEN 'NO SELECCIONABLE (add-on)'
           WHEN p.Codigo IN ('TEST_RECURRING','TEST_PROD_BASIC_100')  THEN 'Avanzado'
           WHEN p.Codigo LIKE 'LC[_]M[_]%' OR p.Codigo LIKE 'LC[_]A[_]%' THEN 'Selector base'
           WHEN p.Codigo IN ('BASIC','PRO','BUSINESS')                THEN 'Avanzado'
           ELSE 'NO SELECCIONABLE (sin clasificar)'
       END AS UbicacionEnSelector
FROM dbo.Planes p
WHERE p.Activo = 1
ORDER BY UbicacionEnSelector, p.Codigo;

/* C2. Planes base comerciales SIN MaxFuncionarios: no podrían definir un límite. */
SELECT 'C2. Plan base comercial sin MaxFuncionarios (revisar)' AS Check_,
       p.Codigo, p.Nombre, p.MaxFuncionarios
FROM dbo.Planes p
WHERE p.Activo = 1
  AND (p.Codigo LIKE 'LC[_]M[_]%' OR p.Codigo LIKE 'LC[_]A[_]%')
  AND p.MaxFuncionarios IS NULL
ORDER BY p.Codigo;


PRINT '=== D) compra1 / compra2 INTACTOS ===';

/* D1. Plan base pagado por TiloPay: debe seguir con proveedor y suscriptor real. */
SELECT 'D1. Suscripcion base recurrente TiloPay' AS Check_,
       t.Nombre AS TenantName, s.CodigoPlan, s.Estado, s.MaxFuncionarios,
       s.Proveedor, s.TilopayRecurringPlanId,
       RIGHT(ISNULL(s.ProviderSubscriptionId, ''), 6) AS ProviderSubSuffix,
       s.FechaProximoCobroUtc,
       /* El refactor no debe haber tocado a los tenants pagados. */
       CASE t.CommercialAccessMode
            WHEN 0 THEN 'RequiresSubscription (correcto para pagado)'
            ELSE 'REVISAR: tenant pagado con modo especial'
       END AS ModoComercial
FROM dbo.Suscripciones s
JOIN dbo.Tenants t ON t.Id = s.TenantId
WHERE s.ProviderSubscriptionId IS NOT NULL
  AND s.ProviderSubscriptionId <> ''
  AND s.Estado IN (1, 2, 3)   -- Activa / Trial / Morosa (según EstadoSuscripcion)
ORDER BY t.Nombre;

/* D2. Add-on WhatsApp pagado por TiloPay: BillingSource = 0 (ProviderRecurring) CON provider sub. */
SELECT 'D2. Add-on WhatsApp pagado (BillingSource=0, con provider sub)' AS Check_,
       t.Nombre AS TenantName, a.AddonCode, a.BillingSource, a.Estado,
       RIGHT(ISNULL(a.ProviderSubscriptionId, ''), 6) AS ProviderSubSuffix,
       a.TilopayRecurringPlanId, a.MonthlyMessageLimit, a.FechaProximoCobroUtc
FROM dbo.TenantSubscriptionAddons a
JOIN dbo.Tenants t ON t.Id = a.TenantId
WHERE a.BillingSource = 0
  AND a.ProviderSubscriptionId IS NOT NULL
  AND a.ProviderSubscriptionId <> ''
ORDER BY t.Nombre;


PRINT '=== E) LUXE: EXENTO + PLAN 5 + WA MANUALGRANT/BARTER ===';

SELECT 'E1. Luxe estado comercial completo' AS Check_,
       t.Nombre AS TenantName,
       CASE t.CommercialAccessMode
            WHEN 0 THEN 'RequiresSubscription' WHEN 1 THEN 'Exempt'
            WHEN 2 THEN 'Internal' WHEN 3 THEN 'PendingVerification'
            ELSE CONVERT(varchar(20), t.CommercialAccessMode) END AS AccessMode,
       fp.Codigo AS ForcedPlanCode,
       fp.MaxFuncionarios AS LimiteEfectivo,
       fa.ActivosCount AS FuncionariosActivos,
       a.AddonCode,
       CASE a.BillingSource
            WHEN 0 THEN 'ProviderRecurring' WHEN 1 THEN 'ManualGrant'
            WHEN 2 THEN 'Legacy' ELSE CONVERT(varchar(20), a.BillingSource) END AS AddonSource,
       a.ManualGrantType,
       a.IsManualGrantIndefinite,
       a.ManualGrantExpiresAtUtc,
       CASE WHEN a.ProviderSubscriptionId IS NULL OR a.ProviderSubscriptionId = ''
            THEN 'sin provider (correcto para manual)'
            ELSE 'CON provider (revisar)' END AS AddonProviderState,
       /* El contacto que debe mostrarse en el listado y la ficha. */
       owner.Email AS OwnerEsperado
FROM dbo.Tenants t
LEFT JOIN dbo.Planes fp ON fp.Id = t.ForcedPlanId
LEFT JOIN dbo.TenantSubscriptionAddons a
       ON a.TenantId = t.Id AND a.Estado IN (1, 3)
OUTER APPLY (
    SELECT COUNT(*) AS ActivosCount
    FROM dbo.Funcionarios f WHERE f.TenantId = t.Id AND f.Activo = 1
) fa
OUTER APPLY (
    SELECT TOP 1 u.Email
    FROM dbo.AspNetUsers u
    LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
    LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
    WHERE u.TenantId = t.Id AND u.IsPlatformSuperAdmin = 0
    GROUP BY u.Id, u.Email, u.State, u.EmailConfirmed
    ORDER BY
        MAX(CASE WHEN r.Name = 'Administrador' THEN 0 ELSE 1 END) ASC,
        MAX(CASE WHEN r.Name = 'Registrado'    THEN 0 ELSE 1 END) ASC,
        u.State DESC, u.EmailConfirmed DESC, u.Email ASC
) owner
WHERE t.Nombre LIKE '%Luxe%'
ORDER BY t.Nombre;


PRINT '=== F) DINERO EN RIESGO / SALUD ===';

/* F1. RIESGO DE DINERO del add-on: ProviderRecurring activo SIN ProviderSubscriptionId.
       DEBE devolver 0 filas. Los manuales/legacy NUNCA aparecen aquí. */
SELECT 'F1. PaidAddonsActiveWithoutProviderRisk (debe ser 0 filas)' AS Check_,
       t.Nombre AS TenantName, a.AddonCode, a.BillingSource, a.Estado,
       a.TilopayRecurringPlanId, a.ProviderSubscriptionId
FROM dbo.TenantSubscriptionAddons a
JOIN dbo.Tenants t ON t.Id = a.TenantId
WHERE a.BillingSource = 0
  AND a.Estado IN (1, 3)
  AND a.TilopayRecurringPlanId IS NOT NULL
  AND (a.ProviderSubscriptionId IS NULL OR a.ProviderSubscriptionId = '')
ORDER BY t.Nombre;

/* F2. Add-on activo SIN plan base con acceso (regla 11) EXCLUYENDO los tenants con
       acceso otorgado por plataforma. Luxe NO debe aparecer acá. */
SELECT 'F2. Add-on sin plan base (excluye exento/interno con plan forzado)' AS Check_,
       t.Nombre AS TenantName, a.AddonCode,
       CASE a.BillingSource WHEN 0 THEN 'ProviderRecurring'
            WHEN 1 THEN 'ManualGrant' WHEN 2 THEN 'Legacy' END AS AddonSource,
       s.CodigoPlan AS BasePlanCode, s.Estado AS BaseEstado,
       'Alerta OPERATIVA (no billing)' AS Clasificacion
FROM dbo.TenantSubscriptionAddons a
JOIN dbo.Tenants t ON t.Id = a.TenantId
OUTER APPLY (
    SELECT TOP 1 s2.CodigoPlan, s2.Estado
    FROM dbo.Suscripciones s2
    WHERE s2.TenantId = a.TenantId
    ORDER BY COALESCE(s2.FechaUltimaActualizacionUtc, s2.FechaInicio) DESC
) s
WHERE a.Estado IN (1, 3)
  /* Excepción del refactor: acceso base otorgado por plataforma. */
  AND NOT (t.Activo = 1
           AND t.ForcedPlanId IS NOT NULL
           AND t.CommercialAccessMode IN (1, 2))
  AND (s.CodigoPlan IS NULL OR s.Estado NOT IN (1, 2, 3))
ORDER BY t.Nombre;

/* F3. Accesos manuales VENCIDOS con la fila todavía activa: alerta operativa, no dinero. */
SELECT 'F3. Acceso manual vencido pero fila activa (operativo)' AS Check_,
       t.Nombre AS TenantName, a.AddonCode, a.ManualGrantType,
       a.ManualGrantExpiresAtUtc, a.Estado
FROM dbo.TenantSubscriptionAddons a
JOIN dbo.Tenants t ON t.Id = a.TenantId
WHERE a.BillingSource = 1
  AND a.Estado IN (1, 3)
  AND a.IsManualGrantIndefinite = 0
  AND a.ManualGrantExpiresAtUtc IS NOT NULL
  AND a.ManualGrantExpiresAtUtc < @Now
ORDER BY t.Nombre;

/* F4. Resumen: accesos manuales vigentes (informativo, jamás dinero en riesgo). */
SELECT 'F4. Resumen de accesos por fuente' AS Check_,
       CASE a.BillingSource WHEN 0 THEN 'ProviderRecurring (dinero)'
            WHEN 1 THEN 'ManualGrant (informativo)'
            WHEN 2 THEN 'Legacy (limpieza)' END AS Fuente,
       COUNT(*) AS Addons,
       SUM(CASE WHEN a.ProviderSubscriptionId IS NOT NULL AND a.ProviderSubscriptionId <> ''
                THEN 1 ELSE 0 END) AS ConProviderSub
FROM dbo.TenantSubscriptionAddons a
WHERE a.Estado IN (1, 3)
GROUP BY a.BillingSource
ORDER BY a.BillingSource;
