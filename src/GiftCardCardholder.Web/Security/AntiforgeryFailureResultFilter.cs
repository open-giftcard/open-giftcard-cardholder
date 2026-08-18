using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GiftCardCardholder.Web.Security;

/// <summary>
/// Replaces only ASP.NET Core's antiforgery failure result with a useful
/// browser recovery path. Application-authored empty 400 responses remain 400.
/// </summary>
internal sealed class AntiforgeryFailureResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Result is not AntiforgeryValidationFailedResult)
        {
            return;
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        context.HttpContext.Response.Headers.Location = "/session-expired";
        context.Result = new EmptyResult();
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
