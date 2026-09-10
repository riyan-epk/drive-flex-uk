# Drive-Flex — Full Deployment Guide (Namecheap domain + Windows Server RDP + IIS)

This is a complete, step-by-step guide to publish the site and go live. Follow it top to bottom.
Replace every `yourdomain.com` with your real domain and `<SERVER-PUBLIC-IP>` with your RDP's
public IP address (your RDP provider gives you this).

> **Legend:** "PowerShell (Admin)" = right-click Start → *Windows PowerShell (Admin)* on the server.
> Commands are copy-paste ready.

---

## Overview of what you'll do
1. Point your Namecheap domain at the server (DNS).
2. Prepare the Windows Server (install .NET runtime + IIS).
3. Copy the app to the server.
4. Configure your secrets (Stripe / DVLA / SMTP / admin password).
5. Create the IIS website.
6. Add HTTPS (free SSL certificate).
7. Enter + test your keys in the admin panel, do a test payment.
8. Go-live checklist.

---

## STEP 1 — Point your Namecheap domain at the server (do this first, DNS takes time to propagate)

1. Log in to **namecheap.com** → **Account → Domain List** → click **Manage** next to your domain.
2. Open the **Advanced DNS** tab.
3. Under **Host Records**, delete any default `CNAME Record` for `www` and the `URL Redirect`/parking
   records Namecheap added.
4. Add these two records (click **Add New Record**):

   | Type       | Host | Value                | TTL       |
   |------------|------|----------------------|-----------|
   | A Record   | `@`  | `<SERVER-PUBLIC-IP>` | Automatic |
   | A Record   | `www`| `<SERVER-PUBLIC-IP>` | Automatic |

5. Click the green **✓ Save All Changes**.
6. DNS can take 15 minutes to a few hours. Check progress from your own PC:
   ```
   nslookup yourdomain.com
   ```
   When it returns your server IP, DNS is ready. (You can continue with the other steps meanwhile.)

---

## STEP 2 — Connect to the server and install prerequisites

### 2a. Connect via RDP
On your local PC: Start → **Remote Desktop Connection** → enter `<SERVER-PUBLIC-IP>` → sign in with
the Administrator username/password from your RDP provider.

### 2b. Install IIS + WebSockets (Blazor Server needs WebSockets)
Open **PowerShell (Admin)** on the server and run:
```powershell
Install-WindowsFeature -Name Web-Server,Web-WebSockets,Web-Mgmt-Console -IncludeManagementTools
```

### 2c. Install the .NET 9 ASP.NET Core Hosting Bundle
This installs the runtime **and** the IIS module that runs the app.
1. On the server, open a browser and go to: **https://dotnet.microsoft.com/download/dotnet/9.0**
2. Under **ASP.NET Core Runtime 9.x → Windows**, download **Hosting Bundle**.
3. Run the installer → Next → Install.
4. Back in PowerShell (Admin), reload IIS so it picks up the module:
   ```powershell
   net stop was /y
   net start w3svc
   ```
5. Verify the runtime is installed:
   ```powershell
   dotnet --list-runtimes
   ```
   You should see `Microsoft.AspNetCore.App 9.x` and `Microsoft.NETCore.App 9.x`.

### 2d. Open the firewall for web traffic
```powershell
New-NetFirewallRule -DisplayName "HTTP-In"  -Direction Inbound -Protocol TCP -LocalPort 80  -Action Allow
New-NetFirewallRule -DisplayName "HTTPS-In" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```
> Also make sure ports **80 and 443** are open in your RDP provider's firewall/security-group panel.

---

## STEP 3 — Put the app on the server

Choose ONE option.

### Option A — Copy the pre-built files (simplest)
1. On **your local PC**, from the project folder, build the production output:
   ```
   dotnet publish ShortDrive/ShortDrive.csproj -c Release -o publish
   ```
2. Zip the `publish` folder.
3. In your RDP window, copy the zip across (drag-drop into the RDP session, or copy-paste — RDP
   redirects the clipboard), and extract it to **`C:\driveflex`** on the server.
   (So `C:\driveflex\ShortDrive.dll` and `C:\driveflex\web.config` exist.)

### Option B — Build on the server from GitHub
1. Install Git and the .NET 9 **SDK** on the server (SDK from the same download page as 2c).
2. In PowerShell:
   ```powershell
   cd C:\
   git clone https://github.com/riyan-epk/drive-flex-uk.git
   dotnet publish C:\drive-flex-uk\ShortDrive\ShortDrive.csproj -c Release -o C:\driveflex
   ```
   (It's a private repo — sign in / use a GitHub Personal Access Token when prompted.)

---

## STEP 4 — Configure your secrets on the server

Create the file **`C:\driveflex\appsettings.Production.json`** (Notepad is fine). This file lives ONLY
on the server and is never in git. Paste this and fill in your real values:

```json
{
  "App": { "BaseUrl": "https://yourdomain.com" },
  "Admin": { "Password": "CHOOSE-A-STRONG-ADMIN-PASSWORD" },
  "ConnectionStrings": { "DefaultConnection": "Data Source=C:\\driveflex\\driveflex.db" },
  "Stripe": {
    "PublishableKey": "pk_live_or_test_xxx",
    "SecretKey": "sk_live_or_test_xxx"
  },
  "Dvsa": {
    "ClientId": "your-dvsa-client-id",
    "ClientSecret": "your-dvsa-client-secret",
    "ApiKey": "your-dvsa-api-key",
    "TokenUrl": "https://login.microsoftonline.com/a455b827-244f-4c97-b5b4-ce5d13b4d00c/oauth2/v2.0/token",
    "Scope": "https://tapi.dvsa.gov.uk/.default"
  },
  "Smtp": {
    "Host": "smtp.your-provider.com",
    "Port": 587,
    "Username": "your-smtp-username",
    "Password": "your-smtp-password",
    "FromEmail": "noreply@yourdomain.com",
    "FromName": "Drive-Flex Insurance",
    "UseSsl": true
  }
}
```

> **You don't have to put Stripe/DVLA/SMTP here** — you can leave them blank and enter them in the
> Admin panel later (Step 7), which also has "Test connection" buttons. But `App:BaseUrl`,
> `Admin:Password` and `ConnectionStrings` are best set here.
>
> **Important — set the DB path** (`ConnectionStrings:DefaultConnection`) to an absolute path like
> `C:\\driveflex\\driveflex.db` (note the doubled backslashes in JSON) so the database always lives
> in a known, writable place.

### Tell the app to run in Production mode
Edit **`C:\driveflex\web.config`** and add an `environmentVariables` block inside `<aspNetCore ...>`
so it looks like this:
```xml
<aspNetCore processPath="dotnet" arguments=".\ShortDrive.dll" stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
  </environmentVariables>
</aspNetCore>
```
(Also create an empty folder `C:\driveflex\logs` so error logging can write there.)

---

## STEP 5 — Create the IIS website

### 5a. Give the app permission to write its database
The IIS app needs to create/write `driveflex.db`. In PowerShell (Admin):
```powershell
icacls "C:\driveflex" /grant "IIS AppPool\DriveFlex:(OI)(CI)M" /T
```
(You'll create the "DriveFlex" app pool next — run this again after 5b if it errors the first time.)

### 5b. Create the site in IIS Manager
1. Start → **Internet Information Services (IIS) Manager**.
2. Left tree: expand your server → right-click **Sites** → **Add Website**:
   - **Site name:** `DriveFlex`
   - **Physical path:** `C:\driveflex`
   - **Binding:** Type `http`, IP `All Unassigned`, Port `80`, **Host name:** `yourdomain.com`
   - Click **OK**.
3. Add the `www` binding too: select the site → **Bindings…** → **Add** → Type `http`, Port `80`,
   Host name `www.yourdomain.com` → OK.
4. Set the app pool to No Managed Code: left tree → **Application Pools** → click **DriveFlex** →
   **Basic Settings** → **.NET CLR version = No Managed Code** → OK.
5. Now (re)run the permission command from 5a if you hadn't:
   ```powershell
   icacls "C:\driveflex" /grant "IIS AppPool\DriveFlex:(OI)(CI)M" /T
   ```
6. Restart IIS:
   ```powershell
   iisreset
   ```

### 5c. First test (HTTP)
Once DNS (Step 1) has propagated, browse from any PC to **http://yourdomain.com** — the Drive-Flex
homepage should load. If it doesn't, see **Troubleshooting** at the end.

---

## STEP 6 — Add HTTPS (free SSL certificate)

Use **win-acme** (free Let's Encrypt certificates, auto-renewing). DNS must already point to the
server and port 80 must be open (Steps 1 & 2d).

1. On the server, download **win-acme** from **https://www.win-acme.com/** (get the ZIP), extract to
   e.g. `C:\win-acme`.
2. In PowerShell (Admin):
   ```powershell
   cd C:\win-acme
   .\wacs.exe
   ```
3. Choose: **N** (create new certificate, simple mode) → it lists your IIS bindings → pick the
   `DriveFlex` site (both `yourdomain.com` and `www.yourdomain.com`) → agree to terms → enter an
   email for renewal notices.
4. win-acme validates the domain, installs the certificate, **adds the HTTPS (443) binding to your
   IIS site automatically**, and sets up an auto-renewal scheduled task.
5. Browse to **https://yourdomain.com** — you should see the padlock.

### Force HTTP → HTTPS (recommended)
The app already calls `UseHttpsRedirection`. To be safe at the IIS layer, install the free
**URL Rewrite** module (https://www.iis.net/downloads/microsoft/url-rewrite) and add an HTTP→HTTPS
rule, or simply rely on the app's redirect. Optional.

---

## STEP 7 — Enter and test your integration keys

1. Go to **https://yourdomain.com/admin** → log in with the `Admin:Password` you set.
2. **Stripe & DVLA tab:**
   - Paste your **Stripe** Publishable + Secret keys → click **Test Stripe connection** → expect
     "Stripe secret key is valid". (Use `sk_live_…`/`pk_live_…` for real payments; `sk_test_…` to test.)
   - Paste your **DVLA/DVSA** Client ID, Client Secret, API Key → click **Test DVLA connection** →
     expect "OAuth token issued and API key accepted".
3. **Email tab:** enter your **SMTP** settings → put your own email in "send a test email to" →
   click **Send test email** → confirm it arrives → then **Save All Changes**.
4. Click **Save All Changes** to persist everything.

### Get your keys
- **Stripe:** dashboard.stripe.com → Developers → API keys. Toggle "Test mode" off for live keys.
- **DVLA/DVSA:** from the DVSA MOT History Trade API portal (the credentials you already have).
- **SMTP:** from your email provider (SendGrid / Brevo / Mailgun / Google Workspace / Microsoft 365).

---

## STEP 8 — Email deliverability DNS (so certificates don't go to spam)

At **Namecheap → Advanced DNS**, add the records your SMTP provider tells you to. Typical examples:

- **SPF** (TXT record), Host `@`:
  `v=spf1 include:sendgrid.net ~all`  (use *your* provider's include value)
- **DKIM** — your provider gives you one or more **CNAME** records (e.g. `s1._domainkey` → `...sendgrid.net`). Add them exactly as shown.
- **DMARC** (optional, TXT), Host `_dmarc`:
  `v=DMARC1; p=none; rua=mailto:you@yourdomain.com`

Send another test email from the admin panel and confirm it lands in the inbox.

---

## STEP 9 — Test a real payment

1. On **https://yourdomain.com**, run a full quote for a real UK plate.
2. At checkout use Stripe **test card** `4242 4242 4242 4242`, any future expiry, any CVC, any postcode.
3. After paying you should land on the certificate page, and (if SMTP is configured) receive the
   certificate email.
4. In `/admin` → **Policy Book**, the purchased policy should appear.
5. When happy, switch Stripe to your **live** keys (Step 7) for real customers.

---

## GO-LIVE CHECKLIST
- [ ] `nslookup yourdomain.com` returns your server IP.
- [ ] `http://yourdomain.com` loads; `https://yourdomain.com` shows the padlock.
- [ ] `/admin` login works with your production password (not the default).
- [ ] Stripe test ✅, DVLA test ✅, SMTP test email received ✅.
- [ ] `App:BaseUrl` = `https://yourdomain.com` (Stripe redirects back correctly).
- [ ] A full test payment completes and shows in the Policy Book.
- [ ] `C:\driveflex\driveflex.db` exists and is included in your backups.

---

## Updating the site later (new version)
1. Locally: `dotnet publish ShortDrive/ShortDrive.csproj -c Release -o publish`
2. On the server: In IIS Manager, **Stop** the DriveFlex site (so files unlock).
3. Copy the new `publish` files over `C:\driveflex` (keep your `appsettings.Production.json`,
   `web.config` env block, `logs\`, and `driveflex.db`).
4. **Start** the site. (Your database and settings are preserved.)

## Backups
- Back up **`C:\driveflex\driveflex.db`** regularly — it contains all quotes, policies and settings.
  You can copy it while the site runs; for a consistent copy, stop the site briefly.

---

## Troubleshooting
- **HTTP 500.30 / 500.19 / app won't start:** open `C:\driveflex\logs\stdout*.log` (enable
  `stdoutLogEnabled="true"` in web.config). Usually a missing runtime (rerun Step 2c) or a
  permissions issue on the folder (Step 5a).
- **Site loads but "database" errors / can't save:** the app pool identity can't write the DB —
  re-run the `icacls` grant (Step 5a) and confirm the DB path in `appsettings.Production.json`.
- **Blazor page connects then disconnects / "reconnecting":** WebSockets not enabled — rerun Step 2b
  and `iisreset`.
- **DVLA test says "temporary gateway/WAF block (403)":** that's IP rate-limiting from repeated
  calls, not a bad key — wait a few minutes and retry. Real lookups from normal traffic are fine.
- **Payment redirects to the wrong URL after paying:** `App:BaseUrl` is wrong — set it to your exact
  `https://yourdomain.com` and restart the site.
- **Certificate emails not arriving:** SMTP not saved, wrong port (use 587), or missing SPF/DKIM
  (Step 8). Use the admin "Send test email" button to see the exact error.
