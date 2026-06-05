namespace LuxuryApp.Models.SaaS
{
    public sealed class TilopayRepeatOptions
    {
        public bool Enabled { get; set; }
        public bool UseHostedLinks { get; set; } = true;
        public bool UseRecurringCheckoutForPublicPlans { get; set; }
        public bool EnableTestRecurringPlan { get; set; }
        public int GracePeriodDays { get; set; } = 3;
        public TilopayRepeatPlanOption Basic { get; set; } = new();
        public TilopayRepeatPlanOption Pro { get; set; } = new();
        public TilopayRepeatPlanOption Business { get; set; } = new();
        public TilopayRepeatPlanOption WhatsApp400 { get; set; } = new();
        public TilopayRepeatPlanOption WhatsApp800 { get; set; } = new();
        public TilopayRepeatPlanOption WhatsApp1200 { get; set; } = new();
        public TilopayRepeatPlanOption TestRecurring { get; set; } = new();

        public IReadOnlyCollection<TilopayRepeatPlanRegistration> GetAllPlans() =>
        [
            new("Basic", Basic),
            new("Pro", Pro),
            new("Business", Business),
            new("WhatsApp400", WhatsApp400),
            new("WhatsApp800", WhatsApp800),
            new("WhatsApp1200", WhatsApp1200),
            new("TestRecurring", TestRecurring)
        ];

        public TilopayRepeatPlanOption? FindByCode(string? code) =>
            FindRegistrationByCode(code)?.Plan;

        public TilopayRepeatPlanOption? FindByRecurringPlanId(int? recurringPlanId) =>
            FindRegistrationByRecurringPlanId(recurringPlanId)?.Plan;

        public TilopayRepeatPlanRegistration? FindRegistrationByCode(string? code) =>
            GetAllPlans().FirstOrDefault(plan =>
                !string.IsNullOrWhiteSpace(plan.Plan.Code) &&
                string.Equals(plan.Plan.Code, code, StringComparison.OrdinalIgnoreCase));

        public TilopayRepeatPlanRegistration? FindRegistrationByRecurringPlanId(int? recurringPlanId) =>
            recurringPlanId.HasValue
                ? GetAllPlans().FirstOrDefault(plan => plan.Plan.TilopayPlanId == recurringPlanId.Value)
                : null;

        public static bool IsManagedPlanCode(string? code) =>
            code is PlanCodes.Basic or
                PlanCodes.Pro or
                PlanCodes.Business or
                PlanCodes.WhatsApp400 or
                PlanCodes.WhatsApp800 or
                PlanCodes.WhatsApp1200 or
                PlanCodes.TestRecurring;
    }

    public sealed record TilopayRepeatPlanRegistration(
        string SectionKey,
        TilopayRepeatPlanOption Plan);

    public sealed class TilopayRepeatPlanOption
    {
        public int TilopayPlanId { get; set; }
        public string Code { get; set; } = string.Empty;
        public decimal MonthlyPrice { get; set; }
        public string Currency { get; set; } = "CRC";
        public int? MaxFuncionarios { get; set; }
        public int? MonthlyMessageLimit { get; set; }
        public string CheckoutUrl { get; set; } = string.Empty;
        public bool IsAddon { get; set; }
        public bool IsValidation { get; set; }
        public bool IsPublic { get; set; } = true;
    }
}
