using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SkyBook.Infrastructure;
using SkyBook.Infrastructure.Persistence;
using SkyBook.Web.Data;
using SkyBook.Web.Infrastructure;
using SkyBook.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<SkyBookDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSkyBookSuppliers(builder.Configuration);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.Cookie.HttpOnly = true;
    });
builder.Services.AddAuthorization(o =>
    // every endpoint requires a signed-in user unless it opts out with [AllowAnonymous]
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddHostedService<HoldExpiryService>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // first, so every log line and supplier call carries the id
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    await DbSeeder.InitializeAsync(app.Services);
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();     // who are you?  (must come before authorization)
app.UseAuthorization();      // what may you do?

app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program { }   // lets integration tests reference Program
