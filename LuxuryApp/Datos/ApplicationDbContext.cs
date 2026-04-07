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
using Microsoft.EntityFrameworkCore;

namespace ProyectoIdentity.Datos
{
    public class ApplicationDbContext : IdentityDbContext<AppUsuario>
    {
        private readonly ITenantProvider _tenantProvider;

        public ApplicationDbContext(
            DbContextOptions options,
            ITenantProvider tenantProvider) : base(options)
        {
            _tenantProvider = tenantProvider
                ?? throw new Exception("TenantProvider no disponible");
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
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var hasTenant = _tenantProvider.HasTenant();
            var tenantId = hasTenant ? _tenantProvider.GetTenantId() : Guid.Empty;

            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                var entity = entry.Entity;

                // 🚨 BLOQUEO TOTAL si intenta modificar sin tenant
                if (!hasTenant)
                    throw new Exception("Operación bloqueada: Tenant no resuelto");

                // 🔹 INSERT
                if (entry.State == EntityState.Added)
                {
                    entity.TenantId = tenantId;
                }

                // 🔹 UPDATE
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(ITenantEntity.TenantId)).IsModified = false;

                    var originalTenantId = (Guid)entry.OriginalValues[nameof(ITenantEntity.TenantId)];

                    if (originalTenantId != tenantId)
                        throw new Exception("Intento de modificar datos de otro tenant");
                }

                // 🔹 DELETE
                else if (entry.State == EntityState.Deleted)
                {
                    var originalTenantId = (Guid)entry.OriginalValues[nameof(ITenantEntity.TenantId)];

                    if (originalTenantId != tenantId)
                        throw new Exception("Intento de eliminar datos de otro tenant");
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
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
        public DbSet<StripeEvento> StripeEventos { get; set; }





    }



}
