SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Categorias_TenantId' AND object_id = OBJECT_ID('dbo.Categorias'))
    CREATE INDEX IX_Categorias_TenantId ON dbo.Categorias (TenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClienteImagenes_TenantId' AND object_id = OBJECT_ID('dbo.ClienteImagenes'))
    CREATE INDEX IX_ClienteImagenes_TenantId ON dbo.ClienteImagenes (TenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClienteVisitas_TenantId' AND object_id = OBJECT_ID('dbo.ClienteVisitas'))
    CREATE INDEX IX_ClienteVisitas_TenantId ON dbo.ClienteVisitas (TenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DetalleCobroProductos_TenantId' AND object_id = OBJECT_ID('dbo.DetalleCobroProductos'))
    CREATE INDEX IX_DetalleCobroProductos_TenantId ON dbo.DetalleCobroProductos (TenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Egresos_TenantId' AND object_id = OBJECT_ID('dbo.Egresos'))
    CREATE INDEX IX_Egresos_TenantId ON dbo.Egresos (TenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventosPago_TenantId' AND object_id = OBJECT_ID('dbo.EventosPago'))
    CREATE INDEX IX_EventosPago_TenantId ON dbo.EventosPago (TenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MovimientosInventario_TenantId' AND object_id = OBJECT_ID('dbo.MovimientosInventario'))
    CREATE INDEX IX_MovimientosInventario_TenantId ON dbo.MovimientosInventario (TenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PagosFuncionarios_TenantId' AND object_id = OBJECT_ID('dbo.PagosFuncionarios'))
    CREATE INDEX IX_PagosFuncionarios_TenantId ON dbo.PagosFuncionarios (TenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Puestos_TenantId' AND object_id = OBJECT_ID('dbo.Puestos'))
    CREATE INDEX IX_Puestos_TenantId ON dbo.Puestos (TenantId);
GO
