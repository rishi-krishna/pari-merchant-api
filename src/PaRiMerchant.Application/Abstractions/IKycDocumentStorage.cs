namespace PaRiMerchant.Application.Abstractions;

public interface IKycDocumentStorage
{
    Task<StoredDocumentResult> SaveAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken);
}

public sealed record StoredDocumentResult(string StoragePath, string Sha256Hash, long SizeBytes);
