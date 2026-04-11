using ClosedXML.Excel;
using LuxuryApp.Controllers;
using LuxuryApp.Controllers.DataBase;
using LuxuryApp.Controllers.Finanzas;
using LuxuryApp.Controllers.Funcionarios;
using LuxuryApp.Controllers.Identity;
using LuxuryApp.Models.Common;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services;
using Microsoft.AspNetCore.Http;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class EndpointBindingSecurityTests
    {
        [Fact]
        public async Task CategoriaCreate_ShouldIgnoreTenantIdOverpostingAndForceInternalState()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            tenantProvider.TenantId = tenantA;

            var controller = new CategoriasController(context)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            var maliciousPayload = new Categoria
            {
                TenantId = tenantB,
                Nombre = "Categoria segura",
                Detalle = "Payload manipulado",
                Activo = false
            };

            var result = await controller.Create(maliciousPayload);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(CategoriasController.Index), redirect.ActionName);

            var persisted = context.Categorias.Single();
            Assert.Equal(tenantA, persisted.TenantId);
            Assert.True(persisted.Activo);
        }

        [Fact]
        public async Task SecondaryJsonEndpoint_ShouldNotExposeOtherTenantRecords()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            tenantProvider.TenantId = tenantB;
            context.Servicios.Add(new Servicio
            {
                Nombre = "Servicio privado",
                Precio = 50,
                DuracionMinutos = 60,
                Activo = true
            });
            await context.SaveChangesAsync();

            var foreignServiceId = context.Servicios.Single().Id;

            tenantProvider.TenantId = tenantA;
            context.ChangeTracker.Clear();

            var controller = new ServiciosController(context);
            var result = await controller.ObtenerPrecio(foreignServiceId);

            Assert.IsType<JsonResult>(result);
            Assert.Null(result.Value);
        }

        [Fact]
        public async Task ClientesAutocomplete_ShouldOnlyReturnCurrentTenantMatches()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            tenantProvider.TenantId = tenantB;
            context.Clientes.Add(new ClientesModel
            {
                Nombre = "Cliente Privado",
                NumeroTelefono = "2222",
                CorreoElectronico = "private@test.local",
                FechaUltimaVisita = DateTime.UtcNow,
                FrecuenciaVisita = 30
            });
            await context.SaveChangesAsync();

            tenantProvider.TenantId = tenantA;
            context.Clientes.Add(new ClientesModel
            {
                Nombre = "Cliente Publico",
                NumeroTelefono = "1111",
                CorreoElectronico = "public@test.local",
                FechaUltimaVisita = DateTime.UtcNow,
                FrecuenciaVisita = 30
            });
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var controller = new ClientesController(context, null!, null!, null!);
            var result = await controller.Autocompletado("Cliente");

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
            var serialized = System.Text.Json.JsonSerializer.Serialize(payload);

            Assert.Contains("Cliente Publico", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("Cliente Privado", serialized, StringComparison.Ordinal);
        }

        [Fact]
        public async Task FuncionariosGetActivos_ShouldOnlyReturnCurrentTenantRecords()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            tenantProvider.TenantId = tenantB;
            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Puesto Privado",
                Activo = true
            });
            await context.SaveChangesAsync();
            var foreignPuestoId = context.Puestos.Single().IdPuesto;

            context.Funcionarios.Add(new Funcionario
            {
                Nombre = "Funcionario Privado",
                IdPuesto = foreignPuestoId,
                ColorCalendario = "#111111",
                PorcentajeGanancia = 10,
                PorcentajeProducto = 10,
                FechaIngreso = DateTime.UtcNow,
                Activo = true
            });
            await context.SaveChangesAsync();

            tenantProvider.TenantId = tenantA;
            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Puesto Visible",
                Activo = true
            });
            await context.SaveChangesAsync();
            var currentPuestoId = context.Puestos
                .Single(puesto => puesto.NombrePuesto == "Puesto Visible").IdPuesto;

            context.Funcionarios.Add(new Funcionario
            {
                Nombre = "Funcionario Visible",
                IdPuesto = currentPuestoId,
                ColorCalendario = "#222222",
                PorcentajeGanancia = 15,
                PorcentajeProducto = 15,
                FechaIngreso = DateTime.UtcNow,
                Activo = true
            });
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var controller = new FuncionariosController(context);
            var result = await controller.GetActivos();

            var json = Assert.IsType<JsonResult>(result);
            var serialized = System.Text.Json.JsonSerializer.Serialize(json.Value);

            Assert.Contains("Funcionario Visible", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("Funcionario Privado", serialized, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CobrosExportarExcel_ShouldExcludeForeignTenantRows()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            tenantProvider.TenantId = tenantB;
            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Puesto Privado",
                Activo = true
            });
            await context.SaveChangesAsync();
            var foreignPuestoId = context.Puestos.Single().IdPuesto;

            var foreignFuncionario = new Funcionario
            {
                Nombre = "Funcionario Privado",
                IdPuesto = foreignPuestoId,
                ColorCalendario = "#111111",
                PorcentajeGanancia = 10,
                PorcentajeProducto = 10,
                FechaIngreso = DateTime.UtcNow,
                Activo = true
            };
            context.Funcionarios.Add(foreignFuncionario);
            await context.SaveChangesAsync();

            context.Cobros.Add(new Cobro
            {
                NombreCliente = "Cliente Privado",
                FuncionarioId = foreignFuncionario.IdFuncionario,
                FechaCobro = DateTime.Today,
                Monto = 1250,
                MetodoPago = "EFECTIVO"
            });
            await context.SaveChangesAsync();

            tenantProvider.TenantId = tenantA;
            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Puesto Visible",
                Activo = true
            });
            await context.SaveChangesAsync();
            var currentPuestoId = context.Puestos
                .Single(puesto => puesto.NombrePuesto == "Puesto Visible").IdPuesto;

            var currentFuncionario = new Funcionario
            {
                Nombre = "Funcionario Visible",
                IdPuesto = currentPuestoId,
                ColorCalendario = "#222222",
                PorcentajeGanancia = 12,
                PorcentajeProducto = 12,
                FechaIngreso = DateTime.UtcNow,
                Activo = true
            };
            context.Funcionarios.Add(currentFuncionario);
            await context.SaveChangesAsync();

            context.Cobros.Add(new Cobro
            {
                NombreCliente = "Cliente Visible",
                FuncionarioId = currentFuncionario.IdFuncionario,
                FechaCobro = DateTime.Today,
                Monto = 1500,
                MetodoPago = "SINPE"
            });
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var controller = new CobrosController(context);
            var result = await controller.ExportarExcel(new CobroFiltroViewModel { VistaTiempo = "todo" });

            var file = Assert.IsType<FileContentResult>(result);
            using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
            var text = string.Join(
                "|",
                workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed().Select(cell => cell.GetString())));

            Assert.Contains("Cliente Visible", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Cliente Privado", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EgresosExportarExcel_ShouldExcludeForeignTenantRows()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            tenantProvider.TenantId = tenantB;
            context.Categorias.Add(new Categoria
            {
                Nombre = "Categoria Privada",
                Detalle = "B",
                Activo = true
            });
            await context.SaveChangesAsync();
            var foreignCategoryId = context.Categorias.Single().Id;

            context.Egresos.Add(new Egreso
            {
                CategoriaId = foreignCategoryId,
                FechaEgreso = DateTime.Today,
                Detalle = "Egreso Privado",
                Monto = 500,
                MetodoPago = "EFECTIVO"
            });
            await context.SaveChangesAsync();

            tenantProvider.TenantId = tenantA;
            context.Categorias.Add(new Categoria
            {
                Nombre = "Categoria Visible",
                Detalle = "A",
                Activo = true
            });
            await context.SaveChangesAsync();
            var currentCategoryId = context.Categorias
                .Single(category => category.Nombre == "Categoria Visible").Id;

            context.Egresos.Add(new Egreso
            {
                CategoriaId = currentCategoryId,
                FechaEgreso = DateTime.Today,
                Detalle = "Egreso Visible",
                Monto = 750,
                MetodoPago = "TARJETA"
            });
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var controller = new EgresosController(context);
            var result = await controller.ExportarExcel(new EgresoFiltroViewModel { VistaTiempo = "dia" });

            var file = Assert.IsType<FileContentResult>(result);
            using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
            var text = string.Join(
                "|",
                workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed().Select(cell => cell.GetString())));

            Assert.Contains("Egreso Visible", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Egreso Privado", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SubscriptionHistory_ShouldBeFilteredByCurrentTenant()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            tenantProvider.TenantId = Guid.Empty;

            var tenantA = new Tenant { Id = Guid.NewGuid(), Nombre = "Tenant A", Activo = true };
            var tenantB = new Tenant { Id = Guid.NewGuid(), Nombre = "Tenant B", Activo = true };
            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                Nombre = "Plan Test",
                PrecioMensual = 10,
                Moneda = "CRC",
                Activo = true
            };

            context.Tenants.AddRange(tenantA, tenantB);
            context.Planes.Add(plan);
            await context.SaveChangesAsync();

            tenantProvider.TenantId = tenantA.Id;
            var subscriptionA = new Suscripcion
            {
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                ProviderReference = "LXA-AA1101-ABCDEF0001",
                FechaInicio = DateTime.UtcNow
            };
            context.Suscripciones.Add(subscriptionA);
            await context.SaveChangesAsync();

            context.HistorialSuscripciones.Add(new HistorialSuscripcion
            {
                Id = Guid.NewGuid(),
                SuscripcionId = subscriptionA.Id,
                PlanIdNuevo = plan.Id,
                FechaCambio = DateTime.UtcNow,
                Proveedor = PaymentProviderType.Tilopay,
                Motivo = "Tenant A"
            });
            await context.SaveChangesAsync();

            tenantProvider.TenantId = tenantB.Id;
            var subscriptionB = new Suscripcion
            {
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                ProviderReference = "LXA-BB2202-ABCDEF0002",
                FechaInicio = DateTime.UtcNow
            };
            context.Suscripciones.Add(subscriptionB);
            await context.SaveChangesAsync();

            context.HistorialSuscripciones.Add(new HistorialSuscripcion
            {
                Id = Guid.NewGuid(),
                SuscripcionId = subscriptionB.Id,
                PlanIdNuevo = plan.Id,
                FechaCambio = DateTime.UtcNow,
                Proveedor = PaymentProviderType.Tilopay,
                Motivo = "Tenant B"
            });
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            tenantProvider.TenantId = tenantA.Id;
            var tenantAHistories = await context.HistorialSuscripciones
                .Include(historial => historial.Suscripcion)
                .ToListAsync();

            Assert.Single(tenantAHistories);
            Assert.Equal("Tenant A", tenantAHistories[0].Motivo);
            Assert.Equal(tenantA.Id, tenantAHistories[0].Suscripcion?.TenantId);
        }

        [Fact]
        public void TenantEntityProperties_ShouldBeBindNever()
        {
            var tenantEntityTypes = typeof(ProyectoIdentity.Datos.ApplicationDbContext).Assembly
                .GetTypes()
                .Where(type => typeof(ITenantEntity).IsAssignableFrom(type) && type.IsClass)
                .ToArray();

            var violations = tenantEntityTypes
                .Where(type => type.GetProperty(nameof(ITenantEntity.TenantId))?
                    .GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ModelBinding.BindNeverAttribute), inherit: true)
                    .Any() != true)
                .Select(type => type.FullName)
                .OrderBy(name => name)
                .ToArray();

            Assert.True(
                violations.Length == 0,
                "TenantId sin BindNever en: " + string.Join(", ", violations));
        }

        [Fact]
        public void IgnoreQueryFilters_ShouldOnlyExistInApprovedInfrastructureFiles()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine("Services", "Payments", "SaaSPaymentService.cs"),
                Path.Combine("Services", "Stripe", "SuscripcionService.cs")
            };

            var targetRoots = new[]
            {
                Path.Combine(repoRoot, "Controllers"),
                Path.Combine(repoRoot, "Datos"),
                Path.Combine(repoRoot, "Services"),
                Path.Combine(repoRoot, "Workers")
            };

            var violations = targetRoots
                .SelectMany(root => Directory.Exists(root)
                    ? Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                    : Enumerable.Empty<string>())
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                               !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(file => File.ReadAllText(file).Contains(".IgnoreQueryFilters()", StringComparison.Ordinal))
                .Select(file => Path.GetRelativePath(repoRoot, file))
                .Where(relativePath => !allowedFiles.Contains(relativePath))
                .OrderBy(path => path)
                .ToArray();

            Assert.True(
                violations.Length == 0,
                "Uso inesperado de IgnoreQueryFilters en: " + string.Join(", ", violations));
        }
    }
}
