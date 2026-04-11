SET NOCOUNT ON;

PRINT 'Users without valid tenant';
SELECT
    u.Id,
    u.Email,
    u.TenantId
FROM AspNetUsers u
LEFT JOIN Tenants t ON u.TenantId = t.Id
WHERE u.TenantId IS NULL
   OR u.TenantId = '00000000-0000-0000-0000-000000000000'
   OR t.Id IS NULL;

PRINT 'Tenants without users';
SELECT
    t.Id,
    t.Nombre,
    t.Activo,
    t.FechaCreacion
FROM Tenants t
LEFT JOIN AspNetUsers u ON u.TenantId = t.Id
WHERE u.Id IS NULL;

PRINT 'Tenant isolation orphans by table';
SELECT 'Suscripciones' AS Tabla, COUNT(*) AS RegistrosInvalidos
FROM Suscripciones s
LEFT JOIN Tenants t ON s.TenantId = t.Id
WHERE t.Id IS NULL
UNION ALL
SELECT 'Facturas', COUNT(*)
FROM Facturas f
LEFT JOIN Tenants t ON f.TenantId = t.Id
WHERE t.Id IS NULL
UNION ALL
SELECT 'PagosSuscripcion', COUNT(*)
FROM PagosSuscripcion p
LEFT JOIN Tenants t ON p.TenantId = t.Id
WHERE t.Id IS NULL
UNION ALL
SELECT 'Clientes', COUNT(*)
FROM Clientes c
LEFT JOIN Tenants t ON c.TenantId = t.Id
WHERE t.Id IS NULL
UNION ALL
SELECT 'Funcionarios', COUNT(*)
FROM Funcionarios f
LEFT JOIN Tenants t ON f.TenantId = t.Id
WHERE t.Id IS NULL
UNION ALL
SELECT 'Servicios', COUNT(*)
FROM Servicios s
LEFT JOIN Tenants t ON s.TenantId = t.Id
WHERE t.Id IS NULL
UNION ALL
SELECT 'Productos', COUNT(*)
FROM Productos p
LEFT JOIN Tenants t ON p.TenantId = t.Id
WHERE t.Id IS NULL;

PRINT 'Tenant summary';
SELECT
    t.Id,
    t.Nombre,
    t.Activo,
    COUNT(DISTINCT u.Id) AS Usuarios,
    COUNT(DISTINCT s.Id) AS Suscripciones,
    COUNT(DISTINCT c.NumeroTelefono) AS Clientes,
    COUNT(DISTINCT p.IdProducto) AS Productos
FROM Tenants t
LEFT JOIN AspNetUsers u ON u.TenantId = t.Id
LEFT JOIN Suscripciones s ON s.TenantId = t.Id
LEFT JOIN Clientes c ON c.TenantId = t.Id
LEFT JOIN Productos p ON p.TenantId = t.Id
GROUP BY t.Id, t.Nombre, t.Activo
ORDER BY t.Nombre;
