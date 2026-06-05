namespace LuxuryApp.Models.SaaS
{
    public static class PlanCodes
    {
        public const string Basic = "BASIC";
        public const string Pro = "PRO";
        public const string Business = "BUSINESS";
        public const string WhatsApp400 = "WA400";
        public const string WhatsApp800 = "WA800";
        public const string WhatsApp1200 = "WA1200";
        public const string TestRecurring = "TEST_RECURRING";

        public static readonly string[] BasePlans =
        [
            Basic,
            Pro,
            Business
        ];

        public static readonly string[] WhatsAppAddons =
        [
            WhatsApp400,
            WhatsApp800,
            WhatsApp1200
        ];
    }
}
