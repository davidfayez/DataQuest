using DataVerification.Application.Common.Interfaces;
using DataVerification.Application.Features.Wallets;
using DataVerification.Infrastructure.Email;
using DataVerification.Infrastructure.Identity;
using DataVerification.Infrastructure.Persistence;
using DataVerification.Infrastructure.Services;
using DataVerification.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataVerification.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence and the supporting infrastructure services.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<ApplicationDbContext>(options => options
            .UseSqlServer(connectionString, sql => sql
                .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)
                .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null)));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<DataSeeder>();
        services.AddScoped<DemoApplicationSeeder>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<SendGridOptions>(configuration.GetSection(SendGridOptions.SectionName));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<WalletOptions>(configuration.GetSection(WalletOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IPasswordHashingService, PasswordHashingService>();
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<IEmailSettingsStore, EmailSettingsStore>();

        // The store is registered by its concrete type as well: the assistant needs the decrypted
        // key, which is deliberately absent from the interface the admin panel talks to.
        services.AddScoped<AiSettingsStore>();
        services.AddScoped<IAiSettingsStore>(sp => sp.GetRequiredService<AiSettingsStore>());
        services.AddHttpClient<IAiAssistant, GeminiAssistant>();

        services.Configure<GeolocationOptions>(configuration.GetSection(GeolocationOptions.SectionName));
        services.AddMemoryCache();
        services.AddHttpClient<IIpGeolocator, HttpIpGeolocator>();
        services.AddSingleton<IUserAgentReader, UserAgentReader>();
        services.AddScoped<IPasswordResetSettingsStore, PasswordResetSettingsStore>();
        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IApplicationNumberGenerator, ApplicationNumberGenerator>();
        services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();
        services.AddSingleton<IFileTypeValidator, FileTypeValidator>();
        services.AddSingleton<IUploadContentScanner, UploadContentScanner>();
        services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<IEmailRouting, Email.EmailRouting>();

        // Transport selection, in order of precedence:
        //   1. SendGrid — when enabled (production email provider).
        //   2. SMTP     — when enabled, for a self-hosted / local relay.
        //   3. Logging  — otherwise, so the full email path still runs in dev without a server.
        //
        // The API key is deliberately not part of this decision any more: it now lives in the
        // database and is set from the admin settings page, so it is not known at startup. The
        // sender resolves it per send and logs the message instead of delivering when there is none.
        var sendGridEnabled = configuration.GetValue<bool>($"{SendGridOptions.SectionName}:Enabled");
        var smtpEnabled = configuration.GetValue<bool>($"{SmtpOptions.SectionName}:Enabled");

        if (sendGridEnabled)
        {
            services.AddScoped<IEmailSender, SendGridEmailSender>();
        }
        else if (smtpEnabled)
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, LoggingEmailSender>();
        }

        return services;
    }
}
