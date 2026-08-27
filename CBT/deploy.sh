#!/bin/bash
# ─────────────────────────────────────────────────────────────────────────────
# NCS CBT — VPS Deployment Script
# Run once on your VPS after uploading the project folder
# Usage: chmod +x deploy.sh && ./deploy.sh
# ─────────────────────────────────────────────────────────────────────────────

set -e  # stop on any error

echo ""
echo "======================================"
echo "  NCS CBT — Deployment Starting"
echo "======================================"
echo ""

# ── 1. Copy Nginx config ──────────────────────────────────────────────────────
echo "[1/6] Setting up Nginx config..."
sudo cp nginx/cbt.tlimc.net.conf /etc/nginx/sites-available/cbt.tlimc.net

# Enable site (create symlink if it doesn't exist)
if [ ! -f /etc/nginx/sites-enabled/cbt.tlimc.net ]; then
    sudo ln -s /etc/nginx/sites-available/cbt.tlimc.net /etc/nginx/sites-enabled/cbt.tlimc.net
    echo "    Nginx site enabled."
else
    echo "    Nginx site already enabled."
fi

# Test Nginx config
sudo nginx -t
echo "    Nginx config OK."

# ── 2. Get SSL certificate for cbt.tlimc.net ─────────────────────────────────
echo ""
echo "[2/6] Obtaining SSL certificate..."
if [ ! -d "/etc/letsencrypt/live/cbt.tlimc.net" ]; then
    sudo certbot certonly --nginx -d cbt.tlimc.net --non-interactive --agree-tos \
        --email admin@tlimc.net --redirect
    echo "    SSL certificate obtained."
else
    echo "    SSL certificate already exists, skipping."
fi

# ── 3. Reload Nginx ───────────────────────────────────────────────────────────
echo ""
echo "[3/6] Reloading Nginx..."
sudo systemctl reload nginx
echo "    Nginx reloaded."

# ── 4. Build and start Docker containers ─────────────────────────────────────
echo ""
echo "[4/6] Building and starting Docker containers..."
docker compose down --remove-orphans 2>/dev/null || true
docker compose up -d --build
echo "    Containers started."

# ── 5. Wait for app to be healthy ────────────────────────────────────────────
echo ""
echo "[5/6] Waiting for app to be ready..."
attempt=0
max=20
until curl -sf http://127.0.0.1:8080/Account/Login > /dev/null 2>&1; do
    attempt=$((attempt+1))
    if [ $attempt -ge $max ]; then
        echo "    WARNING: App didn't respond after ${max} attempts. Check logs:"
        echo "    docker compose logs web"
        break
    fi
    echo "    Waiting... ($attempt/$max)"
    sleep 5
done

if curl -sf http://127.0.0.1:8080/Account/Login > /dev/null 2>&1; then
    echo "    App is up!"
fi

# ── 6. Done ───────────────────────────────────────────────────────────────────
echo ""
echo "======================================"
echo "  Deployment Complete!"
echo "  Visit: https://cbt.tlimc.net"
echo "======================================"
echo ""
echo "Useful commands:"
echo "  View logs:       docker compose logs -f web"
echo "  Restart app:     docker compose restart web"
echo "  Stop all:        docker compose down"
echo "  Update & redeploy: git pull && ./deploy.sh"
echo ""
