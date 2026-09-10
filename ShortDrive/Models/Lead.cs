using System.ComponentModel.DataAnnotations;

namespace ShortDrive.Models;

public class Lead
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(10)]
    public string? Registration { get; set; }

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string? Source { get; set; }
}
