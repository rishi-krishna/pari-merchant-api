namespace PaRiMerchant.Application.Abstractions;

public interface ISensitiveDataProtector
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    string ComputeBlindIndex(string plaintext);
}
