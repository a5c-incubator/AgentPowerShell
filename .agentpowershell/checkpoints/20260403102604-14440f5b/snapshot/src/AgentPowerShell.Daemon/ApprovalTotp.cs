using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AgentPowerShell.Daemon;

public static class ApprovalTotp
{
    public static string GenerateCode(string secret, DateTimeOffset? timestamp = null) =>
        ComputeCode(secret, timestamp ?? DateTimeOffset.UtcNow);

    public static bool ValidateCode(string? code, string secret, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var now = timestamp ?? DateTimeOffset.UtcNow;
        foreach (var offset in new[] { -30, 0, 30 })
        {
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(code.Trim()),
                Encoding.ASCII.GetBytes(ComputeCode(secret, now.AddSeconds(offset)))))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeCode(string secret, DateTimeOffset timestamp)
    {
        var key = Convert.FromBase64String(secret);
        var counter = timestamp.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(counterBytes, counter);
        if (BitConverter.IsLittleEndian)
        {
            counterBytes.Reverse();
        }

        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }
}
