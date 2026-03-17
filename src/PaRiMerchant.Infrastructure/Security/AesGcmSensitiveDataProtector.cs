using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PaRiMerchant.Application.Abstractions;

namespace PaRiMerchant.Infrastructure.Security;

public sealed class AesGcmSensitiveDataProtector(IOptions<SecurityOptions> options) : ISensitiveDataProtector
{
    private readonly byte[] _encryptionKey = Convert.FromBase64String(options.Value.EncryptionKey);
    private readonly byte[] _blindIndexKey = Convert.FromBase64String(options.Value.BlindIndexKey);

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(nonce, plaintextBytes, cipherBytes, tag);

        var payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);
        return Convert.ToBase64String(payload);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return string.Empty;
        }

        var payload = Convert.FromBase64String(ciphertext);
        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipherBytes = payload[28..];
        var plaintextBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Decrypt(nonce, cipherBytes, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public string ComputeBlindIndex(string plaintext)
    {
        using var hmac = new HMACSHA256(_blindIndexKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plaintext.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash);
    }
}
