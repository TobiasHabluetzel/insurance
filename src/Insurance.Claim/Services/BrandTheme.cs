namespace Insurance.Claim.Services;

/// <summary>
/// Server-side equivalent of the React project's VITE_DESIGN env var.
/// Chosen once at startup — swap Design=saltsys / Design=globalexcel to
/// re-brand. All three palettes are defined as CSS custom properties in
/// wwwroot/css/brand.css keyed by data-theme.
/// </summary>
public class BrandTheme(string design)
{
    public string Design { get; } = design;

    public string CompanyName => Design switch
    {
        "saltsys" => "Saltsys GmbH",
        "globalexcel" => "Global Excel",
        _ => "de Goudse",
    };

    public string Tagline => Design switch
    {
        "saltsys" => "intend. solve. evolve.",
        "globalexcel" => "Management Inc.",
        _ => "verzekeringen",
    };
}
