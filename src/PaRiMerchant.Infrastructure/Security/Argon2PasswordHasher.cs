using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using PaRiMerchant.Application.Abstractions;

namespace PaRiMerchant.Infrastructure.Security;

public sealed class Argon2PasswordHasher(IOptions<SecurityOptions> options) : IPasswordHasher
{
    private readonly string _pepper = options.Value.PasswordPepper;

    public string Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Compute(value, salt);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string value, string hash)
    {
        var parts = hash.Split('.', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Compute(value, salt);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private byte[] Compute(string value, byte[] salt)
    {
        var argon = new Argon2id(Encoding.UTF8.GetBytes($"{value}{_pepper}"))
        {
            Salt = salt,
            DegreeOfParallelism = 4,
            Iterations = 3,
            MemorySize = 65536
        };

        return argon.GetBytes(32);
    }
}
