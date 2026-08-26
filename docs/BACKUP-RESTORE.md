# Stock Tracker — Backup & Disaster Recovery Guide

Bu doküman, Stock Tracker sisteminin canlı ortamda veritabanı yedekleme, saklama (retention) ve acil durum geri yükleme (disaster recovery / restore) prosedürlerini açıklar.

---

## 1. Yedekleme Stratejisi (Backup Strategy)

SQLite veritabanı çalışır durumdayken (hot backup) kilitlenme oluşturmadan `sqlite3 .backup` API'si ile yedeklenir.

### Otomatik Cron Yedekleme:
Her gece saat 03:00'te çalıştırılması önerilen cron tanımı:
```bash
0 3 * * * /var/www/stocktracker/scripts/backup.sh /var/data/stocktracker.db /var/backups/stocktracker >> /var/log/stocktracker_backup.log 2>&1
```

### Yedekleme Scripti Parametreleri:
- **Kaynak Veritabanı:** `/var/data/stocktracker.db`
- **Hedef Dizin:** `/var/backups/stocktracker/`
- **İsimlendirme Formatı:** `stocktracker_backup_YYYYMMDD_HHMMSS.db`
- **Saklama Süresi (Retention):** 14 gün (14 günden eski yedekler otomatik temizlenir)
- **Dosya İzinleri:** `chmod 600` (Yetkisiz okuma engellenir)

---

## 2. Geri Yükleme Prosedürü (Restore Procedure)

Bir arıza, veri bozulması veya yanlış işlem durumunda yedekten geri yükleme adımları:

### Adım 1: Servisi Durdurun
```bash
sudo systemctl stop stocktracker
# veya Docker kullanılıyorsa:
docker compose -f docker-compose.prod.yml stop stocktracker-api
```

### Adım 2: Restore Scriptini Çalıştırın
```bash
/var/www/stocktracker/scripts/restore.sh /var/backups/stocktracker/stocktracker_backup_20260825_030000.db /var/data/stocktracker.db
```

Script otomatik olarak:
1. Mevcut veritabanının güvenlik kopyasını alır (`stocktracker.db.pre_restore_timestamp`).
2. Yedek dosyasını hedef konuma kopyalar.
3. Varsa eski `-wal` ve `-shm` geçici dosyalarını temizler.
4. Dosya izinlerini (`640`) ayarlar.
5. Servisi yeniden başlatır.

### Adım 3: Sağlık Durumunu Doğrulayın
```bash
curl -i http://127.0.0.1:5000/health/ready
```
Yanıt `200 OK` ve `"status":"Healthy"` dönmelidir.
