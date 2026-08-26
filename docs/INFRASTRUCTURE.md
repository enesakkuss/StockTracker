# Stock Tracker — Production Infrastructure & Hosting Architecture

Bu doküman, Stock Tracker sisteminin canlı sunucu mimarisini, bileşenler arası veri akışını, ağ topolojisini ve servis yaşam döngüsünü açıklar.

---

## 1. Mimari Şeması (Architecture Diagram)

```
                       [ İNTERNET ]
                            │
                            ▼
              [ HTTPS Reverse Proxy / Nginx ]
             (Port 443 / SSL Terminasyonu / Gzip)
                            │
                            │ (HTTP / Unix Socket)
                            ▼
           [ ASP.NET Core Kestrel (StockTracker.Api) ]
       ┌────────────────────┼────────────────────┐
       ▼                    ▼                    ▼
[ Controllers ]    [ Security & Auth ]    [ Background Worker ]
 (REST API / SPA)   (JWT / DataProt.)    (StockMonitoringWorker)
       │                    │                    │
       └────────────────────┼────────────────────┘
                            ▼
              [ SQLite Database (WAL Mode) ]
               (/var/data/stocktracker.db)
                            │
                            ▼
                 [ Telegram Bot API ]
                 (Stok Değişim Bildirimleri)
```

---

## 2. Bileşen Rolleri & Sorumlulukları

### A) Reverse Proxy (Nginx / Caddy / Cloudflare)
- TLS/SSL sertifika yönetimi (Let's Encrypt / Certbot).
- HTTP -> HTTPS yönlendirmesi.
- Statik dosya önbellekleme (CSS, JS, Resimler).
- Gzip/Brotli sıkıştırma.
- Client IP ve Proto bilgilerinin aktarımı (`X-Forwarded-For`, `X-Forwarded-Proto`).
- `/health/live` ve `/health/ready` probe'larını iç network'e yönlendirme.

### B) ASP.NET Core Kestrel Uygulama Sunucusu
- SPA statik dosyalarını (`wwwroot`) ve API endpointlerini sunar.
- `SecurityHeadersMiddleware`: Strict Transport Security, CSP, X-Frame-Options, MIME Sniffing engelleme.
- `GlobalExceptionMiddleware`: Sıfır stack trace sızıntısı ve `X-Correlation-ID` korelasyonu.
- `RateLimiter`: Brute-force ve DDoS koruması (IP bazlı 500/dk global, 100/dk auth kotası).

### C) SQLite Veritabanı (WAL Mode & Concurrency)
- `PRAGMA journal_mode = WAL;` ile yazma ve okuma işlemleri birbirini kilitlemez.
- `PRAGMA busy_timeout = 5000;` ile yüksek eşzamanlılıkta kilit bekleme süresi optimize edilmiştir.
- Veritabanı ve DataProtection anahtarları `/var/data/` altında kalıcı volume'de saklanır.

### D) Background Worker (StockMonitoringWorker)
- Arka planda `StockMonitoring:WorkerIntervalSeconds` (varsayılan 30s) aralıklarla zamanı gelmiş takipleri kontrol eder.
- Web isteklerini bloklamaz; `CancellationToken` ile sunucu durdurulurken graceful shutdown yapar.
- Tekrarlı (duplicate) kontrolleri ve eşzamanlı çakışmaları engeller.

---

## 3. Ağ Portları & İletişim Protokolleri

| Bileşen | Dinlenen Port | Protokol | Erişim Kısıtı |
|---|---|---|---|
| Nginx Reverse Proxy | `80`, `443` | HTTP/1.1, HTTP/2 | Public Internet |
| StockTracker.Api | `5000` | HTTP/1.1 | Yalnızca `127.0.0.1` / Internal Docker Network |
| SQLite Engine | - | In-Process / File I/O | Yalnızca Api Process Kullanıcısı |
| Telegram API | `443` (Dışa doğru) | HTTPS | Egress to `api.telegram.org` |
| Store PDP Web | `443` (Dışa doğru) | HTTPS | Egress to Mağaza Siteleri |

---

## 4. Kaynak ve Boyutlandırma Kılavuzu (Sizing)

- **Temel SaaS (0 - 5.000 Aktif Takip):** 2 vCPU, 4 GB RAM, 20 GB SSD
- **Orta Ölçek (5.000 - 25.000 Aktif Takip):** 4 vCPU, 8 GB RAM, 50 GB SSD
- **Gelişmiş / Yüksek Yük (25.000+ Aktif Takip):** Ayrılmış scraping worker node'ları ve PostgreSQL kümesi.
