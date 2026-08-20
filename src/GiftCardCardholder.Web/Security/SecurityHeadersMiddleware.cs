using GiftCardCardholder.Web.Configuration;
using Microsoft.Extensions.Options;

namespace GiftCardCardholder.Web.Security;

/// <summary>
/// Applies conservative response security headers.
///
/// JavaScript enhancements are disabled by default. When an operator enables
/// them, only scripts from this application origin are permitted; inline and
/// third-party script remain blocked. <c>Referrer-Policy: no-referrer</c>
/// matters especially here: a recipient's activation link carries a single-use
/// secret in its query string, and no-referrer guarantees it cannot leak through
/// an outbound request.
/// </summary>
internal sealed class SecurityHeadersMiddleware(
    RequestDelegate next,
    IOptions<CardholderUiOptions> uiOptions)
{
    private const string ContentSecurityPolicyWithoutScripts =
        "default-src 'self'; " +
        "script-src 'none'; " +
        "style-src 'self'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "frame-src 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'none'; " +
        "object-src 'none'";

    private readonly string contentSecurityPolicy = uiOptions.Value.EnableJavaScriptEnhancements
        ? ContentSecurityPolicyWithoutScripts.Replace(
            "script-src 'none'",
            "script-src 'self'",
            StringComparison.Ordinal)
        : ContentSecurityPolicyWithoutScripts;

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        var isPaymentStatus = context.Request.Path.Value?
            .EndsWith("/pay/status", StringComparison.OrdinalIgnoreCase) == true;
        headers["Content-Security-Policy"] = isPaymentStatus
            ? contentSecurityPolicy.Replace(
                "frame-ancestors 'none'",
                "frame-ancestors 'self'",
                StringComparison.Ordinal)
            : contentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = isPaymentStatus ? "SAMEORIGIN" : "DENY";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";

        // Every page in this application is recipient-specific. Caching one in a
        // shared proxy or restoring it from the back-forward cache after sign-out
        // would show one person's card balance to the next.
        headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        headers["Pragma"] = "no-cache";

        return next(context);
    }
}
