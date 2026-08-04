using Microsoft.AspNetCore.Identity;

namespace Insurance.Api.Data;

public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>HMAC-SHA256(email.ToLowerInvariant(), EMAIL_HASH_SECRET). Lookup key — no PII stored.</summary>
    public string EmailHash { get; set; } = null!;

    /// <summary>Legacy: NIS ClientPerson.Id. No longer used; kept for backward compat.</summary>
    public int? NisClientId { get; set; }

    /// <summary>Insured.Id in the policy demo system. Populated on registration.</summary>
    public Guid? InsuredId { get; set; }
}
