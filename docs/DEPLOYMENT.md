# Stock Tracker — Production Deployment Guide

Bu doküman, Stock Tracker sisteminin canlı (production) Linux sunucularına (Ubuntu 22.04 LTS / Debian 12 / Docker Container Host) güvenli, yüksek performanslı ve sürdürülebilir şekilde deploy edilmesi için standart operasyonel yönergeleri içerir.

---

## 1. Sistem Gereksinimleri & Standart Dizin Yapısı (Prerequisites)

- **Sunucu:** Ubuntu 22.04 LTS / Debian 12 (Minimum 2 vCPU, 4 GB RAM, 20 GB SSD)
- **Container Runtime:** Docker Engine 24+ ve Docker Compose v2/v5
- **Güvenlik Duvarı (UFW):** Yalnızca `22/tcp` (SSH), `80/tcp` (HTTP) ve `443/tcp` (HTTPS) dış dünyaya açık olmalıdır. **API 5000 portu kesinlikle dış internete açılmamalıdır (yalnızca localhost/internal network).**

### Standart Dizin Yapısı:
```
/opt/stocktracker/                      # Ana uygulama dizini
├── .env                                # Canlı ortam değişkenleri (Korumalı, 600)
├── .env.example                        # Ortam değişkenleri şablonu
├── docker-compose.prod.yml             # Üretim Docker Compose dosyası
├── Dockerfile                          # Multi-stage container tanımı
├── nginx.prod.conf                     # Üretim Nginx ters vekil yapılandırması
├── scripts/                            # Operasyonel scriptler (backup, restore)
└── docs/                               # Operasyonel dokümantasyon

/var/backups/stocktracker/              # Otomatik SQLite yedekleme dizini (chmod 700)
stocktracker_prod_data (/var/data)      # Docker kalıcı veri hacmi (SQLite & DataProtection)
```

---

## 2. Kriptografik Sır Üretimi & `.env` Yapılandırması

Canlı sunucuda hiçbir sır kaynak kodda tutulmaz. `.env` dosyası aşağıdaki gibi oluşturulmalı ve güçlü rastgele anahtarlar atanmalıdır:

```bash
cd /opt/stocktracker
cp .env.example .env
chmod 600 .env
```

### Güvenli Kriptografik Anahtar Üretimi:
```bash
# JWT_SECRET_KEY ve SECRET_PROTECTION_KEY için güçlü 64 karakter anahtar üretimi:
openssl rand -base64 48
```

### `.env` Dosyası İçeriği:
```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000

# openssl ile üretilen güçlü anahtarlar:
JWT_SECRET_KEY=YOUR_GENERATED_CRYPTOGRAPHIC_JWT_KEY_MIN_32_BYTES_LONG
SECRET_PROTECTION_KEY=YOUR_GENERATED_CRYPTOGRAPHIC_DATA_PROTECTION_KEY

JWT_ISSUER=StockTracker
JWT_AUDIENCE=StockTrackerUsers

ConnectionStrings__DefaultConnection=Data Source=/var/data/stocktracker.db;Cache=Shared
Cors__AllowedOrigins__0=https://tracker.yourdomain.com
StockMonitoring__WorkerIntervalSeconds=30
Browser__Headless=true
```

---

## 3. Adım Adım Canlıya Çıkış Sırası (Deployment Sequence)

```bash
# 1. Projeyi sunucuya çekin
cd /opt/stocktracker
git pull origin main

# 2. Ortam değişkenlerini yapılandırın (.env)
cp .env.example .env
nano .env

# 3. Docker Compose sözdizimini ve ortam değişkenlerini doğrulayın
docker compose -f docker-compose.prod.yml config

# 4. Production Docker imajını derleyin
docker compose -f docker-compose.prod.yml build

# 5. Konteynerleri arka planda başlatın
docker compose -f docker-compose.prod.yml up -d

# 6. Servis durumunu kontrol edin
docker compose -f docker-compose.prod.yml ps

# 7. Sağlık kontrollerini doğrulayın
curl -f http://127.0.0.1:5000/health/live
curl -f http://127.0.0.1:5000/health/ready
curl -f http://127.0.0.1:5000/health
```

---

## 4. Alan Adı, DNS & SSL/TLS (Let's Encrypt / Certbot)

1. **DNS Kaydı:** Domain DNS sağlayıcınızda (Cloudflare, Route53 vb.) `tracker.yourdomain.com` için sunucu Public IP adresini gösteren **A Kaydı** ekleyin.
2. **SSL Sertifikası:** Certbot ile alan adınız için geçerli TLS sertifikası üretin:
   ```bash
   sudo certbot certonly --standalone -d tracker.yourdomain.com
   ```
3. Nginx konteynerini sertifikalar ile birlikte yeniden başlatın:
   ```bash
   docker compose -f docker-compose.prod.yml restart reverse-proxy
   ```

---

## 5. Güvenlik Duvarı (UFW Firewall)

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```
*(API 5000 portu dış dünyaya açılmaz; yalnızca internal Docker network ve localhost üzerinden Nginx tarafından proxy edilir.)*

---

## 6. Günlük Otomatik Yedekleme (Backup Automation)

SQLite veritabanı hot-backup mekanizması için crontab kaydı:

```bash
# Crontab düzenleme
sudo crontab -e

# Her gece 03:00'te çalışan sıfır kilitlenmeli yedekleme:
0 3 * * * /opt/stocktracker/scripts/backup.sh /var/data/stocktracker.db /var/backups/stocktracker >> /var/log/stocktracker_backup.log 2>&1
```

---

## 7. Operasyonel İzleme & Log Yönetimi

```bash
# Tüm servislerin loglarını inceleme:
docker compose -f docker-compose.prod.yml logs --tail=100

# API servis loglarını canlı takip etme:
docker compose -f docker-compose.prod.yml logs -f stocktracker-api

# API servisini yeniden başlatma:
docker compose -f docker-compose.prod.yml restart stocktracker-api

# Çalışan servislerin durumunu listeleme:
docker compose -f docker-compose.prod.yml ps
```

---

## 8. Rollback Prosedürü

Kritik bir hata durumunda bir önceki stabil sürüme dönmek için [`docs/ROLLBACK.md`](file:///C:/Users/Enes/Desktop/Takip%20Botu/docs/ROLLBACK.md) dokümanındaki adımları izleyin.
