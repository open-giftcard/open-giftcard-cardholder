using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace GiftCardCardholder.Tests.Fakes;

/// <summary>
/// TestServer leaves <c>Connection.RemoteIpAddress</c> null, so without this the
/// forwarded-address behaviour could not be exercised at all. Stamping a fixed
/// address at the very front of the pipeline lets the tests assert what this
/// application forwards, and that it ignores whatever the browser claimed.
/// </summary>
internal sealed class ClientAddressStartupFilter(IPAddress address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        builder =>
        {
            builder.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                await nextMiddleware();
            });
            next(builder);
        };
}
