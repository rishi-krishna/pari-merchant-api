using Microsoft.Extensions.DependencyInjection;
using PaRiMerchant.Application.Auth;
using PaRiMerchant.Application.Beneficiaries;
using PaRiMerchant.Application.Contacts;
using PaRiMerchant.Application.Kyc;
using PaRiMerchant.Application.Payments;
using PaRiMerchant.Application.Transactions;
using PaRiMerchant.Application.Wallet;

namespace PaRiMerchant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<ContactService>();
        services.AddScoped<BeneficiaryService>();
        services.AddScoped<KycService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<WalletService>();
        services.AddScoped<TransactionService>();
        return services;
    }
}
