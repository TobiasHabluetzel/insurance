namespace Insurance.Portal.Services;

/// <summary>
/// Server-side equivalent of the React project's VITE_DESIGN env var.
/// Same palette / naming as the degoudse claim wizard so the portal and
/// the wizard render consistently for any given tenant.
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

/// <summary>Read-only URLs the AngularJS dashboard controller needs to build outbound links.</summary>
public record PortalConfig(string ClaimAppUrl, string BotUrl, string ClaimEmail);
