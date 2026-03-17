using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaRiMerchant.Application.Abstractions;
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

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ISensitiveDataProtector, AesGcmSensitiveDataProtector>();
        services.AddScoped<IPasswordHasher, Argon2PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IKycDocumentStorage, LocalKycDocumentStorage>();
        services.AddScoped<DevelopmentDataSeeder>();

        return services;
    }
}
