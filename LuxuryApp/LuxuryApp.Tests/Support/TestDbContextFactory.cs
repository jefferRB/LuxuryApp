using LuxuryApp.Models.Identity;
using LuxuryApp.Services.Tenant;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Tests.Support
{
    internal static class TestDbContextFactory
    {
        public static (ApplicationDbContext Context, SqliteConnection Connection) CreateSqliteContext(TestTenantProvider tenantProvider)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new ApplicationDbContext(
                options,
                tenantProvider,
                NullLogger<ApplicationDbContext>.Instance);

            context.Database.EnsureCreated();

            return (context, connection);
        }
    }
}
