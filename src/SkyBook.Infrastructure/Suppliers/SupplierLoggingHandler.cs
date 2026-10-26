using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SkyBook.Application.Suppliers;
using SkyBook.Domain.Entities;
using SkyBook.Infrastructure.Persistence;

namespace SkyBook.Infrastructure.Suppliers;

/// <summary>
/// Records every supplier call (request, response, status, duration) in SupplierLogs with the current
/// correlation id, and forwards that id to the supplier. Passport numbers and dates of birth are masked.
/// </summary>
public sealed class SupplierLoggingHandler(string supplier, IServiceScopeFactory scopes) : DelegatingHandler
{
    private const int MaxBody = 8000;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var correlationId = CorrelationContext.Id ?? Guid.NewGuid().ToString("N");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var operation = Operation(request);
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, ct);
            await response.Content.LoadIntoBufferAsync();            // so we can read it and the caller still can
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            await SaveAsync(correlationId, operation, requestBody, responseBody, (int)response.StatusCode, sw);
            return response;
        }
        catch (Exception ex)
        {
            await SaveAsync(correlationId, operation, requestBody, $"{ex.GetType().Name}: {ex.Message}", 0, sw);
            throw;
        }
    }

    private static string Operation(HttpRequestMessage r)
    {
        if (r.Content?.Headers.Contains("SOAPAction") == true)
        {
            var action = r.Content.Headers.GetValues("SOAPAction").First().Trim('"');
            return action[(action.LastIndexOf('/') + 1)..];
        }
        return $"{r.Method} {r.RequestUri?.AbsolutePath}";
    }

    private async Task SaveAsync(string correlationId, string operation, string? req, string? res, int status, Stopwatch sw)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SkyBookDbContext>();
            db.SupplierLogs.Add(new SupplierLog
            {
                CorrelationId = correlationId, Supplier = supplier, Operation = operation,
                RequestBody = Trim(SensitiveDataMasker.Mask(req)), ResponseBody = Trim(SensitiveDataMasker.Mask(res)),
                HttpStatus = status, DurationMs = sw.ElapsedMilliseconds
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // diagnostics must never break a booking
        }
    }

    private static string? Trim(string? s) => s is { Length: > MaxBody } ? s[..MaxBody] + "…[truncated]" : s;
}
