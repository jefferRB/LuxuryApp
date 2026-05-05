using System.Linq.Expressions;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Common;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Legal;
using LuxuryApp.Models.Productos;
using LuxuryApp.Models.Saas;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Tenant;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProyectoIdentity.Datos
{
    public class ApplicationDbContext : IdentityDbContext<AppUsuario>
    {
        private readonly ITenantProvider _tenantProvider;
        private readonly ILogger<ApplicationDbContext> _logger;

        public ApplicationDbContext(
            DbContextOptions options,
            ITenantProvider tenantProvider,
            ILogger<ApplicationDbContext> logger) : base(options)
        {
            _tenantProvider = tenantProvider
                ?? throw new Exception("TenantProvider no disponible");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // 🔥 PRO: Obtener tenant dinámico (NO en constructor)
        private Guid CurrentTenantId
        {
            get
            {
                if (!_tenantProvider.HasTenant())
                    return Guid.Empty;

                return _tenantProvider.GetTenantId();
            }
        }

        // 🔥 QUERY FILTER NIVEL DIOS
        private LambdaExpression CreateTenantFilter(Type type)
        {
            var parameter = Expression.Parameter(type, "e");

            var property = Expression.Property(parameter, nameof(ITenantEntity.TenantId));

            var tenantId = Expression.Property(
                Expression.Constant(this),
                nameof(CurrentTenantId)
            );

            var body = Expression.OrElse(
                Expression.Equal(tenantId, Expression.Constant(Guid.Empty)), // permite públicos
                Expression.Equal(property, tenantId)
            );

            return Expression.Lambda(body, parameter);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 🔥 CONFIGURACIÓN PLANFEATURE (PK COMPUESTA)
            modelBuilder.Entity<PlanFeature>(entity =>
            {
                entity.HasKey(pf => new { pf.PlanId, pf.FeatureId });

                entity.HasOne(pf => pf.Plan)
                    .WithMany(p => p.PlanFeatures)
                    .HasForeignKey(pf => pf.PlanId);

                entity.HasOne(pf => pf.Feature)
                    .WithMany(f => f.PlanFeatures)
                    .HasForeignKey(pf => pf.FeatureId);

                entity.Property(pf => pf.Limite)
                    .IsRequired(false);
            });

            modelBuilder.Entity<Plan>(entity =>
            {
                entity.Property(p => p.Nombre).IsRequired();
                entity.Property(p => p.Moneda).HasMaxLength(100);
                entity.Property(p => p.EsPlanValidacion).HasDefaultValue(false);
                entity.Property(p => p.PrecioMensual).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<Funcionario>(entity =>
            {
                entity.HasOne(f => f.Puesto)
                    .WithMany(p => p.Funcionarios)
                    .HasForeignKey(f => f.IdPuesto)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(f => f.PorcentajeGanancia).HasColumnType("decimal(18,2)");
                entity.Property(f => f.PorcentajeProducto).HasColumnType("decimal(18,2)");

                entity.HasIndex(f => new { f.TenantId, f.Nombre })
                    .HasDatabaseName("IX_Funcionarios_TenantId_Nombre");
            });

            modelBuilder.Entity<Cita>(entity =>
            {
                entity.HasIndex(c => new { c.TenantId, c.FechaHoraCita })
                    .HasDatabaseName("IX_Citas_TenantId_FechaHoraCita");

                entity.HasIndex(c => new { c.TenantId, c.FuncionarioId, c.FechaHoraCita })
                    .HasDatabaseName("IX_Citas_TenantId_FuncionarioId_FechaHoraCita");
            });

            modelBuilder.Entity<Puesto>(entity =>
            {
                entity.HasIndex(p => new { p.TenantId, p.NombrePuesto })
                    .IsUnique()
                    .HasDatabaseName("IX_Puestos_TenantId_NombrePuesto");
            });

            modelBuilder.Entity<ClientesModel>(entity =>
            {
                entity.Property(c => c.NumeroTelefono)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(c => c.CorreoElectronico)
                    .HasMaxLength(256)
                    .IsRequired(false);

                entity.Property(c => c.Nombre)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.HasIndex(c => new { c.TenantId, c.NumeroTelefono })
                    .IsUnique();

                entity.HasIndex(c => new { c.TenantId, c.Nombre })
                    .HasDatabaseName("IX_Clientes_TenantId_Nombre");

                entity.HasMany(c => c.Visitas)
                    .WithOne(v => v.Cliente)
                    .HasForeignKey(v => v.ClienteId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ClienteVisitas>(entity =>
            {
                entity.Property(v => v.NumeroTelefono)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.HasIndex(v => new { v.TenantId, v.ClienteId, v.FechaVisita })
                    .IsDescending(false, false, true)
                    .HasDatabaseName("IX_ClienteVisitas_TenantId_ClienteId_FechaVisita");
            });

            modelBuilder.Entity<Servicio>(entity =>
            {
                entity.Property(s => s.Nombre)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(s => s.Precio).HasColumnType("decimal(18,2)");

                entity.HasIndex(s => new { s.TenantId, s.Nombre })
                    .IsUnique()
                    .HasDatabaseName("IX_Servicios_TenantId_Nombre");
            });

            modelBuilder.Entity<Cobro>(entity =>
            {
                entity.Property(c => c.Monto).HasColumnType("decimal(18,2)");

                entity.HasIndex(c => new { c.TenantId, c.FechaCobro })
                    .HasDatabaseName("IX_Cobros_TenantId_FechaCobro");

                entity.HasIndex(c => new { c.TenantId, c.FuncionarioId, c.FechaCobro })
                    .HasDatabaseName("IX_Cobros_TenantId_FuncionarioId_FechaCobro");
            });

            modelBuilder.Entity<Egreso>(entity =>
            {
                entity.Property(e => e.Monto).HasColumnType("decimal(18,2)");

                entity.HasIndex(e => new { e.TenantId, e.FechaEgreso })
                    .HasDatabaseName("IX_Egresos_TenantId_FechaEgreso");

                entity.HasIndex(e => new { e.TenantId, e.CategoriaId, e.FechaEgreso })
                    .HasDatabaseName("IX_Egresos_TenantId_CategoriaId_FechaEgreso");

                entity.HasOne(e => e.Categoria)
                    .WithMany()
                    .HasForeignKey(e => e.CategoriaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Categoria>(entity =>
            {
                entity.Property(c => c.Nombre)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(c => c.Detalle)
                    .HasMaxLength(500);

                entity.HasIndex(c => new { c.TenantId, c.Nombre })
                    .IsUnique()
                    .HasDatabaseName("IX_Categorias_TenantId_Nombre");
            });

            modelBuilder.Entity<Producto>(entity =>
            {
                entity.Property(p => p.NombreProducto)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(p => p.DetalleProducto)
                    .HasMaxLength(300);

                entity.Property(p => p.PrecioProducto)
                    .HasColumnType("decimal(18,2)");

                entity.HasIndex(p => new { p.TenantId, p.NombreProducto })
                    .IsUnique()
                    .HasDatabaseName("IX_Productos_TenantId_NombreProducto");

                entity.HasIndex(p => new { p.TenantId, p.Activo, p.NombreProducto })
                    .HasDatabaseName("IX_Productos_TenantId_Activo_NombreProducto");
            });

            modelBuilder.Entity<MovimientoInventario>(entity =>
            {
                entity.HasIndex(m => new { m.TenantId, m.ProductoId, m.FechaMovimiento })
                    .HasDatabaseName("IX_MovimientosInventario_TenantId_ProductoId_FechaMovimiento");

                entity.HasOne(m => m.Producto)
                    .WithMany()
                    .HasForeignKey(m => m.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<DetalleCobroProducto>(entity =>
            {
                entity.HasIndex(d => new { d.TenantId, d.CobroId })
                    .HasDatabaseName("IX_DetalleCobroProductos_TenantId_CobroId");
            });

            modelBuilder.Entity<PagoFuncionario>(entity =>
            {
                entity.Property(p => p.MontoPagado).HasColumnType("decimal(18,2)");

                entity.HasIndex(p => new { p.TenantId, p.InicioSemana, p.FinSemana, p.FuncionarioId })
                    .HasDatabaseName("IX_PagosFuncionarios_TenantId_Semana_Funcionario");
            });

            modelBuilder.Entity<LiquidacionSemanal>(entity =>
            {
                entity.Property(l => l.MontoTotal).HasColumnType("decimal(18,2)");
                entity.Property(l => l.Estado).HasMaxLength(30).IsRequired();
                entity.Property(l => l.Observacion).HasMaxLength(500);
                entity.Property(l => l.CreadoPor).HasMaxLength(450);

                entity.HasIndex(l => new { l.TenantId, l.SemanaInicio, l.SemanaFin, l.FechaPago })
                    .HasDatabaseName("IX_LiquidacionesSemanales_TenantId_Semana");

                entity.HasIndex(l => l.EgresoId)
                    .IsUnique();

                entity.HasOne(l => l.Egreso)
                    .WithMany()
                    .HasForeignKey(l => l.EgresoId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<LiquidacionSemanalDetalle>(entity =>
            {
                entity.Property(d => d.MontoServicios).HasColumnType("decimal(18,2)");
                entity.Property(d => d.MontoProductos).HasColumnType("decimal(18,2)");
                entity.Property(d => d.Impuestos).HasColumnType("decimal(18,2)");
                entity.Property(d => d.MontoNeto).HasColumnType("decimal(18,2)");
                entity.Property(d => d.MontoPagado).HasColumnType("decimal(18,2)");
                entity.Property(d => d.Pendiente).HasColumnType("decimal(18,2)");

                entity.HasIndex(d => new { d.TenantId, d.LiquidacionSemanalId, d.FuncionarioId })
                    .IsUnique();

                entity.HasOne(d => d.LiquidacionSemanal)
                    .WithMany(l => l.Detalles)
                    .HasForeignKey(d => d.LiquidacionSemanalId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Funcionario)
                    .WithMany()
                    .HasForeignKey(d => d.FuncionarioId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<LiquidacionSemanalDistribucionMensual>(entity =>
            {
                entity.Property(d => d.MontoAsignado).HasColumnType("decimal(18,2)");

                entity.HasIndex(d => new { d.TenantId, d.Anio, d.Mes })
                    .HasDatabaseName("IX_LiquidacionesSemanalesDistribucionMensual_TenantId_Anio_Mes");

                entity.HasIndex(d => new { d.TenantId, d.LiquidacionSemanalId, d.Anio, d.Mes })
                    .IsUnique();

                entity.HasOne(d => d.LiquidacionSemanal)
                    .WithMany(l => l.DistribucionesMensuales)
                    .HasForeignKey(d => d.LiquidacionSemanalId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.Property(t => t.Nombre).IsRequired();
                entity.Property(t => t.CommercialNotes).HasMaxLength(250);
                entity.Property(t => t.CommercialUpdatedByUserId).HasMaxLength(450);

                entity.HasOne(t => t.ForcedPlan)
                    .WithMany()
                    .HasForeignKey(t => t.ForcedPlanId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Suscripcion>(entity =>
            {
                entity.HasIndex(s => s.TenantId).IsUnique();
                entity.Property(s => s.MotivoEstado).HasMaxLength(250);
            });

            modelBuilder.Entity<TenantCommercialAccessGrant>(entity =>
            {
                entity.HasIndex(g => new { g.TenantId, g.Activo, g.FechaInicioUtc, g.FechaFinUtc });
                entity.Property(g => g.NotasInternas).HasMaxLength(2000);
                entity.Property(g => g.CreadoPorUserId).HasMaxLength(450);

                entity.HasOne(g => g.Tenant)
                    .WithMany(t => t.CommercialAccessGrants)
                    .HasForeignKey(g => g.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(g => g.Plan)
                    .WithMany()
                    .HasForeignKey(g => g.PlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(g => g.PromotionalCode)
                    .WithMany(c => c.AccessGrants)
                    .HasForeignKey(g => g.PromotionalCodeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PromotionalCode>(entity =>
            {
                entity.HasIndex(c => c.Codigo).IsUnique();
                entity.Property(c => c.Codigo).HasMaxLength(100).IsRequired();
                entity.Property(c => c.EmailObjetivo).HasMaxLength(256);
                entity.Property(c => c.CreadoPorUserId).HasMaxLength(450);
                entity.Property(c => c.NotasInternas).HasMaxLength(2000);

                entity.HasOne(c => c.Plan)
                    .WithMany()
                    .HasForeignKey(c => c.PlanId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PromotionalCodeRedemption>(entity =>
            {
                entity.HasIndex(r => new { r.PromotionalCodeId, r.TenantId }).IsUnique();
                entity.Property(r => r.EmailConsumidor).HasMaxLength(256).IsRequired();
                entity.Property(r => r.ConsumidoPorUserId).HasMaxLength(450);

                entity.HasOne(r => r.PromotionalCode)
                    .WithMany(c => c.Redemptions)
                    .HasForeignKey(r => r.PromotionalCodeId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.AccessGrant)
                    .WithMany(g => g.Redemptions)
                    .HasForeignKey(r => r.TenantCommercialAccessGrantId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<PagoSuscripcion>(entity =>
            {
                entity.HasIndex(p => new { p.Proveedor, p.ReferenciaInterna }).IsUnique();
                entity.HasIndex(p => new { p.Proveedor, p.ProviderReference }).IsUnique().HasFilter("[ProviderReference] IS NOT NULL");
                entity.HasIndex(p => new { p.Proveedor, p.ProviderTransactionId }).IsUnique().HasFilter("[ProviderTransactionId] IS NOT NULL");
                entity.HasIndex(p => new { p.Proveedor, p.ProviderCheckoutId }).IsUnique().HasFilter("[ProviderCheckoutId] IS NOT NULL");
                entity.Property(p => p.Monto).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<Factura>(entity =>
            {
                entity.HasIndex(f => new { f.Proveedor, f.ProviderTransactionId }).IsUnique().HasFilter("[ProviderTransactionId] IS NOT NULL");
                entity.HasIndex(f => new { f.Proveedor, f.ProviderReference, f.Estado }).HasFilter("[ProviderReference] IS NOT NULL");
                entity.Property(f => f.Monto).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<EventoPago>(entity =>
            {
                entity.HasIndex(e => new { e.Proveedor, e.ProveedorEventId }).IsUnique();
                entity.HasIndex(e => new { e.Proveedor, e.ReferenciaExterna });
            });

            modelBuilder.Entity<ContractDocument>(entity =>
            {
                entity.Property(document => document.Title).HasMaxLength(200).IsRequired();
                entity.Property(document => document.VersionNumber).HasMaxLength(50).IsRequired();
                entity.Property(document => document.ContentHash).HasMaxLength(64).IsRequired();

                entity.HasIndex(document => document.VersionNumber).IsUnique();
                entity.HasIndex(document => document.IsActive)
                    .IsUnique()
                    .HasFilter("IsActive = 1");

                entity.HasData(ContractDocumentSeedData.CreateInitialDocument());
            });

            modelBuilder.Entity<ContractAcceptanceRecord>(entity =>
            {
                entity.Property(record => record.UserId).HasMaxLength(450).IsRequired();
                entity.Property(record => record.ContractVersion).HasMaxLength(50).IsRequired();
                entity.Property(record => record.AcceptedContentHash).HasMaxLength(64).IsRequired();
                entity.Property(record => record.AcceptanceSource).HasMaxLength(40).IsRequired();
                entity.Property(record => record.IpAddress).HasMaxLength(64);
                entity.Property(record => record.UserAgent).HasMaxLength(2048);

                entity.HasIndex(record => new { record.UserId, record.ContractDocumentId, record.AcceptedAtUtc });

                entity.HasOne(record => record.ContractDocument)
                    .WithMany(document => document.Acceptances)
                    .HasForeignKey(record => record.ContractDocumentId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(record => record.User)
                    .WithMany()
                    .HasForeignKey(record => record.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<HistorialSuscripcion>()
                .HasQueryFilter(historial =>
                    CurrentTenantId == Guid.Empty ||
                    (historial.Suscripcion != null && historial.Suscripcion.TenantId == CurrentTenantId));

            // 🔹 MULTITENANT FILTER
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .HasIndex("TenantId");

                    modelBuilder.Entity(entityType.ClrType)
                        .HasQueryFilter(CreateTenantFilter(entityType.ClrType));
                }
            }
        }

        // 🔥 SAVE CHANGES BLINDADO
        public override int SaveChanges()
        {
            ApplyTenantGuards();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplyTenantGuards();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            await ApplyTenantGuardsAsync(cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplyTenantGuards()
        {
            var hasTenant = _tenantProvider.HasTenant();
            var tenantId = hasTenant ? _tenantProvider.GetTenantId() : Guid.Empty;
            Guid? systemTenantId = null;

            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                var entity = entry.Entity;
                if (entry.State == EntityState.Unchanged || entry.State == EntityState.Detached)
                    continue;

                if (entry.State == EntityState.Added)
                {
                    if (hasTenant)
                    {
                        entity.TenantId = tenantId;
                    }
                    else
                    {
                        EnsureSystemTenant(entity.TenantId, ref systemTenantId);
                    }

                    ValidateTenantRelationships(entry);
                }
                else if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                {
                    entry.Property(nameof(ITenantEntity.TenantId)).IsModified = false;
                    var persistedTenantId = ResolvePersistedTenantId(entry);

                    if (hasTenant)
                    {
                        if (persistedTenantId != tenantId)
                        {
                            _logger.LogWarning(
                                "Intento de modificación cross-tenant bloqueado. EntityType {EntityType}. TenantId {TenantId}. PersistedTenantId {PersistedTenantId}.",
                                entry.Metadata.ClrType.Name,
                                tenantId,
                                persistedTenantId);
                            throw new Exception("Intento de modificar datos de otro tenant");
                        }
                    }
                    else
                    {
                        EnsureSystemTenant(persistedTenantId, ref systemTenantId);
                    }

                    if (entry.State == EntityState.Modified)
                    {
                        ValidateTenantRelationships(entry);
                    }
                }
            }
        }

        private async Task ApplyTenantGuardsAsync(CancellationToken cancellationToken)
        {
            var hasTenant = _tenantProvider.HasTenant();
            var tenantId = hasTenant ? _tenantProvider.GetTenantId() : Guid.Empty;
            Guid? systemTenantId = null;

            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                var entity = entry.Entity;
                if (entry.State == EntityState.Unchanged || entry.State == EntityState.Detached)
                    continue;

                // 🔹 INSERT
                if (entry.State == EntityState.Added)
                {
                    if (hasTenant)
                    {
                        entity.TenantId = tenantId;
                    }
                    else
                    {
                        EnsureSystemTenant(entity.TenantId, ref systemTenantId);
                    }

                    await ValidateTenantRelationshipsAsync(entry, cancellationToken);
                }

                else if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                {
                    entry.Property(nameof(ITenantEntity.TenantId)).IsModified = false;
                    var persistedTenantId = await ResolvePersistedTenantIdAsync(entry, cancellationToken);

                    if (hasTenant)
                    {
                        if (persistedTenantId != tenantId)
                        {
                            _logger.LogWarning(
                                "Intento de modificación cross-tenant bloqueado en async. EntityType {EntityType}. TenantId {TenantId}. PersistedTenantId {PersistedTenantId}.",
                                entry.Metadata.ClrType.Name,
                                tenantId,
                                persistedTenantId);
                            throw new Exception("Intento de modificar datos de otro tenant");
                        }
                    }
                    else
                    {
                        EnsureSystemTenant(persistedTenantId, ref systemTenantId);
                    }

                    if (entry.State == EntityState.Modified)
                    {
                        await ValidateTenantRelationshipsAsync(entry, cancellationToken);
                    }
                }
            }
        }

        private static Guid ResolvePersistedTenantId(EntityEntry<ITenantEntity> entry)
        {
            var databaseValues = entry.GetDatabaseValues();
            if (databaseValues is null)
                throw new DbUpdateConcurrencyException("No se pudo resolver el tenant persistido para la entidad.");

            return (Guid)(databaseValues[nameof(ITenantEntity.TenantId)]
                ?? throw new Exception("La entidad persistida no tiene TenantId."));
        }

        private void ValidateTenantRelationships(EntityEntry<ITenantEntity> entry)
        {
            foreach (var foreignKey in entry.Metadata.GetForeignKeys())
            {
                if (!RequiresTenantRelationshipValidation(foreignKey))
                {
                    continue;
                }

                if (!TryGetForeignKeyValues(entry, foreignKey, out var keyValues))
                {
                    continue;
                }

                var principalTenantId = ResolvePrincipalTenantId(entry, foreignKey, keyValues);
                EnsurePrincipalTenant(entry, foreignKey, principalTenantId);
            }
        }

        private async Task ValidateTenantRelationshipsAsync(
            EntityEntry<ITenantEntity> entry,
            CancellationToken cancellationToken)
        {
            foreach (var foreignKey in entry.Metadata.GetForeignKeys())
            {
                if (!RequiresTenantRelationshipValidation(foreignKey))
                {
                    continue;
                }

                if (!TryGetForeignKeyValues(entry, foreignKey, out var keyValues))
                {
                    continue;
                }

                var principalTenantId = await ResolvePrincipalTenantIdAsync(entry, foreignKey, keyValues, cancellationToken);
                EnsurePrincipalTenant(entry, foreignKey, principalTenantId);
            }
        }

        private static bool TryGetForeignKeyValues(
            EntityEntry<ITenantEntity> entry,
            IForeignKey foreignKey,
            out object?[] keyValues)
        {
            keyValues = new object?[foreignKey.Properties.Count];

            for (var index = 0; index < foreignKey.Properties.Count; index++)
            {
                var dependentProperty = foreignKey.Properties[index];
                var value = entry.Property(dependentProperty.Name).CurrentValue;

                if (value is null)
                {
                    return false;
                }

                keyValues[index] = value;
            }

            return true;
        }

        private static bool RequiresTenantRelationshipValidation(IForeignKey foreignKey) =>
            IsTenantScoped(foreignKey.DeclaringEntityType.ClrType) &&
            IsTenantScoped(foreignKey.PrincipalEntityType.ClrType);

        private static bool IsTenantScoped(Type clrType) =>
            typeof(ITenantEntity).IsAssignableFrom(clrType);

        private Guid? ResolvePrincipalTenantId(
            EntityEntry<ITenantEntity> entry,
            IForeignKey foreignKey,
            object?[] keyValues)
        {
            var trackedPrincipalTenantId = ResolveTrackedPrincipalTenantId(foreignKey, keyValues);
            if (trackedPrincipalTenantId.HasValue)
            {
                return trackedPrincipalTenantId.Value;
            }

            var principal = Find(foreignKey.PrincipalEntityType.ClrType, keyValues);
            if (principal is not ITenantEntity principalTenantEntity)
            {
                _logger.LogWarning(
                    "Relación inválida detectada. EntityType {EntityType}. PrincipalType {PrincipalType}. TenantId {TenantId}.",
                    entry.Metadata.ClrType.Name,
                    foreignKey.PrincipalEntityType.ClrType.Name,
                    entry.Entity.TenantId);
                return null;
            }

            return principalTenantEntity.TenantId;
        }

        private async Task<Guid?> ResolvePrincipalTenantIdAsync(
            EntityEntry<ITenantEntity> entry,
            IForeignKey foreignKey,
            object?[] keyValues,
            CancellationToken cancellationToken)
        {
            var trackedPrincipalTenantId = ResolveTrackedPrincipalTenantId(foreignKey, keyValues);
            if (trackedPrincipalTenantId.HasValue)
            {
                return trackedPrincipalTenantId.Value;
            }

            var principal = await FindAsync(foreignKey.PrincipalEntityType.ClrType, keyValues, cancellationToken);
            if (principal is not ITenantEntity principalTenantEntity)
            {
                _logger.LogWarning(
                    "Relación inválida detectada en async. EntityType {EntityType}. PrincipalType {PrincipalType}. TenantId {TenantId}.",
                    entry.Metadata.ClrType.Name,
                    foreignKey.PrincipalEntityType.ClrType.Name,
                    entry.Entity.TenantId);
                return null;
            }

            return principalTenantEntity.TenantId;
        }

        private Guid? ResolveTrackedPrincipalTenantId(IForeignKey foreignKey, object?[] keyValues)
        {
            foreach (var trackedEntry in ChangeTracker.Entries())
            {
                if (trackedEntry.Metadata != foreignKey.PrincipalEntityType ||
                    trackedEntry.State == EntityState.Detached ||
                    trackedEntry.State == EntityState.Deleted ||
                    trackedEntry.Entity is not ITenantEntity trackedTenantEntity)
                {
                    continue;
                }

                var keyMatches = true;
                for (var index = 0; index < foreignKey.PrincipalKey.Properties.Count; index++)
                {
                    var principalKeyProperty = foreignKey.PrincipalKey.Properties[index];
                    var trackedValue = trackedEntry.Property(principalKeyProperty.Name).CurrentValue;

                    if (!Equals(trackedValue, keyValues[index]))
                    {
                        keyMatches = false;
                        break;
                    }
                }

                if (keyMatches)
                {
                    return trackedTenantEntity.TenantId;
                }
            }

            return null;
        }

        private void EnsurePrincipalTenant(
            EntityEntry<ITenantEntity> entry,
            IForeignKey foreignKey,
            Guid? principalTenantId)
        {
            if (!principalTenantId.HasValue)
            {
                throw new InvalidOperationException(
                    $"La relación hacia '{foreignKey.PrincipalEntityType.ClrType.Name}' no existe o no pertenece al tenant actual.");
            }

            if (principalTenantId.Value == entry.Entity.TenantId)
            {
                return;
            }

            _logger.LogWarning(
                "Relación cross-tenant bloqueada. EntityType {EntityType}. PrincipalType {PrincipalType}. TenantId {TenantId}. PrincipalTenantId {PrincipalTenantId}.",
                entry.Metadata.ClrType.Name,
                foreignKey.PrincipalEntityType.ClrType.Name,
                entry.Entity.TenantId,
                principalTenantId.Value);

            throw new InvalidOperationException(
                $"La relación hacia '{foreignKey.PrincipalEntityType.ClrType.Name}' pertenece a otro tenant.");
        }

        private static async Task<Guid> ResolvePersistedTenantIdAsync(
            EntityEntry<ITenantEntity> entry,
            CancellationToken cancellationToken)
        {
            var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
            if (databaseValues is null)
                throw new DbUpdateConcurrencyException("No se pudo resolver el tenant persistido para la entidad.");

            return (Guid)(databaseValues[nameof(ITenantEntity.TenantId)]
                ?? throw new Exception("La entidad persistida no tiene TenantId."));
        }

        private static void EnsureSystemTenant(Guid candidateTenantId, ref Guid? systemTenantId)
        {
            if (candidateTenantId == Guid.Empty)
                throw new Exception("Operación bloqueada: Tenant no resuelto");

            if (!systemTenantId.HasValue)
            {
                systemTenantId = candidateTenantId;
                return;
            }

            if (systemTenantId.Value != candidateTenantId)
                throw new Exception("Operación bloqueada: contexto de sistema intentando mezclar tenants");
        }


        //se agregan los modelos
        //Identity
        public DbSet<AppUsuario> AppUsuario { get; set; }
        //DataBase
        public DbSet<ClientesModel> Clientes { get; set; }
        public DbSet<ClienteVisitas> ClienteVisitas { get; set; }
        //Calendar
        public DbSet<Cita> Citas { get; set; }
        //Finanzas
        public DbSet<Cobro> Cobros { get; set; }
        public DbSet<Servicio> Servicios { get; set; }
        public DbSet<Egreso> Egresos { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        //Productos
        public DbSet<Producto> Productos { get; set; }
        public DbSet<DetalleCobroProducto> DetalleCobroProductos { get; set; }
        public DbSet<MovimientoInventario> MovimientosInventario { get; set; }
        //Funcionarios
        public DbSet<Funcionario> Funcionarios { get; set; }
        public DbSet<Puesto> Puestos { get; set; }
        public DbSet<PagoFuncionario> PagosFuncionarios { get; set; }
        public DbSet<LiquidacionSemanal> LiquidacionesSemanales { get; set; }
        public DbSet<LiquidacionSemanalDetalle> LiquidacionesSemanalesDetalle { get; set; }
        public DbSet<LiquidacionSemanalDistribucionMensual> LiquidacionesSemanalesDistribucionMensual { get; set; }
        //Saas 
        public DbSet<Plan> Planes { get; set; }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Suscripcion> Suscripciones { get; set; }
        public DbSet<HistorialSuscripcion> HistorialSuscripciones { get; set; }
        public DbSet<PlanFeature> PlanFeatures { get; set; }
        public DbSet<Feature> Features { get; set; }
        public DbSet<Factura> Facturas { get; set; }
        public DbSet<PagoSuscripcion> PagosSuscripcion { get; set; }
        public DbSet<EventoPago> EventosPago { get; set; }
        public DbSet<TenantCommercialAccessGrant> TenantCommercialAccessGrants { get; set; }
        public DbSet<PromotionalCode> PromotionalCodes { get; set; }
        public DbSet<PromotionalCodeRedemption> PromotionalCodeRedemptions { get; set; }
        public DbSet<ContractDocument> ContractDocuments { get; set; }
        public DbSet<ContractAcceptanceRecord> ContractAcceptanceRecords { get; set; }





    }



}
