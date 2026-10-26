using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using SkyBook.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using SkyBook.Application.Suppliers;
using SkyBook.Infrastructure.Bookings;
using SkyBook.Infrastructure.Queries;
using SkyBook.Infrastructure.Suppliers;

namespace SkyBook.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSkyBookSuppliers(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
        services.AddSingleton<IOfferCache, MemoryOfferCache>();
        services.AddScoped<BookingQueries>();

        AddSupplierClients(services, "SkyGDS", SkyGdsSupplier.ClientName, SkyGdsSupplier.NoRetryClientName,
            config["Suppliers:SkyGds:BaseUrl"] ?? "http://localhost:8080");
        AddSupplierClients(services, "AeroNDC", AeroNdcSupplier.ClientName, AeroNdcSupplier.NoRetryClientName,
            config["Suppliers:AeroNdc:BaseUrl"] ?? "http://localhost:8081");

        services.AddScoped<SkyGdsSupplier>();
        services.AddScoped<AeroNdcSupplier>();
        services.AddScoped<IFlightSupplier>(sp => sp.GetRequiredService<SkyGdsSupplier>());
        services.AddScoped<IFlightSupplier>(sp => sp.GetRequiredService<AeroNdcSupplier>());

        services.AddScoped<FlightSearchService>();
        services.AddScoped<FlightPricingService>();
        services.AddScoped<BookingService>();
        services.AddScoped<AdminService>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        return services;
    }

    // Two clients per supplier: one with retries (search, price) and one without (book, ticket, cancel).
    private static void AddSupplierClients(IServiceCollection services, string supplier,
        string safeName, string noRetryName, string baseUrl)
    {
        Configure(services.AddHttpClient(safeName), retry: true);
        Configure(services.AddHttpClient(noRetryName), retry: false);

        void Configure(IHttpClientBuilder b, bool retry)
        {
            b.ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri(baseUrl);
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            // added first = outermost: logs one row per logical call, after retries have finished
            .AddHttpMessageHandler(sp => new SupplierLoggingHandler(supplier, sp.GetRequiredService<IServiceScopeFactory>()))
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
                if (retry) o.Retry.MaxRetryAttempts = 2;
                else o.Retry.ShouldHandle = _ => ValueTask.FromResult(false);   // never retry non-idempotent calls
            });
        }
    }
}
