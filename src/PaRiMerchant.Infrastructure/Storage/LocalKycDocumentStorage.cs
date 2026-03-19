using System.Security.Cryptography;
using PaRiMerchant.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace PaRiMerchant.Infrastructure.Storage;

public sealed class LocalKycDocumentStorage(IOptions<StorageOptions> options) : IKycDocumentStorage
{
    private readonly string _rootPath = options.Value.RootPath;

    public async Task<StoredDocumentResult> SaveAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootPath);
        var folder = Path.Combine(_rootPath, "kyc", DateTime.UtcNow.ToString("yyyyMMdd"));
        Directory.CreateDirectory(folder);

        var extension = Path.GetExtension(fileName);
        var storageFileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(folder, storageFileName);

        await using var fileStream = File.Create(filePath);
        using var sha = SHA256.Create();
        await using var crypto = new CryptoStream(fileStream, sha, CryptoStreamMode.Write);
        await content.CopyToAsync(crypto, cancellationToken);
        await crypto.FlushAsync(cancellationToken);

        var hash = Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
        var info = new FileInfo(filePath);
        return new StoredDocumentResult(filePath, hash, info.Length);
    }
}
