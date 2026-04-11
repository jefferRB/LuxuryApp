SET NOCOUNT ON;
GO

PRINT 'Tenant tables without TenantId index';
SELECT t.name AS TableName
FROM sys.tables t
JOIN sys.columns c
    ON c.object_id = t.object_id
   AND c.name = 'TenantId'
WHERE NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    JOIN sys.index_columns ic
        ON ic.object_id = i.object_id
       AND ic.index_id = i.index_id
    JOIN sys.columns c2
        ON c2.object_id = ic.object_id
       AND c2.column_id = ic.column_id
    WHERE i.object_id = t.object_id
      AND i.is_hypothetical = 0
      AND i.is_disabled = 0
      AND c2.name = 'TenantId'
)
ORDER BY t.name;
GO

PRINT 'Tenant tables without RLS policy';
SELECT t.name AS TableName
FROM sys.tables t
JOIN sys.columns c
    ON c.object_id = t.object_id
   AND c.name = 'TenantId'
WHERE NOT EXISTS
(
    SELECT 1
    FROM sys.security_predicates pr
    WHERE pr.target_object_id = t.object_id
)
ORDER BY t.name;
GO

PRINT 'Current fnTenantAccess definition';
EXEC sp_helptext 'dbo.fnTenantAccess';
GO
