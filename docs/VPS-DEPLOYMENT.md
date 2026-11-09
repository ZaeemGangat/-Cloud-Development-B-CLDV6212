# Deploying CoffeeNChill to a Linux VPS

This walks through putting the Part 3 stack (gateway + Functions + Azurite) on a
fresh Ubuntu server and exposing Swagger at `http://<vps-ip>:5000/swagger`.

> Commands were written for **Ubuntu 22.04 / 24.04 LTS**. Any provider works
> (DigitalOcean, Azure VM, AWS EC2/Lightsail, Linode, Oracle Cloud, or a spare x64
> Linux box). Check current pricing and free/student credits yourself - they change often.
>
> **Raspberry Pi / ARM:** the pipeline builds x64 (linux/amd64) images, and the Azure
> Functions base image may not be published for ARM. Prefer an x64 server.

## 0. Pick a server size

| Resource | Minimum | Why |
|---|---|---|
| RAM | **2 GB** (1 GB works with swap - see step 2) | Azure Functions host ≈ 300-400 MB, gateway ≈ 100 MB, Azurite ≈ 80 MB |
| CPU | 1 vCPU | |
| Disk | 20 GB | Docker images ≈ 1.5 GB |
| OS | Ubuntu 22.04/24.04 **x64** | Images are built for linux/amd64 |

Open these inbound ports in the provider's firewall / security group:
**22** (SSH) and **5000** (gateway). Do *not* open 10000-10002 or 7071.

> **Render** runs single containers, not `docker compose` stacks, so it does not
> fit the "docker-compose up -d on the VPS" requirement. Use a real VM.

## 1. Log in and create a deploy user

```bash
ssh root@<vps-ip>

adduser deploy                 # choose a password
usermod -aG sudo deploy
rsync --archive --chown=deploy:deploy ~/.ssh /home/deploy   # reuse your SSH key
exit

ssh deploy@<vps-ip>
```

## 2. (1 GB servers only) add swap

```bash
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

## 3. Install Docker Engine + Docker Compose

```bash
sudo apt-get update && sudo apt-get -y upgrade
curl -fsSL https://get.docker.com | sudo sh        # Docker Engine + compose plugin
sudo usermod -aG docker $USER && newgrp docker      # run docker without sudo

docker --version
docker compose version
```

`docker compose` (with a space) is the current Compose v2 plugin; it accepts the
same commands as the older `docker-compose` binary.

## 4. Firewall (UFW)

```bash
sudo ufw allow OpenSSH
sudo ufw allow 5000/tcp
sudo ufw enable
sudo ufw status
```

> Docker publishes ports through its own iptables rules, so only *publish* ports
> you want public. `docker-compose.yml` publishes only the gateway (5000).

## 5. Copy the stack and configure secrets

From your own PC (in the repo folder):

```bash
ssh deploy@<vps-ip> "mkdir -p ~/coffeenchill"
scp docker-compose.yml .env.example deploy@<vps-ip>:~/coffeenchill/
```

On the VPS:

```bash
cd ~/coffeenchill
cp .env.example .env
openssl rand -base64 48            # copy the output into JWT_KEY
nano .env                          # set JWT_KEY and SEED_ADMIN_PASSWORD
chmod 600 .env
```

## 6. Start it (pulls the published images from Docker Hub)

```bash
docker compose pull
docker compose up -d
docker compose ps                  # all three services "Up", gateway "(healthy)"
docker compose logs -f gateway     # Ctrl+C to stop following
curl http://localhost:5000/health
```

Open **http://\<vps-ip\>:5000/swagger** in a browser. Put this URL in the README.

## 7. Demo checklist (for the YouTube video)

1. Show `docker compose ps` on the VPS and the public Swagger URL in the browser.
2. `POST /api/auth/login` with the seed Admin → copy `token` → **Authorize**.
3. `POST /api/auth/register` a Staff account (role `Staff`) using the Admin token.
4. Log out (Authorize → Logout), call `GET /api/menu` → 200 (public),
   `POST /api/menu` → **401**.
5. Register/login a Customer → Authorize with that token → `POST /api/menu` → **403**.
6. Login as Staff → Authorize → create/update menu items, upload a document, list orders.
7. `POST /api/orders/queue` (no token) → track it with `GET /api/orders/{date}/{id}` and
   watch it move Received → Preparing → Ready (`docker compose logs -f functions`).
8. As Staff, `PUT /api/orders/status` → Collected.
9. Run the Postman collection against the VPS (`baseUrl = http://<vps-ip>:5000`).

## 8. Updating

Manually:

```bash
cd ~/coffeenchill && docker compose pull && docker compose up -d
```

Or automatically via the GitHub Actions pipeline (`.github/workflows/deploy.yml`):
add the `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `DOCKERHUB_USERNAME` and
`DOCKERHUB_TOKEN` repository secrets and push to `main`.

For the pipeline's SSH key, create a dedicated key pair **on your PC**:

```bash
ssh-keygen -t ed25519 -f coffeenchill_deploy -N ""
ssh-copy-id -i coffeenchill_deploy.pub deploy@<vps-ip>
# paste the contents of coffeenchill_deploy (the PRIVATE key) into the VPS_SSH_KEY secret
```

## 9. After the video - lock it down

Pick one:

```bash
# Close public access but keep the server
sudo ufw delete allow 5000/tcp

# Or hide Swagger only
sed -i 's/^SWAGGER_ENABLED=.*/SWAGGER_ENABLED=false/' .env && docker compose up -d

# Or stop everything (data stays in the azurite-data volume)
docker compose down
```

Then remove the `VPS_HOST` secret so the pipeline stops deploying (it will still
build, test and push images), and destroy the VPS in the provider's console if
you no longer need it, to stop billing.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Set JWT_KEY ... in .env` when starting | `.env` missing or `JWT_KEY` empty |
| Gateway restarts: "Jwt:Key must be at least 32 bytes" | Use a longer key (`openssl rand -base64 48`) |
| Swagger not reachable | Provider firewall/security group and `ufw` must allow 5000 |
| Orders stay "Received" | `docker compose logs functions` - the Functions host must be running |
| `401` on every protected call | Token expired (60 min) - log in again; paste only the token, not "Bearer" |
| `exec format error` | The server is ARM; the images are x64 - use an x64 VPS |
