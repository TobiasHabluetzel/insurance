using System.Security.Cryptography;

namespace Insurance.Api.Services;

/// <summary>
/// Generates the human-readable claim reference (e.g. CLM-F9KX7T) we send
/// to AC as externalId. The claim record itself lives in the ClaimsOps
/// system — uniqueness is enforced there when its webhook upserts the row.
/// </summary>
public class ShortCodeService(IConfiguration config)
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
    private const int CodeLength = 6;

    public string Generate()
    {
        var prefix = config["App:ReferencePrefix"]?.Trim() ?? "CLM";
        return $"{prefix}-{RandomBlock(CodeLength)}";
    }

    private static string RandomBlock(int length)
    {
        var chars = new char[length];
        Span<byte> buf = stackalloc byte[1];
        var max = (byte)(byte.MaxValue - (byte.MaxValue + 1) % Alphabet.Length);
        for (var i = 0; i < length; i++)
        {
            byte b;
            do
            {
                RandomNumberGenerator.Fill(buf);
                b = buf[0];
            } while (b > max);
            chars[i] = Alphabet[b % Alphabet.Length];
        }
        return new string(chars);
    }
}
