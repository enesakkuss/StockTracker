# Stock Tracker — Production Rollback Procedure

Bu doküman, yeni bir deployment sonrasında beklenmedik bir kritik hata tespit edildiğinde sistemin bir önceki stabil sürüme en fazla 2 dakika içinde nasıl geri döndürüleceğini (rollback) adım adım açıklar.

---

## 1. Rollback Karar Kriterleri

Aşağıdaki durumlarda derhal rollback başlatılmalıdır:
- Deployment sonrası `/health/ready` veya `/health/live` endpointleri `503` veya `timeout` dönüyorsa.
- Kullanıcı giriş/kayıt akışlarında 500 hataları meydana geliyorsa.
- Background worker kilitleniyor veya döngüsel çökme (crash loop) yaşıyorsa.
- Frontend statik dosyaları yüklenemiyor veya UI işlevsiz kalıyorsa.

---

## 2. Docker Compose Ortamında Rollback

### A. Uygulama İmajı Rollback'i (Application Image Rollback)
Yeni imajda uygulama hatası varsa, Docker Compose dosyasında önceki stabil imaj etiketine dönün:

```bash
# 1. Compose dosyasındaki imaj etiketini önceki sürüme getirin
sed -i 's/stocktracker:latest/stocktracker:previous/g' docker-compose.prod.yml

# 2. Konteyneri yeniden ayağa kaldırın
docker compose -f docker-compose.prod.yml up -d --force-recreate stocktracker-api

# 3. Sağlık durumunu kontrol edin
curl -f http://localhost:5000/health/live
```

### B. Veritabanı Rollback'i (Database Rollback)
Eğer veritabanı migration'ı veya şema değişikliği veri bozulmasına neden olduysa, deployment öncesi otomatik alınan güvenlik yedeğini geri yükleyin:

```bash
# 1. API konteynerini durdurun
docker compose -f docker-compose.prod.yml stop stocktracker-api

# 2. Deployment öncesi yedeği kalıcı volume'e kopyalayın
docker run --rm -v stocktracker_prod_data:/var/data -v /var/backups/stocktracker:/backups alpine \
  cp /backups/stocktracker_predeploy_latest.db /var/data/stocktracker.db

# 3. Stale WAL dosyalarını temizleyin
docker run --rm -v stocktracker_prod_data:/var/data alpine \
  rm -f /var/data/stocktracker.db-wal /var/data/stocktracker.db-shm

# 4. API konteynerini başlatın ve veritabanı sağlığını doğrulayın
docker compose -f docker-compose.prod.yml start stocktracker-api
curl -f http://localhost:5000/health/ready
```

---

## 3. Systemd / Standalone Rollback

Standalone binary kurulumlarında rollback:

### Adım 1: Uygulama Servisini Durdurun
```bash
sudo systemctl stop stocktracker
```

### Adım 2: Önceki Binary Sürümünü Geri Yükleyin
Deployment öncesi `/var/www/stocktracker_previous/` olarak saklanan binary dosyalarını geri getirin:
```bash
cp -r /var/www/stocktracker_previous/* /var/www/stocktracker/
```

### Adım 3: Gerekirse Deployment Öncesi DB Yedeğine Dönün
```bash
/var/www/stocktracker/scripts/restore.sh /var/backups/stocktracker/stocktracker_predeploy_latest.db /var/data/stocktracker.db
```

### Adım 4: Servisi Başlatın ve Doğrulayın
```bash
sudo systemctl start stocktracker
sleep 3
curl -s http://127.0.0.1:5000/health/ready
```
