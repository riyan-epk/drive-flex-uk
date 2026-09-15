# Drive-Flex UK — Project Status & Handoff

> **Purpose of this file:** a self-contained record of the whole app, everything that has
> been done, how it was verified, and what still remains. If the chat is deleted, read this
> first — it tells you the full state so you can continue without re-discovering everything.
>
> **Last updated:** 2026-09-15

---

## SESSION UPDATE — 2026-09-15 (deployment + fixes)

**The app is deployed and live** on an **AlmaLinux 10 VPS** (HostWorld), IP `191.101.59.65`,
domain **drive-flex.uk**, behind Nginx + systemd (`driveflex` service) with Let's Encrypt HTTPS.
Deployment kit lives in `deploy/` (`DEPLOY-LINUX.md` = master guide, `setup-server.sh` = one-shot).
The repo-root `DEPLOYMENT.md` (Windows/IIS) does NOT apply — this server is Linux.

**Redeploy an update** (on the server): `systemctl stop driveflex` → `cd /root/drive-flex-uk && git pull && dotnet publish ShortDrive/ShortDrive.csproj -c Release -o /var/www/driveflex` → `chown -R driveflex:driveflex /var/www/driveflex && systemctl start driveflex`.

**Changes made this session (all committed):**
- **DVSA vehicle lookup fixed** — `DvsaClient` now maps the MOT History API fields correctly
  (engine `engineSize`, first-registered `registrationDate`, year from `manufactureDate`, MOT
  status/expiry + odometer from `motTests`). `EnrichFromMotHistory()` does this. The dossier UI
  (`QuoteWizard.razor`) now shows only fields the API actually returns; CO2/Euro/type-approval/road-tax
  were REMOVED because the MOT History API does not provide them (they require the separate **DVLA
  Vehicle Enquiry Service (VES)** API — not yet integrated; would need a VES key).
- **UI/UX** — quote wizard now prerenders (no blank flash), scrolls to top on each step change (JS
  `scrollTo`), fades between steps; fixed the black focus outline on the hero `<h1>`; animated nav-bar
  underline hover; smooth scrolling; reduced-motion support. New favicon (`wwwroot/favicon.svg`, blue "DF").
- **Editable content pages** — new `SitePage` entity + `SitePages` table (created at startup via
  `CREATE TABLE IF NOT EXISTS` because EnsureCreated won't alter an existing DB; seeded by
  `Data/SiteContent.cs`). Public pages `/about`, `/privacy`, `/terms`, `/contact` render from DB
  (`Components/Pages/SitePageView.razor`, one component, 4 `@page` routes, slug from URL path).
  Admin panel has a **Content Pages** tab to edit title + HTML per page (`QuoteService.GetPageAsync/
  GetAllPagesAsync/UpdatePageAsync`). `/claims` is a static designed page (not DB-editable yet).
  Contact email used throughout: **supportdriveflex@gmail.com**.
- **Email deliverability** — `SmtpEmailSender` now sends multipart (plain-text + HTML) with Reply-To
  (reduces spam). For real inbox delivery the user must add SPF/DKIM/DMARC — see below.
- **Security headers** middleware in `Program.cs` (X-Content-Type-Options, X-Frame-Options: DENY,
  Referrer-Policy, Permissions-Policy, CSP frame-ancestors).
- **Removed fake claims** — homepage "4.9 on Trustpilot" → "Secure Stripe Checkout". NOTE STILL TODO:
  homepage stats strip still shows unverified "Over 1.8m policies issued" / "58s median time to cover"
  — should be replaced with truthful copy (user was asked, awaiting confirmation).

**Outstanding / next session:**
- Gmail SMTP test emails land in **spam** until domain email auth is set up. Real fix: send from an
  `@drive-flex.uk` address via a provider with SPF/DKIM (Google Workspace, or Brevo/SendGrid free),
  add SPF + DKIM + DMARC DNS records at Namecheap. Free-Gmail sending will always be spam-prone.
- Optionally integrate DVLA VES API to fill CO2/tax/Euro/type-approval.
- Replace the fake homepage stats (1.8m policies, 58s) with truthful copy.
- Consider making `/claims` DB-editable too (same pattern as the other 4 pages).

---

## 1. What this app is

- **Drive-Flex UK** (project folder name `ShortDrive`) — an **ASP.NET Core .NET 9 Blazor Server**
  web app for **temporary UK motor insurance** quotes and purchase. It is NOT a rental/booking app.
- **Customer flow:** Home → enter vehicle reg (DVSA lookup) → usage (personal/business) →
  vehicle class (car/van/bike/camper) → driver details + cover duration → quote + optional
  add-ons → **Stripe Checkout** → payment success → **Certificate of Motor Insurance**
  (shown on screen + emailed) → done.
- **Admin panel** at `/admin` (hidden from public nav): pricing engine, add-ons, Stripe/DVLA
  keys, SMTP email settings, security (password), and a policy/quote book.

### Tech / layout
- .NET 9, Blazor Server (Interactive Server components), EF Core **InMemory** DB, Bootstrap CSS.
- Repo root: `C:\Users\SCS\Desktop\shortdrive\`
- Project: `ShortDrive/` (contains `Program.cs`, `Components/`, `Models/`, `Services/`, `Data/`, `wwwroot/`).
- **Git remote (private):** https://github.com/riyan-epk/drive-flex-uk (branch `main`).

### Key files
- `ShortDrive/Services/QuoteService.cs` — **central pricing engine** (`ComputePremium` / `ComputeBaseRate`) + quote persistence. Single source of truth for all prices.
- `ShortDrive/Services/DvsaClient.cs` — vehicle lookup (OAuth2 + MOT History API). Demo plate `AB12 CDE` is hardcoded (fake Vauxhall Astra) and does not hit the API.
- `ShortDrive/Services/PaymentService.cs` — Stripe Checkout session create + payment verify.
- `ShortDrive/Services/SmtpEmailSender.cs` — certificate email (falls back to logging if SMTP not set).
- `ShortDrive/Services/AdminAuthService.cs` — admin auth (PBKDF2 hash, lockout).
- `ShortDrive/Services/UkTime.cs` — UK (Europe/London) time handling.
- `ShortDrive/Components/Pages/Quote/QuoteWizard.razor` — the live 5-step wizard (the real one).
- `ShortDrive/Components/Pages/Quote/QuoteSuccess.razor` — payment verify + certificate.
- `ShortDrive/Components/Pages/Admin/AdminPanel.razor` + `AdminLogin.razor` — admin.

---

## 2. Credentials & configuration (current)

- **Admin password (dev): `DriveFlex2024!`** (in `ShortDrive/appsettings.Development.json`).
  In production `appsettings.json` it is **empty = admin login disabled** until set via env/secret.
- Secrets live ONLY in `ShortDrive/appsettings.Development.json` (git-ignored, NOT in the repo).
  `appsettings.json` (in repo) has empty placeholders.
- Config keys: `Admin:Password`, `App:BaseUrl`, `Stripe:SecretKey`/`PublishableKey`,
  `Dvsa:ClientId`/`ClientSecret`/`ApiKey`/`TokenUrl`/`Scope`, `Smtp:*`.
  All of these can also be set at runtime in the Admin panel (persisted, override config).

---

## 3. Live verification results (what was actually tested)

- **DVSA / DVLA API — WORKING.** OAuth token endpoint returned HTTP 200 + valid token
  (client id + secret valid); the MOT History vehicle endpoint accepted the token + API key
  (returned structured 404 for non-existent test plates — a bad key gives 403). Real UK plates
  return live data. Demo plate `AB12 CDE` is hardcoded and never calls the API.
- **Stripe — WORKING (test mode).** Secret key valid (`livemode:false`). The wizard created a
  real Checkout session and redirected to `checkout.stripe.com` showing the exact calculated
  amount (£54.28 in the test). Completing a payment needs a card entered on Stripe's page
  (test card `4242 4242 4242 4242`) — that manual step was not performed in-session.
- **Certificate email — MECHANISM PROVEN, BUT NOT ENABLED.** After payment the app calls the
  email sender; the actual `System.Net.Mail` delivery path was proven by capturing a full HTML
  certificate email with a local mail server. **However no SMTP is configured**, so currently
  the certificate is only logged to the server console — no email is delivered until SMTP is set.
- **Whole flow — WORKING** end-to-end except the two manual/prod steps: entering a test card to
  complete payment, and configuring SMTP for real email delivery.
- **Pricing** verified live: 3-day = £48.33 (was wrongly £78.50), add-ons and admin-set rates
  flow correctly; frontend price == backend price == Stripe amount.
- **Admin** verified: `/admin` protected (redirects to login), login → dashboard, pricing save,
  policy book, logout, session survives page refresh, protected again after logout.
- **Production build** verified: `dotnet publish -c Release` clean; runs in Production env;
  `/healthz`, `/`, `/quote`, `/admin` all 200; robots/sitemap served; dev secrets excluded from output.

---

## 4. Work completed (audit + fixes)

**Security**
- Fixed IDOR on `/quote/success` (certificates were viewable by guessing quote IDs).
- Admin password: was plaintext `==`; now PBKDF2 salted hash + constant-time compare + 5-try lockout.
- Wired the previously-unused vehicle-lookup rate limiter (5/min) into the live wizard.
- Added `noindex` on admin/certificate pages + `robots.txt` disallow.

**Critical functional bugs**
- Admin panel was **unreachable** (auth guard ran during prerender in a separate scope) — fixed
  by `prerender:false` on admin + success pages.
- Homepage quote widget was **dead** (missing `@rendermode`) — fixed (added InteractiveServer).
- Duplicate certificate emails / double Stripe verification (prerender ran init twice) — fixed.

**Pricing / business logic**
- 3-day (72h) cover was charged the full 7-day rate; centralized pricing into
  `QuoteService.ComputePremium` with per-day interpolation between the 4 configured tiers.
- `UpdatePricingSettingsAsync` silently dropped DVSA/SMTP/password fields — now copies all.
- Admin input clamping (no negative rates / zero multipliers).

**Validation / UX**
- Server-side validation: age ≥ 17, DOB in past, past/too-far start dates, required fields,
  re-checked before payment (UI can't be bypassed). Errors now display on every step.
- Certificate excess label fixed for the £250 Comprehensive Upgrade case.

**Remaining-items round (also done)**
- UK timezone handling via `UkTime` (cover times are UK-local, stored UTC, cert labelled "UK time").
- Admin session persists across full page refresh (encrypted ProtectedSessionStorage), cleared on logout.
- Duration label "24 Hours" → "1 Day".
- Removed dead code (`LoggingEmailSender`, 5 unused `Step*.razor` components).
- Keyboard accessibility on choice/add-on cards (role/tabindex/aria/Enter+Space).
- SEO: meta description + OG tags, `sitemap.xml`.
- Excluded `appsettings.Development.json` from publish; excluded `publish/` from MSBuild globs.

---

## 5. What REMAINS to do

### DONE since first status (2026-09-11, round 2)
- ✅ **DB switched to SQLite** — quotes/policies/settings now persist across restarts
  (`Program.cs` uses `UseSqlite`; file `driveflex.db`, git-ignored). Verified persistence.
- ✅ **Secrets configurable via Admin panel with live "Test connection" buttons** —
  Stripe (validates key), DVLA (validates OAuth + API key), SMTP (sends a test email).
  All three verified working. DVSA client now reads admin-entered creds (DB) with config fallback.

### Before real customers (your actions)
1. **Enter your production secrets in the Admin panel** (or env/`appsettings.Production.json`):
   Admin password, Stripe keys, DVLA creds, SMTP — and use the **Test connection** buttons to
   confirm each. Set `App:BaseUrl` to your real domain (needed for Stripe redirects).
2. **Configure SMTP** so certificates actually email (see §6), then hit "Send test email".
3. **Complete one real test payment** (Stripe test card `4242 4242 4242 4242`) on the deployed
   server to confirm the full pay → certificate path.
4. **Deploy to the RDP** and point the domain (see §7).

### Nice-to-have / lower priority
- Real Stripe **webhook** handling (currently payment is confirmed by verifying the session on
  the success page, which is fine, but a webhook is more robust for edge cases).
- Consider proper cookie-based auth if you want admin login to persist beyond a browser session.
- The demo plate `AB12 CDE` returns fake data — remove/replace before launch if undesired.
- Legal/footer links (`#terms`, `#privacy`, etc.) are placeholders — add real pages.

---

## 6. Enabling certificate emails (summary)

An RDP does NOT send email by itself. You need an **SMTP provider**:
- Recommended: **SendGrid / Brevo / Mailgun** (free tiers), or **Google Workspace / Microsoft 365** if you have business email.
- Get: host, port (587), username, password, from-address.
- Add **SPF + DKIM** DNS records for your sending domain (provider supplies them) so mail isn't spam.
- Enter in **Admin → Email (SMTP)** tab, click **Send test email** to confirm, then Save.
  Then purchases (and "Resend Email") deliver real certificates.
- Note: many VPS/RDP hosts block outbound port 25 — transactional providers use 587/465/2525.

**Test buttons** (Admin panel): Stripe & DVLA tab has "Test Stripe connection" and
"Test DVLA connection"; Email tab has "Send test email". Use these to verify each integration
before going live — no need to save first (tests use the values currently in the form).

---

## 7. Deploying on a Windows RDP (summary)

1. Install **ASP.NET Core 9 Hosting Bundle** on the RDP; enable the **WebSocket Protocol**
   Windows feature (Blazor Server needs WebSockets).
2. Get the app on the server — either copy the `publish` folder, or
   `git clone` the private repo and `dotnet publish ShortDrive/ShortDrive.csproj -c Release -o C:\driveflex`.
3. Create `C:\driveflex\appsettings.Production.json` with the real secrets (never commit it).
4. Run: quick test `dotnet ShortDrive.dll --urls http://0.0.0.0:5000`; production = host under
   **IIS** (the published `web.config` auto-launches it), add an **HTTPS (443)** binding with an
   SSL cert (win-acme/Let's Encrypt), open firewall 80/443.
5. Point your domain's **A record → RDP public IP**; set `App:BaseUrl=https://yourdomain` (used by Stripe redirects).

Full step-by-step is in `README.md` and was detailed in chat.

---

## 8. How to build / run / publish

```bash
# run locally
dotnet run --project ShortDrive/ShortDrive.csproj        # http://localhost:5166

# release build
dotnet build ShortDrive/ShortDrive.csproj -c Release

# production publish (artifact in ShortDrive/publish/)
dotnet publish ShortDrive/ShortDrive.csproj -c Release -o publish
```

Admin panel: `/admin` (login `/admin/login`). Dev password: `DriveFlex2024!`.

---

## 9. Known gotchas (don't re-break these)
- Admin/success pages MUST stay `@rendermode @(new InteractiveServerRenderMode(prerender: false))`
  — prerendering breaks admin auth (separate scope) and double-runs email sending.
- All pricing must go through `QuoteService.ComputePremium` — never duplicate pricing math in Razor.
- Never commit `appsettings.Development.json` (it holds real DVSA/Stripe/admin secrets).
- Cover times are UK-local for display/entry, stored as UTC — use `UkTime` helpers.
