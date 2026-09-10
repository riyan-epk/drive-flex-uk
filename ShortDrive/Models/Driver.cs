using System.ComponentModel.DataAnnotations;

namespace ShortDrive.Models;

public class Driver
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string FirstName { get; set; } = default!;

    [Required, MaxLength(50)]
    public string LastName { get; set; } = default!;

    public DateOnly DateOfBirth { get; set; }

    [MaxLength(20)]
    public string LicenceNumber { get; set; } = default!;

    [MaxLength(100)]
    public string AddressLine1 { get; set; } = default!;

    [MaxLength(100)]
    public string? AddressLine2 { get; set; }

    [MaxLength(50)]
    public string City { get; set; } = default!;

    [MaxLength(10)]
    public string Postcode { get; set; } = default!;

    public int YearsLicenceHeld { get; set; }
    public int ClaimsLast5Years { get; set; }
    public int ConvictionsLast5Years { get; set; }

    public ICollection<Quote> Quotes { get; set; } = new List<Quote>();
}
