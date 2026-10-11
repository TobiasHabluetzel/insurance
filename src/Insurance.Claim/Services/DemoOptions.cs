namespace Insurance.Claim.Services;

/// <summary>
/// Switches that exist for demonstrating the product, not for using it.
///
/// Read once at startup from configuration, the same way <see cref="BrandTheme"/>
/// is, so the shape is familiar and a demo-only feature cannot be turned on by a
/// query string or a cookie.
/// </summary>
/// <param name="managementView">
/// Shows the customer/management toggle on the claim pages.
///
/// Defaults to <c>false</c>. The management view deliberately exposes material a
/// claimant must not see — the insurer's coverage reasoning, the machine's
/// reading of their documents, and the behavioural signals gathered while they
/// filled the form. Off unless somebody deliberately turns it on, so it cannot
/// reach a real customer by being forgotten.
/// </param>
public class DemoOptions(bool managementView)
{
    public bool ManagementView { get; } = managementView;
}
