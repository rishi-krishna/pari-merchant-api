namespace PaRiMerchant.Domain.Enums;

public enum TransactionStatus
{
    Initiated = 1,
    PendingProvider = 2,
    Processing = 3,
    Succeeded = 4,
    Failed = 5,
    Reversed = 6
}
