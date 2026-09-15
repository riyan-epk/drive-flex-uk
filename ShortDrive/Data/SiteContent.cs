using Microsoft.EntityFrameworkCore;
using ShortDrive.Models;

namespace ShortDrive.Data;

// Seeds the default content for the admin-editable pages. Only inserts a page if it does not
// already exist, so existing (admin-edited) content is never overwritten on restart.
public static class SiteContent
{
    public record Default(string Slug, string Title, string Html);

    public static readonly Default[] Defaults =
    [
        new("about", "About Drive-Flex", """
            <p>Drive-Flex provides flexible, short-term motor insurance for drivers across the United Kingdom.
            Whether you are borrowing a car, practising as a learner, moving a van for the day or bridging a
            gap between annual policies, we make it simple to get fully comprehensive cover in minutes — from
            one hour to twenty-eight days.</p>

            <h2>What we do</h2>
            <p>We combine live DVLA and MOT vehicle checks with instant quotes and secure card payment, so you
            can go from registration plate to a valid Certificate of Motor Insurance in under a minute. Every
            policy is registered on the Motor Insurance Database (MID) and your certificate is emailed to you
            straight away.</p>

            <h2>Underwriting</h2>
            <p>Our policies are underwritten by ERS (Syndicate 218 at Lloyd's). Drive-Flex is authorised and
            regulated by the Financial Conduct Authority (FCA Firm Reference: 612248).</p>
            """),

        new("contact", "Contact Us", """
            <p>Questions about a quote, your cover, or a certificate? Our UK support team is happy to help. The
            quickest way to reach us is by email and we aim to reply within one business day.</p>

            <h2>Email</h2>
            <p><a href="mailto:supportdriveflex@gmail.com">supportdriveflex@gmail.com</a></p>

            <h2>Support hours</h2>
            <p>Monday to Friday, 9:00am – 6:00pm (UK time). Messages sent outside these hours are answered the
            next working day.</p>

            <h2>Making a claim</h2>
            <p>If you need to report an incident, please see our <a href="/claims">claims page</a> for what to do
            and how to reach us.</p>

            <p>Drive-Flex is authorised and regulated by the Financial Conduct Authority (FCA Firm Reference: 612248).</p>
            """),

        new("privacy", "Privacy Policy", """
            <p><em>Last reviewed: please review this policy with a legal adviser before relying on it.</em></p>

            <p>This Privacy Policy explains how Drive-Flex ("we", "us") collects, uses and protects the personal
            information you provide when you obtain a quote or purchase a policy through this website.</p>

            <h2>Information we collect</h2>
            <ul>
              <li>Vehicle details (registration, make, model and related DVLA/MOT data).</li>
              <li>Driver details (name, date of birth, address, licence number and driving history).</li>
              <li>Contact details (email address) and payment information processed securely by our payment provider.</li>
            </ul>

            <h2>How we use it</h2>
            <p>We use your information to provide quotes, arrange and administer your insurance, register your
            policy on the Motor Insurance Database (MID), send your certificate and meet our legal and
            regulatory obligations.</p>

            <h2>Sharing</h2>
            <p>We share information only as needed with our underwriter, the MID, payment processors and where
            required by law. We do not sell your personal data.</p>

            <h2>Your rights</h2>
            <p>You have the right to access, correct or request deletion of your personal data. To exercise these
            rights, contact us at <a href="mailto:supportdriveflex@gmail.com">supportdriveflex@gmail.com</a>.</p>

            <p>Drive-Flex is authorised and regulated by the Financial Conduct Authority (FCA Firm Reference: 612248).</p>
            """),

        new("terms", "Terms of Business", """
            <p><em>Last reviewed: please review these terms with a legal adviser before relying on them.</em></p>

            <p>These Terms of Business set out the basis on which Drive-Flex provides temporary motor insurance
            through this website.</p>

            <h2>About our service</h2>
            <p>Drive-Flex arranges short-term motor insurance policies, underwritten by ERS (Syndicate 218 at
            Lloyd's). We are authorised and regulated by the Financial Conduct Authority (FCA Firm Reference:
            612248).</p>

            <h2>Your policy</h2>
            <p>Cover begins at the start time you select and lasts for the duration you choose (from one hour to
            twenty-eight days). Your Certificate of Motor Insurance sets out exactly what is covered. Please read
            it carefully and make sure the details are correct.</p>

            <h2>Your responsibilities</h2>
            <p>You must provide accurate information when obtaining a quote. Giving incorrect information may
            invalidate your cover. You must hold a valid UK driving licence and meet the eligibility criteria
            shown during the quote process.</p>

            <h2>Payment and cancellation</h2>
            <p>Premiums are payable in full at the time of purchase via our secure payment provider. Because cover
            is short-term and starts almost immediately, please contact us as soon as possible if you believe a
            policy was purchased in error.</p>

            <h2>Complaints</h2>
            <p>If you are unhappy with our service, contact us at
            <a href="mailto:supportdriveflex@gmail.com">supportdriveflex@gmail.com</a> and we will do our best to
            put things right.</p>
            """),
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        foreach (var d in Defaults)
        {
            var exists = await db.SitePages.AnyAsync(p => p.Slug == d.Slug, ct);
            if (!exists)
            {
                db.SitePages.Add(new SitePage
                {
                    Slug = d.Slug,
                    Title = d.Title,
                    ContentHtml = d.Html,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
