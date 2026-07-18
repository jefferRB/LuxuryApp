using System.Linq.Expressions;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Common;
using LuxuryApp.Models.Comprobantes;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Fiscal;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Legal;
using LuxuryApp.Models.Notifications;
using LuxuryApp.Models.Platform;
using LuxuryApp.Models.Productos;
using LuxuryApp.Models.PublicPages;
using LuxuryApp.Models.Reports;
using LuxuryApp.Models.Reservas;
using LuxuryApp.Models.Saas;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
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
                entity.Property(p => p.Codigo).HasMaxLength(50);
                entity.Property(p => p.Nombre).IsRequired();
                entity.Property(p => p.Moneda).HasMaxLength(100);
                entity.Property(p => p.EsPlanValidacion).HasDefaultValue(false);
                entity.Property(p => p.PrecioMensual).HasColumnType("decimal(18,2)");
                entity.Property(p => p.MonthlyEquivalentAmount).HasColumnType("decimal(18,2)");
                entity.HasIndex(p => p.Codigo)
                    .IsUnique()
                    .HasFilter("[Codigo] IS NOT NULL");
            });

            modelBuilder.Entity<PlanChangeIntent>(entity =>
            {
                entity.Property(i => i.ToPlanCode).HasMaxLength(50);
                entity.Property(i => i.FromPlanCode).HasMaxLength(50);
                entity.Property(i => i.FromProviderSubscriptionId).HasMaxLength(100);
                entity.Property(i => i.NewProviderSubscriptionId).HasMaxLength(100);
                entity.Property(i => i.Notes).HasMaxLength(300);

                // Anti doble-cambio: a lo sumo un intento Pending (Estado = 0) por tenant.
                entity.HasIndex(i => i.TenantId)
                    .IsUnique()
                    .HasFilter("[Estado] = 0")
                    .HasDatabaseName("IX_PlanChangeIntents_TenantId_OpenPending");

                entity.HasIndex(i => new { i.TenantId, i.Estado });
            });

            modelBuilder.Entity<Funcionario>(entity =>
            {
                entity.HasOne(f => f.Puesto)
                    .WithMany(p => p.Funcionarios)
                    .HasForeignKey(f => f.IdPuesto)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(f => f.PorcentajeGanancia).HasColumnType("decimal(18,2)");
                entity.Property(f => f.PorcentajeProducto).HasColumnType("decimal(18,2)");

                // Default true: los funcionarios existentes conservan el cálculo con rebaja de impuestos.
                entity.Property(f => f.RebajarImpuestosAntesDeComision).HasDefaultValue(true);

                // Foto opcional: por defecto se permite mostrarla en reservas (sin foto no afecta a nadie).
                entity.Property(f => f.MostrarFotoEnReservas).HasDefaultValue(true);
                entity.Property(f => f.FotoUrl).HasMaxLength(400);
                entity.Property(f => f.FotoStoragePath).HasMaxLength(400);

                // Configuración fiscal del colaborador.
                // OJO: ComisionCalculadaSobre NO lleva default de BD. Su default CLR (TotalCobrado=0)
                // difiere del valor de negocio deseado (BaseSinIva), así que un default de columna
                // provocaría que EF ignore un "TotalCobrado" elegido explícitamente (sentinel). El
                // valor inicial de filas existentes se resuelve en la migración con un backfill a
                // partir de RebajarImpuestosAntesDeComision; las inserciones nuevas envían el valor
                // real del objeto (inicializado a BaseSinIva en el modelo).
                entity.Property(f => f.TipoRelacionColaborador).HasDefaultValue(TipoRelacionColaborador.Empleado);
                entity.Property(f => f.ColaboradorFacturaIva).HasDefaultValue(false);
                // Default de columna NoFactura(0) == default CLR → sin problema de sentinel. Las filas
                // existentes con ColaboradorFacturaIva=1 se backfillean a IvaIncluido en la migración.
                entity.Property(f => f.ModalidadIvaColaborador).HasDefaultValue(ModalidadIvaColaborador.NoFactura);
                entity.Property(f => f.TarifaIvaFacturaColaborador)
                    .HasColumnType("decimal(18,2)")
                    .HasDefaultValue(FiscalDefaults.TarifaIvaPorDefecto);
                entity.Property(f => f.RequiereFacturaAntesDePagar).HasDefaultValue(false);

                entity.HasIndex(f => new { f.TenantId, f.Nombre })
                    .HasDatabaseName("IX_Funcionarios_TenantId_Nombre");

                // Acceso al portal: 1 cuenta como máximo por funcionario.
                // Índice único filtrado para que un mismo usuario no se vincule a
                // dos funcionarios. La integridad referencial al usuario se valida en
                // código (AppUsuario no es ITenantEntity, no aplica el guard de tenant).
                entity.HasIndex(f => f.AppUsuarioId)
                    .IsUnique()
                    .HasFilter("[AppUsuarioId] IS NOT NULL")
                    .HasDatabaseName("UX_Funcionarios_AppUsuarioId");

                entity.HasOne<AppUsuario>()
                    .WithMany()
                    .HasForeignKey(f => f.AppUsuarioId)
                    .HasPrincipalKey(u => u.Id)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<AppUsuario>(entity =>
            {
                // Conserva el índice histórico por TenantId para no regresar el desempeño
                // de las búsquedas de usuarios por tenant. FuncionarioId no necesita índice
                // propio: el vínculo se resuelve por Funcionario.AppUsuarioId.
                entity.HasIndex(u => u.TenantId);
            });

            modelBuilder.Entity<Cita>(entity =>
            {
                entity.Property(c => c.EstadoConfirmacionWhatsApp)
                    .HasMaxLength(30)
                    .HasDefaultValue(WhatsAppConfirmationStates.Pendiente);

                entity.Property(c => c.UltimoMetaMessageId)
                    .HasMaxLength(128);

                entity.Property(c => c.WhatsAppConsentAtCreation)
                    .HasDefaultValue(false);

                entity.Property(c => c.WhatsAppConsentSource)
                    .HasMaxLength(80);

                entity.HasOne(c => c.Cliente)
                    .WithMany(cliente => cliente.Citas)
                    .HasForeignKey(c => c.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(c => new { c.TenantId, c.FechaHoraCita })
                    .HasDatabaseName("IX_Citas_TenantId_FechaHoraCita");

                entity.HasIndex(c => new { c.TenantId, c.FuncionarioId, c.FechaHoraCita })
                    .HasDatabaseName("IX_Citas_TenantId_FuncionarioId_FechaHoraCita");

                entity.HasIndex(c => new { c.TenantId, c.ClienteId, c.FechaHoraCita })
                    .IsDescending(false, false, true)
                    .HasFilter("[ClienteId] IS NOT NULL")
                    .HasDatabaseName("IX_Citas_TenantId_ClienteId_FechaHoraCita");
            });

            modelBuilder.Entity<WhatsAppMessageLog>(entity =>
            {
                entity.Property(message => message.Direction).HasMaxLength(20).IsRequired();
                entity.Property(message => message.NotificationType).HasMaxLength(40).IsRequired();
                entity.Property(message => message.Provider).HasMaxLength(40).IsRequired();
                entity.Property(message => message.MetaMessageId).HasMaxLength(128);
                entity.Property(message => message.ContextMessageId).HasMaxLength(128);
                entity.Property(message => message.RecipientPhoneE164).HasMaxLength(32);
                entity.Property(message => message.SenderPhoneE164).HasMaxLength(32);
                entity.Property(message => message.WaId).HasMaxLength(64);
                entity.Property(message => message.TemplateName).HasMaxLength(128);
                entity.Property(message => message.Status).HasMaxLength(30).IsRequired();
                entity.Property(message => message.ErrorCode).HasMaxLength(80);
                entity.Property(message => message.ErrorMessage).HasMaxLength(1000);

                entity.HasIndex(message => new { message.TenantId, message.CitaId })
                    .HasDatabaseName("IX_WhatsAppMessageLogs_TenantId_CitaId");

                entity.HasIndex(message => new { message.TenantId, message.NotificationType, message.Status })
                    .HasDatabaseName("IX_WhatsAppMessageLogs_TenantId_NotificationType_Status");

                entity.HasIndex(message => new { message.TenantId, message.CitaId, message.NotificationType, message.Direction })
                    .IsUnique()
                    .HasFilter("[Direction] = 'Outbound' AND [CitaId] IS NOT NULL AND [Status] IN ('Pending', 'Processing', 'Sent')")
                    .HasDatabaseName("UX_WhatsAppMessageLogs_ActiveOutboundNotification");

                entity.HasIndex(message => message.MetaMessageId)
                    .IsUnique()
                    .HasFilter("[MetaMessageId] IS NOT NULL")
                    .HasDatabaseName("UX_WhatsAppMessageLogs_MetaMessageId");

                entity.HasIndex(message => message.ContextMessageId)
                    .HasDatabaseName("IX_WhatsAppMessageLogs_ContextMessageId");

                entity.HasIndex(message => new { message.TenantId, message.RecipientPhoneE164, message.CreatedAtUtc })
                    .HasDatabaseName("IX_WhatsAppMessageLogs_TenantId_RecipientPhone_CreatedAtUtc");

                entity.HasIndex(message => new { message.TenantId, message.CreatedAtUtc })
                    .HasDatabaseName("IX_WhatsAppMessageLogs_TenantId_CreatedAtUtc");

                entity.HasOne(message => message.Cita)
                    .WithMany()
                    .HasForeignKey(message => message.CitaId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<TenantWhatsAppSettings>(entity =>
            {
                entity.Property(settings => settings.IsEnabled).HasDefaultValue(false);
                entity.Property(settings => settings.SendConfirmationOnCreate).HasDefaultValue(true);
                entity.Property(settings => settings.SendReminderThreeHoursBefore).HasDefaultValue(true);
                entity.Property(settings => settings.DailyMessageLimit).HasDefaultValue(LuxuryApp.Models.WhatsApp.TenantWhatsAppSettings.DefaultDailyMessageLimit);
                entity.Property(settings => settings.TimeZoneId)
                    .HasMaxLength(100)
                    .HasDefaultValue(LuxuryApp.Models.WhatsApp.TenantWhatsAppSettings.DefaultTimeZoneId)
                    .IsRequired();

                // Programación de confirmaciones.
                entity.Property(settings => settings.ConfirmationScheduleMode)
                    .HasMaxLength(40)
                    .HasDefaultValue(LuxuryApp.Models.WhatsApp.WhatsAppConfirmationScheduleModes.RelativeBeforeAppointment)
                    .IsRequired();
                entity.Property(settings => settings.ConfirmationHoursBefore)
                    .HasDefaultValue(LuxuryApp.Models.WhatsApp.TenantWhatsAppSettings.DefaultConfirmationHoursBefore);
                entity.Property(settings => settings.ConfirmationBatchTarget)
                    .HasMaxLength(30)
                    .HasDefaultValue(LuxuryApp.Models.WhatsApp.WhatsAppConfirmationBatchTargets.TomorrowAllDay)
                    .IsRequired();
                entity.Property(settings => settings.SendConfirmationImmediatelyIfInsideWindow)
                    .HasDefaultValue(true);

                // Programación de recordatorios.
                entity.Property(settings => settings.ReminderScheduleMode)
                    .HasMaxLength(40)
                    .HasDefaultValue(LuxuryApp.Models.WhatsApp.WhatsAppReminderScheduleModes.RelativeBeforeAppointment)
                    .IsRequired();
                entity.Property(settings => settings.ReminderHoursBefore)
                    .HasDefaultValue(LuxuryApp.Models.WhatsApp.TenantWhatsAppSettings.DefaultReminderHoursBefore);
                entity.Property(settings => settings.ReminderBatchTarget)
                    .HasMaxLength(30)
                    .HasDefaultValue(LuxuryApp.Models.WhatsApp.WhatsAppReminderBatchTargets.SameDayRemaining)
                    .IsRequired();
                entity.Property(settings => settings.ReminderLookAheadHours)
                    .HasDefaultValue(LuxuryApp.Models.WhatsApp.TenantWhatsAppSettings.DefaultReminderHoursBefore);
                entity.Property(settings => settings.SendReminderImmediatelyIfInsideWindow)
                    .HasDefaultValue(true);

                entity.Property(settings => settings.QuietHoursEnabled).HasDefaultValue(false);

                entity.Property(settings => settings.Notes).HasMaxLength(2000);
                entity.Property(settings => settings.UpdatedByUserId).HasMaxLength(450);

                entity.HasIndex(settings => settings.TenantId)
                    .IsUnique()
                    .HasDatabaseName("UX_TenantWhatsAppSettings_TenantId");

                entity.HasIndex(settings => new { settings.TenantId, settings.IsEnabled })
                    .HasDatabaseName("IX_TenantWhatsAppSettings_TenantId_IsEnabled");

                entity.HasOne(settings => settings.Tenant)
                    .WithOne(tenant => tenant.WhatsAppSettings)
                    .HasForeignKey<TenantWhatsAppSettings>(settings => settings.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);
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

                entity.Property(c => c.AceptaMensajesWhatsApp)
                    .HasDefaultValue(false);

                entity.Property(c => c.WhatsAppConsentSource)
                    .HasMaxLength(80);

                entity.Property(c => c.WhatsAppConsentCapturedByUserId)
                    .HasMaxLength(450);

                entity.Property(c => c.WhatsAppConsentTextVersion)
                    .HasMaxLength(40);

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

                entity.Property(s => s.AplicaIva).HasDefaultValue(FiscalDefaults.AplicaIvaPorDefecto);
                entity.Property(s => s.TarifaIva).HasColumnType("decimal(18,2)");

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

                entity.HasIndex(c => new { c.TenantId, c.ClienteId })
                    .HasFilter("[ClienteId] IS NOT NULL")
                    .HasDatabaseName("IX_Cobros_TenantId_ClienteId");

                // Un cobro como máximo por cita: evita doble cobro de la misma cita.
                entity.HasIndex(c => new { c.TenantId, c.CitaId })
                    .IsUnique()
                    .HasFilter("[CitaId] IS NOT NULL")
                    .HasDatabaseName("UX_Cobros_TenantId_CitaId");

                entity.HasOne(c => c.Cliente)
                    .WithMany()
                    .HasForeignKey(c => c.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(c => c.Cita)
                    .WithMany()
                    .HasForeignKey(c => c.CitaId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ComprobanteCobro>(entity =>
            {
                entity.Property(c => c.NumeroInterno).HasMaxLength(40).IsRequired();
                entity.Property(c => c.TipoComprobante).HasMaxLength(40).IsRequired();
                entity.Property(c => c.TokenPublico).HasMaxLength(64).IsRequired();
                entity.Property(c => c.EmailDestino).HasMaxLength(256);
                entity.Property(c => c.EmailDestinoNormalizado).HasMaxLength(256);
                entity.Property(c => c.Moneda).HasMaxLength(3);
                entity.Property(c => c.MetodoPago).HasMaxLength(20);

                // Estado de envío como string legible en BD.
                entity.Property(c => c.EstadoEnvio)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                entity.Property(c => c.Subtotal).HasColumnType("decimal(18,2)");
                entity.Property(c => c.Descuento).HasColumnType("decimal(18,2)");
                entity.Property(c => c.Impuesto).HasColumnType("decimal(18,2)");
                entity.Property(c => c.Total).HasColumnType("decimal(18,2)");

                // Número interno único por tenant (red de seguridad de la numeración).
                entity.HasIndex(c => new { c.TenantId, c.NumeroInterno })
                    .IsUnique()
                    .HasDatabaseName("UX_ComprobantesCobro_TenantId_NumeroInterno");

                // Un comprobante "vivo" por cobro: evita duplicados por doble submit/retry.
                // Filtrado para no chocar con estados anulados (Cancelled) futuros.
                entity.HasIndex(c => new { c.TenantId, c.CobroId })
                    .IsUnique()
                    .HasFilter("[EstadoEnvio] <> 'Cancelled'")
                    .HasDatabaseName("UX_ComprobantesCobro_TenantId_CobroId");

                entity.HasIndex(c => new { c.TenantId, c.ClienteId })
                    .HasFilter("[ClienteId] IS NOT NULL")
                    .HasDatabaseName("IX_ComprobantesCobro_TenantId_ClienteId");

                entity.HasIndex(c => new { c.TenantId, c.EstadoEnvio })
                    .HasDatabaseName("IX_ComprobantesCobro_TenantId_EstadoEnvio");

                // Token único global: la ruta pública lo resuelve sin contexto de tenant.
                entity.HasIndex(c => c.TokenPublico)
                    .IsUnique()
                    .HasDatabaseName("UX_ComprobantesCobro_TokenPublico");

                // No borrar comprobantes históricos al borrar el cobro.
                entity.HasOne(c => c.Cobro)
                    .WithMany()
                    .HasForeignKey(c => c.CobroId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(c => c.Cliente)
                    .WithMany()
                    .HasForeignKey(c => c.ClienteId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(c => c.Funcionario)
                    .WithMany()
                    .HasForeignKey(c => c.FuncionarioId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(c => c.Lineas)
                    .WithOne(l => l.ComprobanteCobro!)
                    .HasForeignKey(l => l.ComprobanteCobroId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ComprobanteCobroLinea>(entity =>
            {
                entity.Property(l => l.Descripcion).HasMaxLength(250).IsRequired();
                entity.Property(l => l.TipoLinea).HasMaxLength(20).IsRequired();
                entity.Property(l => l.Cantidad).HasColumnType("decimal(18,2)");
                entity.Property(l => l.PrecioUnitario).HasColumnType("decimal(18,2)");
                entity.Property(l => l.Subtotal).HasColumnType("decimal(18,2)");
                entity.Property(l => l.Impuesto).HasColumnType("decimal(18,2)");
                entity.Property(l => l.Total).HasColumnType("decimal(18,2)");

                entity.HasIndex(l => new { l.TenantId, l.ComprobanteCobroId })
                    .HasDatabaseName("IX_ComprobanteCobroLineas_TenantId_ComprobanteCobroId");
            });

            modelBuilder.Entity<ComprobanteCobroSecuencia>(entity =>
            {
                entity.HasKey(s => s.TenantId);
                entity.Property(s => s.UltimoNumero).IsRequired();
            });

            modelBuilder.Entity<FuncionarioPortalPermiso>(entity =>
            {
                entity.Property(p => p.Permiso).HasMaxLength(60).IsRequired();

                entity.HasIndex(p => new { p.TenantId, p.FuncionarioId, p.Permiso })
                    .IsUnique()
                    .HasDatabaseName("UX_FuncionarioPortalPermisos_Tenant_Funcionario_Permiso");

                entity.HasOne(p => p.Funcionario)
                    .WithMany()
                    .HasForeignKey(p => p.FuncionarioId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ClienteServicioRealizado>(entity =>
            {
                entity.Property(r => r.Monto).HasColumnType("decimal(18,2)");
                entity.Property(r => r.Notas).HasMaxLength(500);
                entity.Property(r => r.Origen).HasMaxLength(30).IsRequired();

                entity.HasIndex(r => new { r.TenantId, r.ClienteId, r.FechaHora })
                    .IsDescending(false, false, true)
                    .HasDatabaseName("IX_ClienteServiciosRealizados_TenantId_ClienteId_FechaHora");

                entity.HasIndex(r => r.CobroId)
                    .IsUnique()
                    .HasFilter("[CobroId] IS NOT NULL")
                    .HasDatabaseName("UX_ClienteServiciosRealizados_CobroId");

                entity.HasOne(r => r.Cliente)
                    .WithMany(c => c.ServiciosRealizados)
                    .HasForeignKey(r => r.ClienteId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.Funcionario)
                    .WithMany()
                    .HasForeignKey(r => r.FuncionarioId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Servicio)
                    .WithMany()
                    .HasForeignKey(r => r.ServicioId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Cobro)
                    .WithMany()
                    .HasForeignKey(r => r.CobroId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(r => r.Cita)
                    .WithMany()
                    .HasForeignKey(r => r.CitaId)
                    .OnDelete(DeleteBehavior.SetNull);
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

                entity.Property(p => p.AplicaIva).HasDefaultValue(FiscalDefaults.AplicaIvaPorDefecto);
                entity.Property(p => p.TarifaIva).HasColumnType("decimal(18,2)");

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

            modelBuilder.Entity<TenantBookingSettings>(entity =>
            {
                entity.Property(s => s.PublicBookingSlug).HasMaxLength(80);
                entity.Property(s => s.PublicBookingMode).HasMaxLength(40).IsRequired();
                entity.Property(s => s.PublicBookingWelcomeMessage).HasMaxLength(500);
                entity.Property(s => s.PublicBookingConfirmationMessage).HasMaxLength(500);
                entity.Property(s => s.UpdatedByUserId).HasMaxLength(450);

                // Por defecto se muestran fotos (los tenants existentes conservan el comportamiento
                // esperado; sin foto no afecta a nadie).
                entity.Property(s => s.PublicBookingShowEmployeePhotos).HasDefaultValue(true);

                entity.HasIndex(s => s.TenantId)
                    .IsUnique()
                    .HasDatabaseName("UX_TenantBookingSettings_TenantId");

                // Slug único entre tenants (solo cuando está definido).
                entity.HasIndex(s => s.PublicBookingSlug)
                    .IsUnique()
                    .HasFilter("[PublicBookingSlug] IS NOT NULL")
                    .HasDatabaseName("UX_TenantBookingSettings_Slug");

                entity.HasOne(s => s.Tenant)
                    .WithOne()
                    .HasForeignKey<TenantBookingSettings>(s => s.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TenantPublicPage>(entity =>
            {
                entity.Property(page => page.HeroTitle).HasMaxLength(120);
                entity.Property(page => page.HeroSubtitle).HasMaxLength(180);
                entity.Property(page => page.HeroEyebrow).HasMaxLength(80);
                entity.Property(page => page.Description).HasMaxLength(1500);
                entity.Property(page => page.LogoUrl).HasMaxLength(400);
                entity.Property(page => page.CoverImageUrl).HasMaxLength(400);
                entity.Property(page => page.Phone).HasMaxLength(30);
                entity.Property(page => page.WhatsAppPhone).HasMaxLength(30);
                entity.Property(page => page.Email).HasMaxLength(256);
                entity.Property(page => page.Address).HasMaxLength(300);
                entity.Property(page => page.GoogleMapsUrl).HasMaxLength(500);
                entity.Property(page => page.WazeUrl).HasMaxLength(500);
                entity.Property(page => page.InstagramUrl).HasMaxLength(300);
                entity.Property(page => page.FacebookUrl).HasMaxLength(300);
                entity.Property(page => page.TikTokUrl).HasMaxLength(300);
                entity.Property(page => page.SeoTitle).HasMaxLength(70);
                entity.Property(page => page.SeoDescription).HasMaxLength(180);
                entity.Property(page => page.BusinessHours).HasMaxLength(500);

                entity.Property(page => page.IsPublished).HasDefaultValue(false);
                entity.Property(page => page.ShowServices).HasDefaultValue(true);
                entity.Property(page => page.ShowPrices).HasDefaultValue(true);
                entity.Property(page => page.ShowTeam).HasDefaultValue(false);
                entity.Property(page => page.ShowLocation).HasDefaultValue(true);
                entity.Property(page => page.ShowWhatsAppButton).HasDefaultValue(true);

                entity.HasIndex(page => page.TenantId)
                    .IsUnique()
                    .HasDatabaseName("UX_TenantPublicPages_TenantId");

                entity.HasOne(page => page.Tenant)
                    .WithOne()
                    .HasForeignKey<TenantPublicPage>(page => page.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TenantPublicAsset>(entity =>
            {
                entity.Property(asset => asset.AssetType)
                    .HasConversion<int>();

                entity.Property(asset => asset.StorageKey)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(asset => asset.PublicUrl)
                    .HasMaxLength(800)
                    .IsRequired();

                entity.Property(asset => asset.ContentType)
                    .HasMaxLength(60)
                    .IsRequired();

                entity.Property(asset => asset.OriginalFileName)
                    .HasMaxLength(180);

                entity.Property(asset => asset.IsActive)
                    .HasDefaultValue(true);

                entity.HasIndex(asset => asset.TenantId)
                    .HasDatabaseName("IX_TenantPublicAssets_TenantId");

                entity.HasIndex(asset => new { asset.TenantId, asset.AssetType })
                    .HasDatabaseName("IX_TenantPublicAssets_TenantId_AssetType");

                entity.HasIndex(asset => new { asset.TenantId, asset.ServicioId, asset.AssetType })
                    .HasDatabaseName("IX_TenantPublicAssets_TenantId_ServicioId_AssetType");

                entity.HasIndex(asset => asset.TenantPublicPageId)
                    .HasDatabaseName("IX_TenantPublicAssets_TenantPublicPageId");

                entity.HasIndex(asset => asset.StorageKey)
                    .IsUnique()
                    .HasDatabaseName("UX_TenantPublicAssets_StorageKey");

                entity.HasOne(asset => asset.Tenant)
                    .WithMany()
                    .HasForeignKey(asset => asset.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(asset => asset.TenantPublicPage)
                    .WithMany()
                    .HasForeignKey(asset => asset.TenantPublicPageId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(asset => asset.Servicio)
                    .WithMany()
                    .HasForeignKey(asset => asset.ServicioId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TenantPublicPageDailyMetric>(entity =>
            {
                entity.Property(metric => metric.Date)
                    .HasColumnType("date");

                entity.Property(metric => metric.MetricType)
                    .HasConversion<int>();

                entity.Property(metric => metric.Slug)
                    .HasMaxLength(80)
                    .IsRequired();

                entity.Property(metric => metric.Count)
                    .HasDefaultValue(0L);

                entity.HasIndex(metric => new { metric.TenantId, metric.Date })
                    .HasDatabaseName("IX_TenantPublicPageDailyMetrics_TenantId_Date");

                entity.HasIndex(metric => new { metric.TenantId, metric.Date, metric.MetricType })
                    .HasDatabaseName("IX_TenantPublicPageDailyMetrics_TenantId_Date_MetricType");

                entity.HasIndex(metric => new { metric.TenantId, metric.Date, metric.MetricType, metric.ServicioId })
                    .HasDatabaseName("IX_TenantPublicPageDailyMetrics_TenantId_Date_MetricType_ServicioId");

                entity.HasOne(metric => metric.Tenant)
                    .WithMany()
                    .HasForeignKey(metric => metric.TenantId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(metric => metric.Servicio)
                    .WithMany()
                    .HasForeignKey(metric => metric.ServicioId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<BookingRequest>(entity =>
            {
                entity.Property(r => r.NombreCliente).HasMaxLength(100).IsRequired();
                entity.Property(r => r.TelefonoCliente).HasMaxLength(30).IsRequired();
                entity.Property(r => r.CorreoCliente).HasMaxLength(256);
                entity.Property(r => r.NotasCliente).HasMaxLength(500);
                entity.Property(r => r.Estado).HasMaxLength(30).IsRequired();
                entity.Property(r => r.Origen).HasMaxLength(40).IsRequired();
                entity.Property(r => r.ConfirmedByUserId).HasMaxLength(450);
                entity.Property(r => r.RejectedByUserId).HasMaxLength(450);
                entity.Property(r => r.RejectedReason).HasMaxLength(300);
                entity.Property(r => r.IpHash).HasMaxLength(64);
                entity.Property(r => r.PublicSubmissionToken).HasMaxLength(64);
                entity.Property(r => r.UserAgent).HasMaxLength(400);

                entity.HasIndex(r => new { r.TenantId, r.Estado, r.FechaHoraInicioSolicitada })
                    .HasDatabaseName("IX_BookingRequests_TenantId_Estado_Fecha");

                // Idempotencia de envíos públicos: un token no puede repetirse por tenant.
                entity.HasIndex(r => new { r.TenantId, r.PublicSubmissionToken })
                    .IsUnique()
                    .HasFilter("[PublicSubmissionToken] IS NOT NULL")
                    .HasDatabaseName("UX_BookingRequests_TenantId_SubmissionToken");

                entity.HasIndex(r => new { r.TenantId, r.TelefonoCliente, r.Estado })
                    .HasDatabaseName("IX_BookingRequests_TenantId_Telefono_Estado");

                entity.HasIndex(r => r.ConvertedCitaId)
                    .HasFilter("[ConvertedCitaId] IS NOT NULL")
                    .HasDatabaseName("IX_BookingRequests_ConvertedCitaId");

                entity.HasOne(r => r.Servicio)
                    .WithMany()
                    .HasForeignKey(r => r.ServicioId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Funcionario)
                    .WithMany()
                    .HasForeignKey(r => r.FuncionarioId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Cliente)
                    .WithMany()
                    .HasForeignKey(r => r.ClienteId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(r => r.ConvertedCita)
                    .WithMany()
                    .HasForeignKey(r => r.ConvertedCitaId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<TenantBookingServiceSetting>(entity =>
            {
                entity.Property(s => s.PublicName).HasMaxLength(120);
                entity.Property(s => s.PublicDescription).HasMaxLength(300);
                entity.Property(s => s.Category).HasMaxLength(80);

                // Un registro por servicio y tenant.
                entity.HasIndex(s => new { s.TenantId, s.ServicioId })
                    .IsUnique()
                    .HasDatabaseName("UX_TenantBookingServiceSettings_TenantId_ServicioId");

                entity.HasOne(s => s.Servicio)
                    .WithMany()
                    .HasForeignKey(s => s.ServicioId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TenantBookingFuncionarioService>(entity =>
            {
                // Una relación única por funcionario+servicio dentro del tenant.
                entity.HasIndex(fs => new { fs.TenantId, fs.FuncionarioId, fs.ServicioId })
                    .IsUnique()
                    .HasDatabaseName("UX_TenantBookingFuncionarioServices_Tenant_Func_Servicio");

                entity.HasIndex(fs => new { fs.TenantId, fs.ServicioId })
                    .HasDatabaseName("IX_TenantBookingFuncionarioServices_Tenant_Servicio");

                entity.HasOne(fs => fs.Funcionario)
                    .WithMany()
                    .HasForeignKey(fs => fs.FuncionarioId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(fs => fs.Servicio)
                    .WithMany()
                    .HasForeignKey(fs => fs.ServicioId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TenantNotification>(entity =>
            {
                entity.Property(n => n.Type).HasMaxLength(60).IsRequired();
                entity.Property(n => n.Title).HasMaxLength(150).IsRequired();
                entity.Property(n => n.Message).HasMaxLength(400).IsRequired();
                entity.Property(n => n.ActionUrl).HasMaxLength(300);
                entity.Property(n => n.EntityType).HasMaxLength(60);
                entity.Property(n => n.Source).HasMaxLength(40).IsRequired();

                // Burbuja: contar no leídas y traer recientes por tenant de forma barata.
                entity.HasIndex(n => new { n.TenantId, n.IsRead, n.CreatedAtUtc })
                    .IsDescending(false, false, true)
                    .HasDatabaseName("IX_TenantNotifications_TenantId_IsRead_CreatedAtUtc");

                // Idempotencia anti-duplicados por evento (Type + EntityType + EntityId) por tenant.
                entity.HasIndex(n => new { n.TenantId, n.Type, n.EntityType, n.EntityId })
                    .IsUnique()
                    .HasFilter("[EntityType] IS NOT NULL AND [EntityId] IS NOT NULL")
                    .HasDatabaseName("UX_TenantNotifications_TenantId_Type_Entity");
            });

            modelBuilder.Entity<TenantMonthlyReportSettings>(entity =>
            {
                entity.Property(s => s.AdditionalRecipients).HasMaxLength(1000);
                entity.Property(s => s.SendToOwnerEmail).HasDefaultValue(true);
                entity.Property(s => s.SendToAllAdmins).HasDefaultValue(true);
                entity.Property(s => s.RequireConfirmedEmail).HasDefaultValue(false);
                entity.Property(s => s.IncludeManualRecipients).HasDefaultValue(true);
                entity.Property(s => s.IncludeFinancialData).HasDefaultValue(true);
                entity.Property(s => s.IncludeOperationalData).HasDefaultValue(true);
                entity.Property(s => s.IncludeRecommendations).HasDefaultValue(true);
                entity.Property(s => s.IncludeMonthOverMonth).HasDefaultValue(true);
                entity.Property(s => s.SendDayOfMonth).HasDefaultValue(1);
                entity.Property(s => s.SendHour).HasDefaultValue(8);
                entity.Property(s => s.LastAutomaticError).HasMaxLength(500);

                // Una configuración por tenant.
                entity.HasIndex(s => s.TenantId)
                    .IsUnique()
                    .HasDatabaseName("UX_TenantMonthlyReportSettings_TenantId");

                // Fase 2: el scheduler buscará tenants con IsEnabled = true.
                entity.HasIndex(s => new { s.TenantId, s.IsEnabled })
                    .HasDatabaseName("IX_TenantMonthlyReportSettings_TenantId_IsEnabled");
            });

            modelBuilder.Entity<TenantMonthlyReportEmailLog>(entity =>
            {
                entity.Property(l => l.RecipientEmail).HasMaxLength(256).IsRequired();
                entity.Property(l => l.Subject).HasMaxLength(200).IsRequired();
                entity.Property(l => l.Status).HasMaxLength(20).IsRequired();
                entity.Property(l => l.TriggeredByUserId).HasMaxLength(450);
                entity.Property(l => l.ProviderMessageId).HasMaxLength(100);
                entity.Property(l => l.ErrorMessage).HasMaxLength(500);
                entity.Property(l => l.ContentHash).HasMaxLength(64);

                entity.HasIndex(l => new { l.TenantId, l.ReportYear, l.ReportMonth })
                    .HasDatabaseName("IX_TenantMonthlyReportEmailLogs_Tenant_Anio_Mes");

                entity.HasIndex(l => new { l.TenantId, l.ReportYear, l.ReportMonth, l.RecipientEmail, l.IsTest })
                    .HasDatabaseName("IX_TenantMonthlyReportEmailLogs_Tenant_Periodo_Correo_Test");

                // Idempotencia dura: a lo sumo UN envío real exitoso por tenant/mes/correo.
                // Las pruebas (IsTest = 1) y los intentos fallidos pueden repetirse.
                entity.HasIndex(l => new { l.TenantId, l.ReportYear, l.ReportMonth, l.RecipientEmail })
                    .IsUnique()
                    .HasFilter("[IsTest] = 0 AND [Status] = 'Sent'")
                    .HasDatabaseName("UX_TenantMonthlyReportEmailLogs_RealSent");

                entity.HasIndex(l => new { l.TenantId, l.Status })
                    .HasDatabaseName("IX_TenantMonthlyReportEmailLogs_Tenant_Status");

                entity.HasIndex(l => l.CreatedAt)
                    .HasDatabaseName("IX_TenantMonthlyReportEmailLogs_CreatedAt");
            });

            modelBuilder.Entity<PlatformAuditLog>(entity =>
            {
                // Bitácora append-only cross-tenant. NO es ITenantEntity: queda fuera del RLS.
                entity.Property(log => log.ActorUserId).HasMaxLength(450).IsRequired();
                entity.Property(log => log.ActorEmail).HasMaxLength(256).IsRequired();
                entity.Property(log => log.Action).HasMaxLength(80).IsRequired();
                entity.Property(log => log.EntityType).HasMaxLength(60).IsRequired();
                entity.Property(log => log.EntityId).HasMaxLength(450);
                entity.Property(log => log.TenantName).HasMaxLength(150);
                entity.Property(log => log.TargetUserId).HasMaxLength(450);
                entity.Property(log => log.TargetUserEmail).HasMaxLength(256);
                entity.Property(log => log.Reason).HasMaxLength(500);
                entity.Property(log => log.IpAddress).HasMaxLength(64);
                entity.Property(log => log.UserAgent).HasMaxLength(512);

                entity.HasIndex(log => log.CreatedAtUtc)
                    .HasDatabaseName("IX_PlatformAuditLogs_CreatedAtUtc");
                entity.HasIndex(log => log.ActorUserId)
                    .HasDatabaseName("IX_PlatformAuditLogs_ActorUserId");
                entity.HasIndex(log => log.TenantId)
                    .HasDatabaseName("IX_PlatformAuditLogs_TenantId");
                entity.HasIndex(log => new { log.EntityType, log.EntityId })
                    .HasDatabaseName("IX_PlatformAuditLogs_EntityType_EntityId");
                entity.HasIndex(log => log.Action)
                    .HasDatabaseName("IX_PlatformAuditLogs_Action");
            });

            modelBuilder.Entity<PlatformCommercialSnapshot>(entity =>
            {
                // Historia comercial mensual (AD-4). Cross-tenant, fuera del RLS (no es
                // ITenantEntity). Una fila por mes calendario; nunca se purga (AD-5).
                entity.Property(snapshot => snapshot.TriggerType).HasMaxLength(20).IsRequired();
                entity.Property(snapshot => snapshot.TriggeredByEmail).HasMaxLength(256);
                entity.Property(snapshot => snapshot.MrrTotal).HasColumnType("decimal(18,2)");
                entity.Property(snapshot => snapshot.ArrTotal).HasColumnType("decimal(18,2)");
                entity.Property(snapshot => snapshot.ChurnedMrr).HasColumnType("decimal(18,2)");

                entity.HasIndex(snapshot => new { snapshot.PeriodYear, snapshot.PeriodMonth })
                    .IsUnique()
                    .HasDatabaseName("UX_PlatformCommercialSnapshots_Period");
            });

            modelBuilder.Entity<PlatformWorkerHeartbeat>(entity =>
            {
                // Latidos de workers. Cross-tenant, fuera del RLS (no es ITenantEntity).
                entity.HasKey(h => h.WorkerName);
                entity.Property(h => h.WorkerName).HasMaxLength(100);
                entity.Property(h => h.LastCycleSummary).HasMaxLength(300);
            });

            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.Property(t => t.Nombre).IsRequired();
                entity.Property(t => t.CommercialNotes).HasMaxLength(250);
                entity.Property(t => t.CommercialUpdatedByUserId).HasMaxLength(450);

                // Configuración fiscal del negocio (defaults CR: IVA incluido, 13%).
                entity.Property(t => t.PreciosIncluyenIva)
                    .HasDefaultValue(FiscalDefaults.PreciosIncluyenIvaPorDefecto);
                entity.Property(t => t.TarifaIvaPorDefecto)
                    .HasColumnType("decimal(18,2)")
                    .HasDefaultValue(FiscalDefaults.TarifaIvaPorDefecto);

                entity.HasOne(t => t.ForcedPlan)
                    .WithMany()
                    .HasForeignKey(t => t.ForcedPlanId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Suscripcion>(entity =>
            {
                entity.HasIndex(s => s.TenantId).IsUnique();
                entity.Property(s => s.MotivoEstado).HasMaxLength(250);
                entity.Property(s => s.CodigoPlan).HasMaxLength(50);
                entity.Property(s => s.PrecioMensual).HasColumnType("decimal(18,2)");
                entity.Property(s => s.MonedaFacturacion).HasMaxLength(10);
            });

            modelBuilder.Entity<TenantSubscriptionAddon>(entity =>
            {
                entity.HasIndex(addon => addon.TenantId).IsUnique();
                entity.HasIndex(addon => addon.ProviderSubscriptionId)
                    .IsUnique()
                    .HasFilter("[ProviderSubscriptionId] IS NOT NULL");
                entity.Property(addon => addon.AddonCode).HasMaxLength(50).IsRequired();
                entity.Property(addon => addon.PrecioMensual).HasColumnType("decimal(18,2)");
                entity.Property(addon => addon.MonedaFacturacion).HasMaxLength(10);

                entity.HasOne(addon => addon.Tenant)
                    .WithMany(tenant => tenant.SubscriptionAddons)
                    .HasForeignKey(addon => addon.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(addon => addon.Plan)
                    .WithMany()
                    .HasForeignKey(addon => addon.PlanId)
                    .OnDelete(DeleteBehavior.Restrict);
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
                entity.HasIndex(p => new { p.Proveedor, p.ProviderSubscriberId }).HasFilter("[ProviderSubscriberId] IS NOT NULL");
                entity.Property(p => p.Monto).HasColumnType("decimal(18,2)");
                entity.Property(p => p.ProviderSubscriberId).HasMaxLength(100);
                entity.Property(p => p.CorrelationToken).HasMaxLength(100);
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
                entity.Property(e => e.ProviderSubscriberId).HasMaxLength(100);
                entity.Property(e => e.Moneda).HasMaxLength(10);
                entity.Property(e => e.Monto).HasColumnType("decimal(18,2)");
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
        public DbSet<ClienteServicioRealizado> ClienteServiciosRealizados { get; set; }
        //Calendar
        public DbSet<Cita> Citas { get; set; }
        public DbSet<WhatsAppMessageLog> WhatsAppMessageLogs { get; set; }
        public DbSet<TenantWhatsAppSettings> TenantWhatsAppSettings { get; set; }
        //Finanzas
        public DbSet<Cobro> Cobros { get; set; }
        public DbSet<Servicio> Servicios { get; set; }
        //Comprobantes (comprobante digital interno, no fiscal)
        public DbSet<ComprobanteCobro> ComprobantesCobro { get; set; }
        public DbSet<ComprobanteCobroLinea> ComprobanteCobroLineas { get; set; }
        public DbSet<ComprobanteCobroSecuencia> ComprobanteCobroSecuencias { get; set; }
        public DbSet<Egreso> Egresos { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        //Productos
        public DbSet<Producto> Productos { get; set; }
        public DbSet<DetalleCobroProducto> DetalleCobroProductos { get; set; }
        public DbSet<MovimientoInventario> MovimientosInventario { get; set; }
        //Funcionarios
        public DbSet<Funcionario> Funcionarios { get; set; }
        public DbSet<FuncionarioPortalPermiso> FuncionarioPortalPermisos { get; set; }
        public DbSet<Puesto> Puestos { get; set; }
        public DbSet<PagoFuncionario> PagosFuncionarios { get; set; }
        public DbSet<LiquidacionSemanal> LiquidacionesSemanales { get; set; }
        public DbSet<LiquidacionSemanalDetalle> LiquidacionesSemanalesDetalle { get; set; }
        public DbSet<LiquidacionSemanalDistribucionMensual> LiquidacionesSemanalesDistribucionMensual { get; set; }
        //Saas 
        public DbSet<Plan> Planes { get; set; }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<Suscripcion> Suscripciones { get; set; }
        public DbSet<TenantSubscriptionAddon> TenantSubscriptionAddons { get; set; }
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
        //Reservas online (Fase 1)
        public DbSet<TenantBookingSettings> TenantBookingSettings { get; set; }
        public DbSet<BookingRequest> BookingRequests { get; set; }
        //Reservas online: catálogo publicable y relación servicio-funcionario
        public DbSet<TenantBookingServiceSetting> TenantBookingServiceSettings { get; set; }
        public DbSet<TenantBookingFuncionarioService> TenantBookingFuncionarioServices { get; set; }
        public DbSet<TenantPublicPage> TenantPublicPages { get; set; }
        public DbSet<TenantPublicAsset> TenantPublicAssets { get; set; }
        public DbSet<TenantPublicPageDailyMetric> TenantPublicPageDailyMetrics { get; set; }
        //Centro de Notificaciones
        public DbSet<TenantNotification> TenantNotifications { get; set; }
        //Resumen Ejecutivo Mensual (LuxuryCloud Insights)
        public DbSet<TenantMonthlyReportSettings> TenantMonthlyReportSettings { get; set; }
        public DbSet<TenantMonthlyReportEmailLog> TenantMonthlyReportEmailLogs { get; set; }

        public DbSet<PlatformAuditLog> PlatformAuditLogs { get; set; }
        public DbSet<PlatformWorkerHeartbeat> PlatformWorkerHeartbeats { get; set; }
        public DbSet<PlatformCommercialSnapshot> PlatformCommercialSnapshots { get; set; }
        public DbSet<PlanChangeIntent> PlanChangeIntents { get; set; }
        public DbSet<SubscriptionPaymentIncident> SubscriptionPaymentIncidents { get; set; }





    }



}
