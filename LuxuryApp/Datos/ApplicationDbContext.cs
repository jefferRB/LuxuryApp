using System.Linq.Expressions;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Common;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Identity;
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

                entity.HasMany(c => c.Visitas)
                    .WithOne(v => v.Cliente)
                    .HasForeignKey(v => v.ClienteId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(c => c.Imagenes)
                    .WithOne(i => i.Cliente)
                    .HasForeignKey(i => i.ClienteId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ClienteVisitas>(entity =>
            {
                entity.Property(v => v.NumeroTelefono)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.HasIndex(v => new { v.TenantId, v.ClienteId, v.FechaVisita });
            });

            modelBuilder.Entity<ClienteImagenesModel>(entity =>
            {
                entity.Property(i => i.NumeroTelefono)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.HasIndex(i => new { i.TenantId, i.ClienteId });
            });

            modelBuilder.Entity<Servicio>(entity =>
            {
                entity.Property(s => s.Precio).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<Cobro>(entity =>
            {
                entity.Property(c => c.Monto).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<Egreso>(entity =>
            {
                entity.Property(e => e.Monto).HasColumnType("decimal(18,2)");

                entity.HasOne(e => e.Categoria)
                    .WithMany()
                    .HasForeignKey(e => e.CategoriaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Producto>(entity =>
            {
                entity.Property(p => p.PrecioProducto).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<PagoFuncionario>(entity =>
            {
                entity.Property(p => p.MontoPagado).HasColumnType("decimal(18,2)");
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
        public DbSet<ClienteImagenesModel> ClienteImagenes { get; set; }
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





    }



}
