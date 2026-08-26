# Stock Tracker — Production Pre-Flight & Go-Live Checklist

Bu doküman, Stock Tracker platformunun canlıya çıkış öncesi doğrulanmış durumunu ve canlı Linux sunucusunda devreye alma sırasında izlenecek 18 adımlı sıralı kontrol listesini tanımlar.

---

## 1. 18 Adımlı Canlıya Çıkış Sıralaması (Sequential Deployment Steps)

- [ ] **1. Linux Server Provisioning:** Ubuntu 22.04 LTS / Debian 12 sunucu tahsisi, güncel güvenlik yamaları (`apt update && apt upgrade`).
- [ ] **2. Docker Installation:** Docker Engine 24+ ve Docker Compose v2/v5 kurulumu.
- [ ] **3. Firewall Configuration:** UFW üzerinden yalnızca 22 (SSH), 80 (HTTP) ve 443 (HTTPS) portlarının açılması; API 5000 portunun dış dünyaya kapalı tutulması.
- [ ] **4. Repository Deployment:** Kod tabanının `/var/www/stocktracker` dizinine klonlanması/aktarılması.
- [ ] **5. .env Configuration:** `.env.example` dosyasından `.env` oluşturularak güçlü `JWT_SECRET_KEY` ve `SECRET_PROTECTION_KEY` anahtarlarının atanması.
- [ ] **6. Docker Compose Config Validation:** `docker compose -f docker-compose.prod.yml config` komutu ile ortam değişkenlerinin ve YAML sözdiziminin doğrulanması.
- [ ] **7. Image Build:** `docker compose -f docker-compose.prod.yml build` ile `stocktracker:latest` imajının üretilmesi.
- [ ] **8. Container Startup:** `docker compose -f docker-compose.prod.yml up -d` ile servislerin (`stocktracker-api`, `stocktracker-proxy`) başlatılması.
- [ ] **9. Health Validation:** `GET /health/live`, `GET /health/ready` ve `GET /health` sorgularının 200 OK ve DB Healthy döndüğünün doğrulanması.
- [ ] **10. Database Validation:** `/var/data/stocktracker.db` SQLite dosyasının oluştuğunun, WAL modunun devrede olduğunun ve kalıcı volume bağlamasının çalıştığının teyidi.
- [ ] **11. DNS A Record:** Alan adı DNS sağlayıcısında (Cloudflare / Route53) `tracker.yourdomain.com` için sunucu Public IP adresini gösteren `A` kaydının oluşturulması.
- [ ] **12. SSL Certificate:** Certbot / Let's Encrypt ile alan adı için geçerli SSL sertifikasının alınması ve `/etc/letsencrypt` dizinine bağlanması.
- [ ] **13. HTTPS Validation:** Nginx üzerinden HTTP 80 -> HTTPS 443 301 yönlendirmesinin ve geçerli TLS sertifikasının doğrulanması.
- [ ] **14. External API Smoke Test:** Canlı domain üzerinden `/api/monitors` (401), `/api/users/me` (401), `/js/config.js` (200, billingEnabled: false) ve `/swagger` (404) kontrollerinin yapılması.
- [ ] **15. Backup Cron:** `scripts/backup.sh` dosyasının crontab'a (`0 3 * * *`) eklenerek günlük otomatik SQLite hot-backup alınmasının sağlanması.
- [ ] **16. Restore Drill:** Test amaçlı alınan yedeğin `scripts/restore.sh` ile staging DB'ye geri yüklenebildiğinin doğrulanması.
- [ ] **17. Monitoring & Log Review:** `docker logs --tail 100 stocktracker-api` çıktısında sıfır plaintext token sızıntısı ve sıfır döngüsel hata kontrolü.
- [ ] **18. Rollback Readiness:** Kritik hata anında `docs/ROLLBACK.md` adımları ile önceki sürüme 2 dakikada dönülebileceğinin teyit edilmesi.

---

## 2. Doğrulama Durum Özeti (Validation Status)

### A. Yerel & Docker Ortamında Kanıtlanmış Maddeler (VERIFIED)
- [x] **Security Headers & CSP:** `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy`, `Content-Security-Policy` ve `HSTS` aktif.
- [x] **Server Signature Removal:** Kestrel `Server` ve `X-Powered-By` başlıkları kaldırıldı.
- [x] **Swagger Production Gating:** Swagger arayüzü production ortamında varsayılan olarak kapalı (404).
- [x] **Request Body Limit:** Kestrel istek gövdesi maksimum 10 MB ile sınırlandırıldı.
- [x] **Authentication & Session Security:** JWT Access Token + Refresh Token Rotation (RTR), Revoke-All mekanizması.
- [x] **IDOR Protection:** `userId` kesinlikle JWT claim üzerinden authoritative okunur.
- [x] **Zero Secret Leakage:** Telegram bot token veritabanında AES-256 ile şifreli, API/arayüzde yalnızca maskeli (`1234••••••6789`).
- [x] **Billing Strict Inactive:** `config.js` içinde `features.billingEnabled: false` korunmakta; arayüzde hiçbir Upgrade / Satın Al / Checkout CTA'sı bulunmamaktadır.
- [x] **Rate Limiting:** Global (500/dk) ve Auth (100/dk) rate limiting kuralları devrede.
- [x] **SQLite Hot-Backup & Restore:** Çevrimiçi SQLite hot-backup ve geri yükleme akışının veri bütünlüğü kanıtlandı.
- [x] **Real Docker Runtime:** Docker Engine 29.7.2 üzerinde `stocktracker-api` (healthy) ve volume persistence (`/var/data`) kanıtlandı.
- [x] **Playwright Desktop & Mobile E2E:** 1440x900 ve 390x844 çözünürlüklerde tüm kullanıcı akışları hatasız tamamlandı.
- [x] **Release Build & Test:** `0 Uyarı, 0 Hata`, 252 Başarılı / 0 Başarısız Test.

### B. Canlı Sunucu Tahsisine Bağlı Maddeler (ENVIRONMENT DEPENDENT)
- [ ] **Canlı Domain & DNS A Kaydı:** Henüz canlı bir public IP/domain tahsis edilmedi.
- [ ] **Canlı Let's Encrypt SSL Sertifikası:** Canlı domain atandığında Certbot ile oluşturulacak.
- [ ] **Canlı Sunucu Güvenlik Duvarı (UFW):** Canlı Linux sunucusunda yapılandırılacak.
