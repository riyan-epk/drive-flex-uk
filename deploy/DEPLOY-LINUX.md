# Drive-Flex — Linux (AlmaLinux 10) Deployment Guide

Your HostWorld VPS runs **AlmaLinux 10.1** (a Red Hat–family Linux). We host the app the standard way:

```
Internet ──▶ Nginx (port 80/443, HTTPS)  ──▶  Kestrel (your app, 127.0.0.1:5000)
                                              managed by systemd (auto-restart)
```

Your server: **IP `191.101.59.65`**, login user `root` over SSH. The app runs under a dedicated
`driveflex` service account. Replace **`yourdomain.com`** everywhere with your real domain.

> This replaces the old `DEPLOYMENT.md`, which was written for Windows/IIS and does **not** apply here.
>
> ⚠️ **In the HostWorld portal, never use the "Applications → Install" tab** — it reinstalls/wipes the
> OS. Just connect over SSH (below). The VNC icon is a backup console; the power icon reboots the VPS.

---

## What you'll do (10–20 min of active work)
1. Point your domain at `191.101.59.65` (DNS).
2. Connect to the server with PuTTY.
3. Put the code on the server.
4. Run one setup script (installs everything, builds, starts the app).
5. Turn on HTTPS.
6. Set your secrets + **Gmail SMTP** for sending certificate emails.
7. Test and go live.

---

## STEP 1 — Point your domain at the server (do this first; DNS takes time)

At your domain registrar's DNS settings, add two **A records**:

| Type | Host  | Value           | TTL       |
|------|-------|-----------------|-----------|
| A    | `@`   | `191.101.59.65` | Automatic |
| A    | `www` | `191.101.59.65` | Automatic |

Delete any parking/redirect records the registrar added. Save.
From your own PC you can check progress:

```bash
nslookup yourdomain.com
```

When it returns `191.101.59.65`, DNS is ready. (Continue the steps below meanwhile.)

---

## STEP 2 — Connect to the server (PuTTY)

1. Download PuTTY (https://www.putty.org) and open it.
2. **Host Name:** `191.101.59.65`  → **Port:** `22` → **Open**.
3. Accept the security alert (first connection only).
4. Login as: `root` → password: the **root password you set when ordering** the VPS.
   (Typing shows nothing — that's normal. Press Enter.)

You're now at a `root@awaisraza:~#` prompt.

---

## STEP 3 — Put the code on the server

Pick **ONE** option.

### Option A — Clone from GitHub (best; makes updates a one-liner later)
Your repo is private, so GitHub needs a token instead of a password:

1. On github.com (signed in as the repo owner): **Settings → Developer settings →
   Personal access tokens → Tokens (classic) → Generate new token (classic)**.
   Tick the **`repo`** scope, generate, and **copy** the token (starts `ghp_...`).
2. On the server:
   ```bash
   cd /root
   git clone https://github.com/riyan-epk/drive-flex-uk.git
   ```
   When prompted: **Username** = your GitHub username, **Password** = paste the token.

### Option B — Upload from your Windows PC (no GitHub needed)
1. Install **WinSCP** (https://winscp.net). Connect: host `191.101.59.65`, user `root`,
   your root password, protocol **SFTP**.
2. Drag your project folder `C:\Users\SCS\Desktop\shortdrive` into `/root/` and rename it
   `drive-flex-uk` (so the path is `/root/drive-flex-uk/ShortDrive/ShortDrive.csproj`).

---

## STEP 4 — Run the setup script

The script `deploy/setup-server.sh` (in your project) installs .NET + Nginx + firewall,
builds the app, and starts it as a service.

1. Open it and set the two values at the top: `DOMAIN="yourdomain.com"` and
   `SRC_DIR="/root/drive-flex-uk"`. On the server you can edit with:
   ```bash
   nano /root/drive-flex-uk/deploy/setup-server.sh
   ```
   (Edit, then `Ctrl+O`, `Enter`, `Ctrl+X`.)
2. Run it:
   ```bash
   sudo bash /root/drive-flex-uk/deploy/setup-server.sh
   ```
3. When it finishes it prints a live URL and the exact HTTPS command for Step 5.

Check the app is running:
```bash
systemctl status driveflex        # should say "active (running)"
curl -s http://127.0.0.1:5000/healthz   # should print: Healthy
```

Once DNS (Step 1) has propagated, `http://yourdomain.com` shows the site.

---

## STEP 5 — Turn on HTTPS (free, auto-renewing)

Only after `nslookup yourdomain.com` returns `191.101.59.65`, run (use your real email):

```bash
certbot --nginx -d yourdomain.com -d www.yourdomain.com --agree-tos -m you@example.com --redirect --non-interactive
```

Certbot gets a Let's Encrypt certificate, edits Nginx to serve HTTPS, and forces
`http → https`. Visit **https://yourdomain.com** — you should see the padlock. It
auto-renews (a timer is installed).

---

## STEP 6 — Set your secrets + Gmail email

Create the production settings file (lives only on the server, never in git):

```bash
nano /var/www/driveflex/appsettings.Production.json
```

Paste this and fill in the real values (template also in `deploy/appsettings.Production.template.json`):

```json
{
  "App": { "BaseUrl": "https://yourdomain.com" },
  "Admin": { "Password": "CHOOSE-A-STRONG-ADMIN-PASSWORD" },
  "ConnectionStrings": { "DefaultConnection": "Data Source=/var/www/driveflex/driveflex.db" },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "youraddress@gmail.com",
    "Password": "your16charapppassword",
    "FromEmail": "youraddress@gmail.com",
    "FromName": "Drive-Flex Insurance",
    "UseSsl": true
  }
}
```

Save (`Ctrl+O`, `Enter`, `Ctrl+X`), fix ownership, and restart:
```bash
chown driveflex:driveflex /var/www/driveflex/appsettings.Production.json
systemctl restart driveflex
```

> **Stripe & DVLA keys:** you can leave them out of this file and enter them in the Admin
> panel instead (Step 7) — it has **Test connection** buttons. `App:BaseUrl`, `Admin:Password`
> and the DB path are best set here.

### Getting your Gmail App Password (required — a normal Gmail password will NOT work)
Gmail blocks plain-password SMTP. You need a **16-character App Password**, which requires
2-Step Verification:

1. Go to **https://myaccount.google.com/security** → turn on **2-Step Verification** (if not already).
2. Go to **https://myaccount.google.com/apppasswords**.
3. App name: type `Drive-Flex` → **Create**.
4. Google shows a **16-character code** like `abcd efgh ijkl mnop`.
   Use it as `Smtp:Password` **with the spaces removed** → `abcdefghijklmnop`.
5. `Username` and `FromEmail` = your full Gmail address (e.g. `youraddress@gmail.com`).

**Notes / limits:**
- Port **587** with `"UseSsl": true` is correct for Gmail (STARTTLS) — your app is already coded for this.
- Free Gmail sends up to ~**500 emails/day**; Google Workspace ~2000/day. Fine for launch.
- Gmail forces the "From" to your own address, so keep `FromEmail` = your Gmail address
  (a different `noreply@yourdomain.com` would be rewritten by Google).
- Cheap free alternative if you outgrow Gmail limits or want `@yourdomain.com` sending:
  Brevo / SendGrid / Mailgun (change only the `Smtp` block).

---

## STEP 7 — Enter & test integrations in the Admin panel

1. Go to **https://yourdomain.com/admin** → log in with your `Admin:Password`.
2. **Email tab:** confirm the SMTP fields, put your own email in "send test to" →
   **Send test email** → check it arrives (also check Spam the first time) → **Save**.
3. **Stripe & DVLA tab:** paste keys → **Test Stripe connection** / **Test DVLA connection**
   → expect success → **Save All Changes**.

---

## STEP 8 — One real test purchase
1. Run a full quote on the site for a real UK plate.
2. At Stripe checkout use test card `4242 4242 4242 4242`, any future expiry / CVC / postcode.
3. You should land on the certificate page **and receive the certificate email**.
4. In `/admin → Policy Book` the policy appears.
5. When happy, switch Stripe to **live** keys for real customers.

---

## Updating the site later
```bash
cd /root/drive-flex-uk && git pull            # (Option A users; Option B: re-upload files)
dotnet publish ShortDrive/ShortDrive.csproj -c Release -o /var/www/driveflex
chown -R driveflex:driveflex /var/www/driveflex
systemctl restart driveflex
```
Your `appsettings.Production.json` and `driveflex.db` are preserved.

## Backups
Back up the database regularly (it holds all quotes, policies and settings):
```bash
cp /var/www/driveflex/driveflex.db /root/driveflex-backup-$(date +%F).db
```

---

## Troubleshooting (AlmaLinux)
- **See app logs:** `journalctl -u driveflex -n 100 --no-pager` (add `-f` to follow live).
- **App won't start:** run it by hand to see the error:
  `sudo -u driveflex ASPNETCORE_ENVIRONMENT=Production dotnet /var/www/driveflex/ShortDrive.dll`
- **502 Bad Gateway:** either the app isn't running (`systemctl status driveflex` + logs above), **or**
  SELinux is blocking Nginx → app. Fix SELinux: `setsebool -P httpd_can_network_connect 1` then
  `systemctl restart nginx`. (The setup script already does this.)
- **Page loads then "Reconnecting…":** WebSockets not passing — confirm the `map`/`Upgrade`
  lines are in `/etc/nginx/conf.d/driveflex.conf`, then `nginx -t && systemctl restart nginx`.
- **Endless https redirect loop:** the `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` line is missing
  from the service — it's in the provided unit; `systemctl daemon-reload && systemctl restart driveflex`.
- **Can't save / database errors:** `chown -R driveflex:driveflex /var/www/driveflex`. If it persists,
  SELinux may be confining writes — check `journalctl` and `ausearch -m avc -ts recent`.
- **certbot fails:** DNS isn't pointing here yet (`nslookup yourdomain.com`) or ports are blocked
  (`firewall-cmd --list-services` should list `http` and `https`). Also check the HostWorld portal
  has no external firewall blocking 80/443. Fix, then re-run the certbot command.
- **Email not arriving:** wrong App Password (regenerate), 2-Step Verification off, or you used
  your normal Gmail password. The admin **Send test email** button shows the exact error.
- **Nginx test:** `nginx -t` after any config edit; `systemctl reload nginx` to apply.
- **Check SELinux mode:** `getenforce` (Enforcing is normal/fine with the fixes above).
