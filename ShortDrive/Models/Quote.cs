namespace ShortDrive.Models;

public class Quote
{
    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = default!;

    public int DriverId { get; set; }
    public Driver Driver { get; set; } = default!;

    public DateTime CoverStartUtc { get; set; }
    public int DurationHours { get; set; }

    public string Usage { get; set; } = "Personal";
    public string VehicleClass { get; set; } = "Car";

    public decimal BaseRate { get; set; }
    public decimal DurationMultiplier { get; set; }
    public decimal RiskMultiplier { get; set; }
    public decimal AddOnsTotal { get; set; }
    public decimal TotalPremium { get; set; }

    public bool AddOnExcessProtect { get; set; }
    public bool AddOnRoadside { get; set; }
    public bool AddOnLegal { get; set; }
    public bool AddOnComprehensiveUpgrade { get; set; }

    public string? PolicyNumber { get; set; }
    public DateTime? CertificateIssuedAt { get; set; }

    public QuoteStatus Status { get; set; } = QuoteStatus.Started;
    public string? StripeSessionId { get; set; }
    public string? CustomerEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
