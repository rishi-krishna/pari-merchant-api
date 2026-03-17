namespace PaRiMerchant.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string value);
    bool Verify(string value, string hash);
}
