namespace PaRiMerchant.Application.Abstractions;

public static class Masking
{
    public static string Phone(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
        {
            return "****";
        }

        return $"{new string('*', Math.Max(0, value.Length - 4))}{value[^4..]}";
    }

    public static string Email(string value)
    {
        var parts = value.Split('@');
        if (parts.Length != 2 || parts[0].Length < 2)
        {
            return "***";
        }

        return $"{parts[0][0]}***{parts[0][^1]}@{parts[1]}";
    }

    public static string AccountNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
        {
            return "****";
        }

        return $"{new string('*', Math.Max(0, value.Length - 4))}{value[^4..]}";
    }

    public static string Pan(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
        {
            return "****";
        }

        return $"{value[..2]}******{value[^2..]}";
    }
}
