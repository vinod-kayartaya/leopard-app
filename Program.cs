using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using admin_web.Data;
using Microsoft.EntityFrameworkCore;
using admin_web.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Certificate;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);

// Near the top, after builder creation
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    // Setup HTTP port
    serverOptions.ListenLocalhost(5000);
    
    // Setup HTTPS with client cert support
    serverOptions.ListenLocalhost(5001, listenOptions =>
    {
        listenOptions.UseHttps(httpsOptions =>
        {
            httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            httpsOptions.AllowAnyClientCertificate();
        });
    });
});

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpClient();
builder.Services.AddScoped<ICertificateService, EjbcaCertificateService>();

// Add certificate authentication
builder.Services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
    .AddCertificate(options =>
    {
        options.AllowedCertificateTypes = CertificateTypes.All;
        options.ValidateValidityPeriod = true;
        options.RevocationMode = X509RevocationMode.NoCheck;
        
        // Disable chain validation for testing
        options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
        
        options.Events = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = (context) =>
            {
                var loggerFactory = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("CertificateAuthentication");

                logger.LogInformation("Validating certificate: {SerialNumber}", 
                    context.ClientCertificate.SerialNumber);
                
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, 
                        context.ClientCertificate.SerialNumber, 
                        ClaimValueTypes.String, 
                        context.Options.ClaimsIssuer),
                };

                context.Principal = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, context.Scheme.Name));
                context.Success();
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = (context) =>
            {
                var loggerFactory = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("CertificateAuthentication");

                logger.LogError("Certificate authentication failed: {Exception}", 
                    context.Exception);
                return Task.CompletedTask;
            }
        };
    });

// Add authorization after authentication configuration
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CertificateRequired", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(CertificateAuthenticationDefaults.AuthenticationScheme)
              .RequireClaim(ClaimTypes.NameIdentifier));
});

// Add before authentication configuration
builder.Services.AddCertificateForwarding(options =>
{
    options.CertificateHeader = "X-ARR-ClientCert";
    options.HeaderConverter = headerValue =>
    {
        X509Certificate2? clientCertificate = null;
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            byte[] bytes = Convert.FromBase64String(headerValue);
            clientCertificate = new X509Certificate2(bytes);
        }
        return clientCertificate;
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseCertificateForwarding();
app.UseAuthentication();
app.UseAuthorization();

app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run(); 