using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.SaaS;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Tests.Billing
{
    /// <summary>
    /// Pruebas de la calculadora dinamica: cada combinacion (funcionarios, ciclo) debe
    /// resolver una unica opcion exacta apuntando al plan recurrente correcto de TiloPay,
    /// con el monto exacto. Cubre los 22 casos obligatorios + montos + errores de config.
    /// </summary>
    public class SubscriptionPricingCatalogTests
    {
        // ---- Mapeo obligatorio: workers + ciclo -> codigo + TilopayPlanId + monto ----

        [Theory]
        [InlineData(1, BillingCycle.Monthly, "LC_M_01", 6119, 8000)]
        [InlineData(2, BillingCycle.Monthly, "LC_M_02", 6126, 15000)]
        [InlineData(3, BillingCycle.Monthly, "LC_M_03", 6127, 20000)]
        [InlineData(4, BillingCycle.Monthly, "LC_M_04", 6128, 25000)]
        [InlineData(5, BillingCycle.Monthly, "LC_M_05", 6129, 30000)]
        [InlineData(6, BillingCycle.Monthly, "LC_M_06", 6130, 35000)]
        [InlineData(7, BillingCycle.Monthly, "LC_M_07", 6131, 40000)]
        [InlineData(8, BillingCycle.Monthly, "LC_M_08", 6132, 45000)]
        [InlineData(9, BillingCycle.Monthly, "LC_M_09", 6133, 50000)]
        [InlineData(10, BillingCycle.Monthly, "LC_M_10", 6134, 55000)]
        [InlineData(11, BillingCycle.Monthly, "LC_M_11", 6135, 60000)]
        [InlineData(1, BillingCycle.Annual, "LC_A_01", 6136, 81600)]
        [InlineData(2, BillingCycle.Annual, "LC_A_02", 6137, 153000)]
        [InlineData(3, BillingCycle.Annual, "LC_A_03", 6139, 204000)]
        [InlineData(4, BillingCycle.Annual, "LC_A_04", 6140, 255000)]
        [InlineData(5, BillingCycle.Annual, "LC_A_05", 6141, 306000)]
        [InlineData(6, BillingCycle.Annual, "LC_A_06", 6142, 336000)]
        [InlineData(7, BillingCycle.Annual, "LC_A_07", 6143, 360000)]
        [InlineData(8, BillingCycle.Annual, "LC_A_08", 6144, 378000)]
        [InlineData(9, BillingCycle.Annual, "LC_A_09", 6145, 390000)]
        [InlineData(10, BillingCycle.Annual, "LC_A_10", 6146, 429000)]
        [InlineData(11, BillingCycle.Annual, "LC_A_11", 6147, 468000)]
        public void Resolve_ShouldMapWorkerCountAndCycleToExactPlan(
            int workerCount,
            BillingCycle cycle,
            string expectedCode,
            int expectedPlanId,
            int expectedCharge)
        {
            var catalog = CreateCatalog();

            var resolution = catalog.Resolve(workerCount, cycle);

            Assert.True(resolution.IsAvailable, resolution.Error);
            var option = resolution.Option!;
            Assert.Equal(expectedCode, option.Code);
            Assert.Equal(cycle, option.BillingCycle);
            Assert.Equal(workerCount, option.WorkerCount);
            Assert.Equal(expectedPlanId, option.TilopayRecurringPlanId);
            Assert.Equal(expectedCharge, option.ChargeAmount);
            Assert.Equal("CRC", option.Currency);
            Assert.Equal(workerCount, option.MaxFuncionarios);
            Assert.StartsWith("https://tp.cr/l/", option.CheckoutUrl);
        }

        // ---- Equivalente mensual exacto del ciclo anual ----

        [Theory]
        [InlineData(1, 6800)]
        [InlineData(2, 12750)]
        [InlineData(3, 17000)]
        [InlineData(7, 30000)]
        [InlineData(9, 32500)]
        [InlineData(11, 39000)]
        public void Resolve_Annual_ShouldExposeConfiguredMonthlyEquivalent(int workerCount, int expectedMonthlyEquivalent)
        {
            var catalog = CreateCatalog();

            var option = catalog.Resolve(workerCount, BillingCycle.Annual).Option!;

            Assert.Equal(expectedMonthlyEquivalent, option.MonthlyEquivalentAmount);
        }

        [Fact]
        public void Resolve_Monthly_MonthlyEquivalentEqualsCharge()
        {
            var catalog = CreateCatalog();

            var option = catalog.Resolve(5, BillingCycle.Monthly).Option!;

            Assert.Equal(option.ChargeAmount, option.MonthlyEquivalentAmount);
        }

        // ---- Mensual y anual nunca se cruzan ----

        [Fact]
        public void Resolve_AnnualPlanNeverPointsToMonthlyTilopayPlan()
        {
            var catalog = CreateCatalog();

            var monthly = catalog.Resolve(9, BillingCycle.Monthly).Option!;
            var annual = catalog.Resolve(9, BillingCycle.Annual).Option!;

            Assert.Equal(6133, monthly.TilopayRecurringPlanId);
            Assert.Equal(6145, annual.TilopayRecurringPlanId);
            Assert.NotEqual(monthly.TilopayRecurringPlanId, annual.TilopayRecurringPlanId);
            Assert.NotEqual(monthly.Code, annual.Code);
        }

        // ---- Round-trip por codigo ----

        [Theory]
        [InlineData("LC_M_01", 1, BillingCycle.Monthly)]
        [InlineData("LC_A_09", 9, BillingCycle.Annual)]
        [InlineData("LC_M_11", 11, BillingCycle.Monthly)]
        public void ResolveByCode_ShouldRoundTrip(string code, int expectedWorkers, BillingCycle expectedCycle)
        {
            var catalog = CreateCatalog();

            var option = catalog.ResolveByCode(code).Option!;

            Assert.Equal(expectedWorkers, option.WorkerCount);
            Assert.Equal(expectedCycle, option.BillingCycle);
            Assert.Equal(code, option.Code);
        }

        // ---- Rango invalido ----

        [Theory]
        [InlineData(0, BillingCycle.Monthly)]
        [InlineData(12, BillingCycle.Monthly)]
        [InlineData(0, BillingCycle.Annual)]
        [InlineData(99, BillingCycle.Annual)]
        public void Resolve_OutOfRange_IsNotAvailable(int workerCount, BillingCycle cycle)
        {
            var catalog = CreateCatalog();

            var resolution = catalog.Resolve(workerCount, cycle);

            Assert.False(resolution.IsAvailable);
            Assert.NotNull(resolution.Error);
        }

        // ---- Configuracion faltante => no comprable, sin inventar montos ----

        [Fact]
        public void Resolve_MissingCheckoutUrl_IsConfigErrorAndNotPurchasable()
        {
            var options = BuildProductionRepeatOptions();
            var broken = options.Calculator.Single(plan => plan.Code == "LC_M_11");
            broken.CheckoutUrl = string.Empty;
            var catalog = new SubscriptionPricingCatalog(Options.Create(options));

            var resolution = catalog.Resolve(11, BillingCycle.Monthly);

            Assert.False(resolution.IsAvailable);
            Assert.Contains("CheckoutUrl", resolution.Error);
        }

        [Fact]
        public void Resolve_UnconfiguredCombination_IsNotAvailable()
        {
            var options = BuildProductionRepeatOptions();
            options.Calculator.RemoveAll(plan => plan.Code == "LC_A_07");
            var catalog = new SubscriptionPricingCatalog(Options.Create(options));

            var resolution = catalog.Resolve(7, BillingCycle.Annual);

            Assert.False(resolution.IsAvailable);
            Assert.Contains("LC_A_07", resolution.Error);
        }

        // ---- Catalogo completo ----

        [Fact]
        public void EnumerateAll_ReturnsTwentyTwoCombinations()
        {
            var catalog = CreateCatalog();

            var all = catalog.EnumerateAll();

            Assert.Equal(22, all.Count);
            Assert.All(all, resolution => Assert.True(resolution.IsAvailable, resolution.Error));
        }

        [Fact]
        public void ListPublicAvailable_ReturnsAllTwentyTwoWhenConfigured()
        {
            var catalog = CreateCatalog();

            var publicOptions = catalog.ListPublicAvailable();

            Assert.Equal(22, publicOptions.Count);
            Assert.Equal(22, publicOptions.Select(option => option.Code).Distinct().Count());
        }

        // ---- Helpers: catalogo con los valores reales de produccion ----

        private static SubscriptionPricingCatalog CreateCatalog() =>
            new(Options.Create(BuildProductionRepeatOptions()));

        private static TilopayRepeatOptions BuildProductionRepeatOptions()
        {
            var options = new TilopayRepeatOptions
            {
                Enabled = true,
                UseHostedLinks = true
            };

            options.Calculator.AddRange(new[]
            {
                Monthly(1, 6119, 8000m, "TmpFeE9RPT18MQ=="),
                Monthly(2, 6126, 15000m, "TmpFeU5nPT18MQ=="),
                Monthly(3, 6127, 20000m, "TmpFeU53PT18MQ=="),
                Monthly(4, 6128, 25000m, "TmpFeU9BPT18MQ=="),
                Monthly(5, 6129, 30000m, "TmpFeU9RPT18MQ=="),
                Monthly(6, 6130, 35000m, "TmpFek1BPT18MQ=="),
                Monthly(7, 6131, 40000m, "TmpFek1RPT18MQ=="),
                Monthly(8, 6132, 45000m, "TmpFek1nPT18MQ=="),
                Monthly(9, 6133, 50000m, "TmpFek13PT18MQ=="),
                Monthly(10, 6134, 55000m, "TmpFek5BPT18MQ=="),
                Monthly(11, 6135, 60000m, "TmpFek5RPT18MQ=="),
                Annual(1, 6136, 81600m, 6800m, "TmpFek5nPT18MQ=="),
                Annual(2, 6137, 153000m, 12750m, "TmpFek53PT18MQ=="),
                Annual(3, 6139, 204000m, 17000m, "TmpFek9RPT18MQ=="),
                Annual(4, 6140, 255000m, 21250m, "TmpFME1BPT18MQ=="),
                Annual(5, 6141, 306000m, 25500m, "TmpFME1RPT18MQ=="),
                Annual(6, 6142, 336000m, 28000m, "TmpFME1nPT18MQ=="),
                Annual(7, 6143, 360000m, 30000m, "TmpFME13PT18MQ=="),
                Annual(8, 6144, 378000m, 31500m, "TmpFME5BPT18MQ=="),
                Annual(9, 6145, 390000m, 32500m, "TmpFME5RPT18MQ=="),
                Annual(10, 6146, 429000m, 35750m, "TmpFME5nPT18MQ=="),
                Annual(11, 6147, 468000m, 39000m, "TmpFME53PT18MQ==")
            });

            return options;
        }

        private static TilopayRepeatPlanOption Monthly(int workers, int planId, decimal charge, string linkSuffix) => new()
        {
            Code = PlanCodes.BuildCalculatorCode(workers, BillingCycle.Monthly)!,
            TilopayPlanId = planId,
            MonthlyPrice = charge,
            BillingCycle = BillingCycle.Monthly,
            Currency = "CRC",
            MaxFuncionarios = workers,
            CheckoutUrl = $"https://tp.cr/l/{linkSuffix}",
            UsesRecurringCheckout = true,
            IsPublic = true
        };

        private static TilopayRepeatPlanOption Annual(int workers, int planId, decimal charge, decimal monthlyEquivalent, string linkSuffix) => new()
        {
            Code = PlanCodes.BuildCalculatorCode(workers, BillingCycle.Annual)!,
            TilopayPlanId = planId,
            MonthlyPrice = charge,
            MonthlyEquivalentAmount = monthlyEquivalent,
            BillingCycle = BillingCycle.Annual,
            Currency = "CRC",
            MaxFuncionarios = workers,
            CheckoutUrl = $"https://tp.cr/l/{linkSuffix}",
            UsesRecurringCheckout = true,
            IsPublic = true
        };
    }
}
