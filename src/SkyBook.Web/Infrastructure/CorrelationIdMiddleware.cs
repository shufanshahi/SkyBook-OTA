using Serilog.Context;
using SkyBook.Application.Suppliers;

namespace SkyBook.Web.Infrastructure;

/// <summary>Reads X-Correlation-Id (or makes one), exposes it to logging and supplier calls, and echoes it on the response.</summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) id = Guid.NewGuid().ToString("N");

        context.Items[Header] = id;
        context.Response.Headers[Header] = id;
        CorrelationContext.Id = id;                         // read by SupplierLoggingHandler
        using (LogContext.PushProperty("CorrelationId", id))
            await next(context);
    }
}
