namespace ShortDrive.Models;

public class PricingSettings
{
    public int Id { get; set; } = 1;

    // Base pricing
    public decimal OneHourRate { get; set; } = 8.00m;
    public decimal OneDayRate { get; set; } = 33.25m;
    public decimal SevenDayRate { get; set; } = 78.50m;
    public decimal TwentyEightDayRate { get; set; } = 185.00m;

    // Vehicle Class Multipliers
    public decimal CarMultiplier { get; set; } = 1.00m;
    public decimal VanMultiplier { get; set; } = 1.25m;
    public decimal BikeMultiplier { get; set; } = 0.90m;
    public decimal CamperMultiplier { get; set; } = 1.35m;

    // Usage Multipliers
    public decimal PersonalMultiplier { get; set; } = 1.00m;
    public decimal BusinessMultiplier { get; set; } = 1.20m;

    // Add-ons
    public decimal ExcessProtectPrice { get; set; } = 5.95m;
    public decimal RoadsideRecoveryPrice { get; set; } = 4.95m;
    public decimal LegalExpensesPrice { get; set; } = 2.49m;
    public decimal ComprehensiveUpgradePrice { get; set; } = 2.95m;

    // Underwriter & Regulatory info
    public string UnderwriterName { get; set; } = "ERS Syndicate 218 at Lloyd's";
    public string FcaFirmReference { get; set; } = "612248";

    // Stripe Config overrides
    public string? StripeSecretKey { get; set; }
    public string? StripePublishableKey { get; set; }
    public bool StripeTestMode { get; set; } = true;

    // DVSA/DVLA API overrides
    public string? DvsaClientId { get; set; }
    public string? DvsaClientSecret { get; set; }
    public string? DvsaApiKey { get; set; }

    // Admin password override (stored here so it persists with DB)
    public string? AdminPasswordHash { get; set; }

    // SMTP Email config
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpFromEmail { get; set; }
    public string? SmtpFromName { get; set; } = "Drive-Flex Insurance";
    public bool SmtpUseSsl { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
