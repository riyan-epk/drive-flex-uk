using System.ComponentModel.DataAnnotations;

namespace ShortDrive.Models;

// An admin-editable content page (About, Privacy Policy, Terms of Business, Contact).
// Rendered on the public site at /{Slug} and edited from the admin panel's Content Pages tab.
public class SitePage
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string Slug { get; set; } = default!;   // e.g. "about", "privacy", "terms", "contact"

    [MaxLength(120)]
    public string Title { get; set; } = default!;

    // HTML body. Rendered as a MarkupString on the public page. Edited by the admin only.
    public string ContentHtml { get; set; } = default!;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
