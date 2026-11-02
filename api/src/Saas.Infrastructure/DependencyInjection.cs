using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Microsoft.AspNetCore.DataProtection;
using Saas.Application.Invoices;
using Saas.Infrastructure.Auth;
using Saas.Infrastructure.Invoices;
using Saas.Infrastructure.Notifications;
using Saas.Infrastructure.Payments;
using Saas.Infrastructure.Persistence;
using Saas.Infrastructure.Storage;

namespace Saas.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(connectionString, npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery).UseNetTopologySuite())
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddSingleton(new AppUrls(
            config["App:WebBaseUrl"] ?? "http://localhost:3700",
            config["App:ApiBaseUrl"] ?? "http://localhost:5080",
            config.GetSection("App:ProductWebUrls").Get<Dictionary<string, string>>()));

        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        // Password reset links work for 10 minutes, once (the security stamp changes on reset). Only reset uses this provider.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromMinutes(10));

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddScoped<AuthService>();
        services.AddScoped<PlatformAuthService>();
        services.AddScoped<PasswordService>();
        services.AddScoped<ViewAsService>();
        services.AddScoped<AccountActivityService>();

        services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
        if (string.IsNullOrWhiteSpace(config[$"{EmailOptions.Section}:BrevoApiKey"]))
            services.AddScoped<IEmailSender, LogEmailSender>();
        else
            services.AddHttpClient<IEmailSender, BrevoEmailSender>();
        if (config.GetValue("Notifications:DispatcherEnabled", true))
            services.AddHostedService<NotificationDispatcher>();

        services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
        if (string.Equals(config[$"{StorageOptions.Section}:Provider"], "R2", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorage, R2FileStorage>();
        else
            services.AddSingleton<IFileStorage>(sp => new LocalFileStorage(
                sp.GetRequiredService<IOptions<StorageOptions>>(), sp.GetRequiredService<AppUrls>(), contentRoot));

        services.Configure<PaystackOptions>(config.GetSection(PaystackOptions.Section));
        // A hung Paystack call gives up after 20 seconds (the default is 100) and reads as "Paystack didn't answer".
        services.AddHttpClient<PaystackClient>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton(sp => new Saas.Application.Billing.MerchantKeyRules(sp.GetRequiredService<IOptions<PaystackOptions>>().Value.AllowLocalFixtureKeys));
        services.AddScoped<IMerchantPayments>(sp => sp.GetRequiredService<PaystackClient>());
        services.AddScoped<PaystackGateway>();
        services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<PaystackGateway>());

        // Invoice Generator
        services.AddDataProtection().SetApplicationName("saas").PersistKeysToDbContext<AppDbContext>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddHttpClient("pdf-logo");
        services.AddSingleton<IDocumentPdfRenderer, QuestPdfRenderer>();
        services.AddHttpClient<IExchangeRateProvider, OpenErApiRates>(c => c.Timeout = TimeSpan.FromSeconds(10));
        if (config.GetValue("Jobs:InvoicesEnabled", true))
            services.AddHostedService<InvoiceJobsHostedService>();

        // Online Course Platform
        services.AddSingleton(config.GetSection("Platform").Get<Saas.Application.Auth.PlatformOptions>() ?? new());
        var media = config.GetSection(Saas.Application.Courses.MediaOptions.Section).Get<Saas.Application.Courses.MediaOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(media.SigningKey)) media.SigningKey = "media:" + config["Jwt:SigningKey"]; // separate purpose, same secret store
        services.AddSingleton(media);
        services.AddSingleton<Saas.Application.Courses.MediaSigner>();
        services.AddSingleton<Saas.Application.Courses.ICertificateRenderer, Courses.CertificateRenderer>();
        services.Configure<Courses.VideoOptions>(config.GetSection(Courses.VideoOptions.Section));
        if (config.GetValue("Video:ProcessorEnabled", true))
            services.AddHostedService<Courses.VideoProcessor>();

        // Property Listing Website
        services.AddSingleton<Saas.Application.Property.IListingTokens, Property.ListingTokens>();
        if (config.GetValue("Jobs:PropertyEnabled", true))
            services.AddHostedService<Property.PropertyJobsHostedService>();
        if (config.GetValue("Photos:ProcessorEnabled", true))
            services.AddHostedService<Property.PhotoProcessor>();

        // SchoolLink
        services.AddScoped<Schools.StudentLoginService>();
        if (config.GetValue("Jobs:SchoolEnabled", true))
            services.AddHostedService<Schools.SchoolJobsHostedService>();
        services.AddSingleton<Saas.Application.School.IStudentPhotoProcessor, Schools.StudentPhotoProcessor>();
        services.AddSingleton<Saas.Application.School.ISchoolPdf, Schools.SchoolPdf>();
        services.AddSingleton<Saas.Application.Care.ICarePdf, Care.CarePdf>();
        if (config.GetValue("Jobs:CareEnabled", true))
            services.AddHostedService<Care.CareJobsHostedService>();
        services.AddSingleton<Saas.Application.School.ISpreadsheetReader, Schools.SpreadsheetReader>();
        services.AddSingleton<Saas.Application.School.Imports.IImportTemplateWriter, Schools.ImportTemplateWriter>();

        // Booking System
        services.AddSingleton<IDbErrors, Bookings.PostgresDbErrors>();
        services.AddSingleton<IKeyedHash>(new Auth.HmacKeyedHash(config["Jwt:SigningKey"] ?? ""));
        if (config.GetValue("Jobs:BookingsEnabled", true))
            services.AddHostedService<Bookings.BookingJobsHostedService>();

        return services;
    }
}
