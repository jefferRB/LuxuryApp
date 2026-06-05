using Microsoft.Extensions.Primitives;

namespace LuxuryApp.Services.Contracts
{
    public static class ContractAcceptanceBindingHelper
    {
        public static bool NormalizeAcceptedValue(
            HttpRequest request,
            string fieldName,
            bool currentValue)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!request.HasFormContentType)
            {
                return currentValue;
            }

            if (!request.Form.TryGetValue(fieldName, out StringValues values) || values.Count == 0)
            {
                return currentValue;
            }

            return values.Any(IsTruthyValue);
        }

        private static bool IsTruthyValue(string? value) =>
            string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
