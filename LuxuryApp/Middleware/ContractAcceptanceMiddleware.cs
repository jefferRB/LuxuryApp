using System.Security.Claims;
using LuxuryApp.Services.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace LuxuryApp.Middleware
{
    public sealed class ContractAcceptanceMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ContractAcceptanceMiddleware> _logger;

        public ContractAcceptanceMiddleware(
            RequestDelegate next,
            ILogger<ContractAcceptanceMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context, IContractService contractService)
        {
            var path = (context.Request.Path.Value ?? string.Empty).ToLowerInvariant();

            if (path.StartsWith("/accounts") ||
                path.StartsWith("/home") ||
                path.StartsWith("/error") ||
                path.StartsWith("/contract") ||
                path.StartsWith("/api/webhooks/meta-whatsapp"))
            {
                await _next(context);
                return;
            }

            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogWarning("Sesion autenticada sin NameIdentifier. Se cerrara la cookie actual.");
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.Redirect("/Accounts/Acceso");
                return;
            }

            var status = await contractService.GetAcceptanceStatusAsync(userId, context.RequestAborted);
            context.Items["ContractAcceptanceStatus"] = status;

            if (!status.BlocksApplicationAccess)
            {
                await _next(context);
                return;
            }

            var returnUrl = BuildCurrentReturnUrl(context);
            var redirectUrl = $"/Contract/Reaccept{returnUrl}";
            context.Response.Redirect(redirectUrl);
        }

        private static string BuildCurrentReturnUrl(HttpContext context)
        {
            var currentPath = $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            return QueryString.Create("returnurl", currentPath).ToString();
        }
    }
}
