using System.ComponentModel.DataAnnotations;

namespace ShortDrive.Models;

public class Vehicle
{
    public int Id { get; set; }

    [MaxLength(10)]
    public string Registration { get; set; } = default!;

    [MaxLength(60)]
    public string Make { get; set; } = default!;

    [MaxLength(100)]
    public string Model { get; set; } = default!;

    [MaxLength(30)]
    public string? Colour { get; set; }

    [MaxLength(20)]
    public string? FuelType { get; set; }

    public int? YearOfManufacture { get; set; }

    [MaxLength(20)]
    public string? EngineCapacityCc { get; set; }

    [MaxLength(20)]
    public string? FirstRegistered { get; set; }

    [MaxLength(60)]
    public string? BodyType { get; set; }

    [MaxLength(20)]
    public string? Co2Emissions { get; set; }

    [MaxLength(20)]
    public string? EuroClass { get; set; }

    [MaxLength(20)]
    public string? TypeApproval { get; set; }

    [MaxLength(30)]
    public string? TaxStatus { get; set; } = "Taxed";

    [MaxLength(30)]
    public string? TaxDueDate { get; set; }

    [MaxLength(30)]
    public string? MotStatus { get; set; } = "Valid";

    [MaxLength(30)]
    public string? MotExpiryDate { get; set; }

    public DateTime LookedUpAt { get; set; } = DateTime.UtcNow;

    public ICollection<Quote> Quotes { get; set; } = new List<Quote>();
}
