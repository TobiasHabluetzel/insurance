using System.ComponentModel.DataAnnotations;

namespace Insurance.Claim.Models;

public class PolicyEntryViewModel
{
    [Required]
    [Display(Name = "Policy number")]
    public string? Number { get; set; }

    /// <summary>Server-side error surfaced back to the view (policy not found, upstream failure).</summary>
    public string? Error { get; set; }
}
