#!/usr/bin/env bash
# =============================================================================
# Drive-Flex — one-shot server setup for AlmaLinux 10 / RHEL-family (dnf)
# Run on your HostWorld VPS as root, AFTER the source code is present.
# Installs .NET 9, Nginx, firewalld, certbot; handles SELinux; builds the app;
# creates the systemd service; configures Nginx on port 80.
# HTTPS (certbot) is the final command it prints (run once DNS points here).
#
#   Usage:  sudo bash setup-server.sh
#
# EDIT THE TWO VALUES BELOW before running.
# =============================================================================
set -euo pipefail

# ---- EDIT THESE ----------------------------------------------------------------
DOMAIN="yourdomain.com"          # your real domain (no https://, no www)
SRC_DIR="/root/drive-flex-uk"    # where the source code lives (git clone or upload target)
# --------------------------------------------------------------------------------

APP_DIR="/var/www/driveflex"
APP_USER="driveflex"
CSPROJ="$SRC_DIR/ShortDrive/ShortDrive.csproj"

echo "==> [1/9] Base packages (EPEL, git, nginx, firewalld, SELinux tools)"
dnf -y install epel-release || true
dnf -y install git wget curl nginx firewalld policycoreutils

echo "==> [2/9] Installing .NET SDK 9.0"
if ! dnf -y install dotnet-sdk-9.0; then
  echo "    Not in default repos; adding the Microsoft package feed..."
  rpm -Uvh https://packages.microsoft.com/config/rhel/10/packages-microsoft-prod.rpm || true
  dnf -y install dotnet-sdk-9.0
fi
DOTNET_BIN="$(command -v dotnet)"
echo "    dotnet at: $DOTNET_BIN"
"$DOTNET_BIN" --list-runtimes | grep -i aspnetcore || { echo "ASP.NET Core runtime missing"; exit 1; }

echo "==> [3/9] Installing certbot"
dnf -y install certbot python3-certbot-nginx

echo "==> [4/9] Firewall (allow SSH + web)"
systemctl enable --now firewalld
firewall-cmd --permanent --add-service=ssh   >/dev/null 2>&1 || true
firewall-cmd --permanent --add-service=http  >/dev/null 2>&1 || true
firewall-cmd --permanent --add-service=https >/dev/null 2>&1 || true
firewall-cmd --reload
firewall-cmd --list-services

echo "==> [5/9] SELinux: let Nginx connect to the app (prevents 502 Bad Gateway)"
setsebool -P httpd_can_network_connect 1 || true

echo "==> [6/9] Building the app -> $APP_DIR"
[ -f "$CSPROJ" ] || { echo "ERROR: $CSPROJ not found. Put the source at $SRC_DIR first (git clone or upload)."; exit 1; }
id -u "$APP_USER" >/dev/null 2>&1 || useradd --system --no-create-home --shell /sbin/nologin "$APP_USER"
mkdir -p "$APP_DIR"
"$DOTNET_BIN" publish "$CSPROJ" -c Release -o "$APP_DIR"

echo "==> [7/9] Creating the systemd service"
cat >/etc/systemd/system/driveflex.service <<EOF
[Unit]
Description=Drive-Flex Blazor Server app
After=network.target

[Service]
WorkingDirectory=$APP_DIR
ExecStart=$DOTNET_BIN $APP_DIR/ShortDrive.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=driveflex
User=$APP_USER
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
Environment=DOTNET_NOLOGO=true
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

[Install]
WantedBy=multi-user.target
EOF

# The app must be able to write its SQLite database in $APP_DIR
chown -R "$APP_USER":"$APP_USER" "$APP_DIR"

systemctl daemon-reload
systemctl enable driveflex
systemctl restart driveflex
sleep 3
systemctl --no-pager --full status driveflex | head -n 15 || true

echo "==> [8/9] Configuring Nginx (port 80) via /etc/nginx/conf.d/driveflex.conf"
cat >/etc/nginx/conf.d/driveflex.conf <<EOF
map \$http_upgrade \$connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN www.$DOMAIN;
    client_max_body_size 20M;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade    \$http_upgrade;
        proxy_set_header Connection \$connection_upgrade;
        proxy_set_header Host              \$host;
        proxy_set_header X-Real-IP         \$remote_addr;
        proxy_set_header X-Forwarded-For   \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_cache off;
        proxy_buffering off;
        proxy_read_timeout 100s;
    }
}
EOF
nginx -t
systemctl enable --now nginx
systemctl restart nginx

echo "==> [9/9] Done with server setup."
echo
echo "  The app is now live over HTTP at:  http://$DOMAIN   (once DNS points here)"
echo "  Quick local check:  curl -s http://127.0.0.1:5000/healthz   (should print: Healthy)"
echo
echo "  NEXT — enable HTTPS once 'nslookup $DOMAIN' returns this server's IP (191.101.59.65):"
echo "      certbot --nginx -d $DOMAIN -d www.$DOMAIN --agree-tos -m you@example.com --redirect --non-interactive"
echo
echo "  Then create /var/www/driveflex/appsettings.Production.json (secrets + Gmail SMTP)"
echo "  set owner:  chown $APP_USER:$APP_USER /var/www/driveflex/appsettings.Production.json"
echo "  and run:    systemctl restart driveflex"
