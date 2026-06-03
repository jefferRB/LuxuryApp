using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.WhatsApp;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class TenantWhatsAppSettingsServiceTests
    {
        [Fact]
        public async Task GetSettingsForTenantAsync_WhenSettingsDoNotExist_ShouldAssumeDisabledWithoutPersisting()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;
            var service = CreateService(context, tenantProvider);

            var settings = await service.GetSettingsForTenantAsync(tenantId);

            Assert.False(settings.Exists);
            Assert.False(settings.IsEnabled);
            Assert.Equal(TenantWhatsAppSettings.DefaultDailyMessageLimit, settings.DailyMessageLimit);
            Assert.Empty(context.TenantWhatsAppSettings);
        }

        [Fact]
        public async Task CanSendNotificationAsync_WhenDailyLimitWasReached_ShouldDenyWithStableCode()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;
            var service = CreateService(context, tenantProvider);

            await SeedTenantAsync(context, tenantId);
            await service.UpdateSettingsAsync(
                tenantId,
                new TenantWhatsAppSettingsUpdateDto
                {
                    IsEnabled = true,
                    DailyMessageLimit = 1
                },
                "platform-user");

            context.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
            {
                NotificationType = WhatsAppNotificationTypes.Confirmation,
                Direction = WhatsAppMessageDirections.Outbound,
                Status = WhatsAppMessageStatuses.Pending,
                CreatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var decision = await service.CanSendNotificationAsync(tenantId, WhatsAppNotificationTypes.Reminder3Hours);

            Assert.False(decision.CanSend);
            Assert.Equal(WhatsAppErrorCodes.DailyLimitExceeded, decision.ErrorCode);
            Assert.Equal(1, decision.TodayUsage);
            Assert.Equal(1, decision.DailyMessageLimit);
        }

        [Fact]
        public async Task GetTodayUsageAsync_ShouldCountOnlyProcessableOutboundNotifications()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;
            var service = CreateService(context, tenantProvider);

            context.WhatsAppMessageLogs.AddRange(
                CreateLog(WhatsAppMessageDirections.Outbound, WhatsAppNotificationTypes.Confirmation, WhatsAppMessageStatuses.Pending),
                CreateLog(WhatsAppMessageDirections.Outbound, WhatsAppNotificationTypes.Reminder3Hours, WhatsAppMessageStatuses.Processing),
                CreateLog(WhatsAppMessageDirections.Outbound, WhatsAppNotificationTypes.Confirmation, WhatsAppMessageStatuses.Sent),
                CreateLog(WhatsAppMessageDirections.Inbound, WhatsAppNotificationTypes.Reply, WhatsAppMessageStatuses.Received),
                CreateLog(WhatsAppMessageDirections.Status, WhatsAppNotificationTypes.Status, WhatsAppMessageStatuses.Delivered),
                CreateLog(WhatsAppMessageDirections.Outbound, WhatsAppNotificationTypes.Confirmation, WhatsAppMessageStatuses.Delivered),
                CreateLog(WhatsAppMessageDirections.Outbound, WhatsAppNotificationTypes.Confirmation, WhatsAppMessageStatuses.Read),
                CreateLog(WhatsAppMessageDirections.Outbound, WhatsAppNotificationTypes.Confirmation, WhatsAppMessageStatuses.SkippedTenantDisabled));
            await context.SaveChangesAsync();

            var usage = await service.GetTodayUsageAsync(tenantId);

            Assert.Equal(3, usage);
        }

        [Fact]
        public async Task Service_ShouldRejectCrossTenantSettingsAccess()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;
            var service = CreateService(context, tenantProvider);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetSettingsForTenantAsync(Guid.NewGuid()));

            Assert.Contains("contexto de su tenant", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static TenantWhatsAppSettingsService CreateService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            TestTenantProvider tenantProvider) =>
            new(
                context,
                tenantProvider,
                new StaticOptionsMonitor<MetaWhatsAppOptions>(new MetaWhatsAppOptions { Enabled = true }),
                NullLogger<TenantWhatsAppSettingsService>.Instance);

        private static WhatsAppMessageLog CreateLog(string direction, string notificationType, string status) =>
            new()
            {
                Direction = direction,
                NotificationType = notificationType,
                Status = status,
                CreatedAtUtc = DateTime.UtcNow
            };

        private static async Task SeedTenantAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            Guid tenantId)
        {
            context.Tenants.Add(new LuxuryApp.Models.SaaS.Tenant
            {
                Id = tenantId,
                Nombre = "Tenant WhatsApp"
            });
            await context.SaveChangesAsync();
        }
    }
}
