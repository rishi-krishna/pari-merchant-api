using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Application.Payments;
using PaRiMerchant.Infrastructure.Persistence;
using PaRiMerchant.Infrastructure.Security;
using PaRiMerchant.Infrastructure.Seeding;
using PaRiMerchant.Infrastructure.Storage;

namespace PaRiMerchant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<CashfreeOptions>(configuration.GetSection(CashfreeOptions.SectionName));

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
        var environmentName = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"];
        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            var connectionBuilder = new MySqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(connectionBuilder.Server) ||
                connectionBuilder.Server.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                connectionBuilder.Server.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Production is using a localhost MySQL connection. Set Render env var " +
                    "'ConnectionStrings__DefaultConnection' to your cloud MySQL connection string.");
            }
        }

        services.AddDbContext<AppDbContext>(options =>
            // Pin the provider version so cloud startup/login doesn't depend on an
            // extra AutoDetect connection round-trip before EF can build the context.
            options.UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 0, 45)),
                mySqlOptions => mySqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ISensitiveDataProtector, AesGcmSensitiveDataProtector>();
        services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IKycDocumentStorage, LocalKycDocumentStorage>();
        services.AddScoped<DevelopmentDataSeeder>();

        return services;
    }
}
