using System.Security.Cryptography;
using System.Text;

namespace Insurance.Api.Services;

public class EmailHashService(IConfiguration config)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(
        config["Auth:EmailHashSecret"] ?? throw new InvalidOperationException("Auth:EmailHashSecret is required"));

    /// <summary>
    /// Computes a deterministic HMAC-SHA256 of the email.
    /// The email is normalised (trimmed, lower-cased) before hashing so lookup
    /// is case-insensitive. The result is the only email-derived value stored in DB.
    /// </summary>
    public string Compute(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        using var hmac = new HMACSHA256(_key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
