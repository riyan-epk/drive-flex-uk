# Drive-Flex UK — Temporary Motor Insurance

ASP.NET Core **.NET 9 Blazor Server** app for short-term UK motor insurance quotes and purchase.
Flow: Home → vehicle lookup (DVSA) → usage → class → driver + duration → quote + add-ons →
Stripe Checkout → certificate (on-screen + email) → admin panel.

## Run locally

```bash
dotnet run --project ShortDrive/ShortDrive.csproj
```

App: `http://localhost:5166`. Admin: `/admin` (hidden from public nav).

## Configuration

Secrets are **never committed**. `ShortDrive/appsettings.json` ships with empty values;
put real values in one of:

- `ShortDrive/appsettings.Development.json` (local dev only — git-ignored), or
- environment variables (production).

| Setting | Purpose |
|---|---|
| `Admin:Password` | Admin panel password. **Empty = admin login disabled.** |
| `App:BaseUrl` | Public site URL — used for Stripe success/cancel redirects. Must be your real domain. |
| `Stripe:SecretKey` / `Stripe:PublishableKey` | Stripe API keys (`sk_test_…` / `pk_test_…` for test). |
| `Dvsa:ClientId` / `ClientSecret` / `ApiKey` | DVSA MOT History API credentials for vehicle lookups. |
| `Smtp:Host` / `Username` / `Password` / `FromEmail` … | Email server for certificate delivery (see below). |

Stripe, DVSA, SMTP and the admin password can also be set at runtime in the **Admin panel**
(they persist in the settings store and override config).

## Certificate emails

After a confirmed payment the app emails the Certificate of Motor Insurance.
**If no SMTP is configured, the certificate is only logged to the console — no email is sent.**
Configure SMTP (Admin → Email tab, or the `Smtp:*` config keys) to enable real delivery. Example:

```
Smtp:Host = smtp.your-mail-server.com
Smtp:Port = 587
Smtp:Username = noreply@yourdomain.com
Smtp:Password = <mailbox or app password>
Smtp:FromEmail = noreply@yourdomain.com
Smtp:UseSsl = true
```

## Production build

```bash
dotnet publish ShortDrive/ShortDrive.csproj -c Release -o publish
```

`appsettings.Development.json` is excluded from the published output.

## Notes

- Database is EF Core **SQLite** (`driveflex.db`, git-ignored) — quotes, policies and
  admin-managed settings persist across restarts. Back up this file. The location comes from
  `ConnectionStrings:DefaultConnection`.
- Stripe, DVLA and SMTP credentials can be entered and **tested** in the Admin panel
  (Stripe & DVLA tab, Email tab) — "Test connection" / "Send test email" buttons validate them live.
- Pricing is centralized in `QuoteService.ComputePremium` (single source of truth).
- Admin/certificate pages are `noindex` and disallowed in `robots.txt`.
