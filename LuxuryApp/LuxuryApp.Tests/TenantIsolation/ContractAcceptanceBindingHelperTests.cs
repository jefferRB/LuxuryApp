using LuxuryApp.Services.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class ContractAcceptanceBindingHelperTests
    {
        [Fact]
        public void NormalizeAcceptedValue_ShouldTreatTruthyCheckboxPayloadAsAccepted()
        {
            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["AcceptCurrentContract"] = new StringValues(["false", "true"])
            });

            var accepted = ContractAcceptanceBindingHelper.NormalizeAcceptedValue(
                context.Request,
                "AcceptCurrentContract",
                currentValue: false);

            Assert.True(accepted);
        }

        [Fact]
        public void NormalizeAcceptedValue_ShouldTreatOnValueAsAccepted()
        {
            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["AcceptCurrentContract"] = new StringValues("on")
            });

            var accepted = ContractAcceptanceBindingHelper.NormalizeAcceptedValue(
                context.Request,
                "AcceptCurrentContract",
                currentValue: false);

            Assert.True(accepted);
        }

        [Fact]
        public void NormalizeAcceptedValue_ShouldStayFalseWhenOnlyFalseIsPosted()
        {
            var context = BuildHttpContext(new Dictionary<string, StringValues>
            {
                ["AcceptCurrentContract"] = new StringValues("false")
            });

            var accepted = ContractAcceptanceBindingHelper.NormalizeAcceptedValue(
                context.Request,
                "AcceptCurrentContract",
                currentValue: false);

            Assert.False(accepted);
        }

        private static DefaultHttpContext BuildHttpContext(Dictionary<string, StringValues> values)
        {
            var context = new DefaultHttpContext();
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.Form = new FormCollection(values);
            return context;
        }
    }
}
