# 👗 Stock Tracker — Multi-Store Fashion Inventory & Stock Monitoring Platform

[![Stock Tracker CI](https://github.com/enesakkuss/StockTracker/actions/workflows/ci.yml/badge.svg)](https://github.com/enesakkuss/StockTracker/actions/workflows/ci.yml)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker Ready](https://img.shields.io/badge/Docker-Production%20Ready-2496ED?logo=docker)](https://www.docker.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**Stock Tracker**, Türkiye ve global popüler moda ve giyim markalarındaki (Zara, Bershka, Mango, Massimo Dutti, Stradivarius, Oysho, Pull&Bear, H&M, Beymen, Network, Vakko, Koton, Mavi, Defacto, LC Waikiki, Penti vb.) tükenmiş ürün bedenlerini arka planda yüksek doğrulukla izleyen, stok girdiğinde Telegram üzerinden anında bildirim gönderen modern, ölçeklenebilir ve tam konteynerize bir SaaS platformudur.

---

## 🌟 Temel Özellikler

- **🛍️ 13+ Mağaza Adaptörü:** JSON-LD, Hybris API ve Headless Chromium (Playwright) entegrasyonu ile akıllı varyant denetimi.
- **⚡ Canlı Ürün Denetleyici (Inspector):** Herhangi bir ürün linkini yapıştırarak mevcut tüm beden ve stok durumlarını anında görüntüleme.
- **📱 Telegram Anlık Bildirimleri:** Stok açıldığı saniyede görsel ve ürün linkiyle birlikte Telegram bot mesajı. Token'lar veritabanında AES-256 ile şifrelenir.
- **🛡️ Kurumsal Güvenlik:** JWT Authentication, Refresh Token Rotation (RTR), Tek Tıkla Tüm Oturumlardan Çıkış (Revoke-All), IDOR koruması ve katı CSP / Security Headers.
- **💾 SQLite WAL & Kalıcı Depolama:** Sıfır kilitlenmeli (Zero-Locking) Write-Ahead Logging mimarisi ve tek komutla otomatik hot-backup.
- **🐳 Docker & Nginx Entegrasyonu:** Multi-stage derlenmiş optimize imaj, Nginx reverse proxy, Gzip sıkıştırma ve statik önbellekleme.
- **🧪 250+ Otomatik Test:** Kapsamlı birim, entegrasyon, IDOR, güvenlik ve Playwright Desktop/Mobile E2E test paketi.

---

## 🚀 Hızlı Başlangıç (Docker ile 1 Dakikada Çalıştırma)

### 1. Depoyu Klonlayın
```bash
git clone https://github.com/enesakkuss/StockTracker.git
cd StockTracker
```

### 2. Ortam Değişkenlerini Hazırlayın
```bash
cp .env.example .env
```
`.env` dosyasında `JWT_SECRET_KEY` ve `SECRET_PROTECTION_KEY` için güçlü rastgele anahtarlar tanımlayın (Örn: `openssl rand -base64 48`).

### 3. Docker Compose ile Başlatın
```bash
docker compose -f docker-compose.prod.yml up -d
```

Uygulamanız hazır! Tarayıcınızdan **[http://localhost](http://localhost)** adresine giderek kullanmaya başlayabilirsiniz.

---

## 🛠️ Yerel Geliştirme (Local Development)

### Gereksinimler
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Node.js & Playwright (Opsiyonel)

### Derleme & Test
```bash
# Projeyi derleyin
dotnet build StockTracker.sln --configuration Release

# Tüm test paketini (253 test) çalıştırın
dotnet test StockTracker.sln --configuration Release

# API sunucusunu başlatın
dotnet run --project src/StockTracker.Api
```

---

## 🏛️ Mimari Yapı

```
StockTracker/
├── src/
│   ├── StockTracker.Domain/          # Entity modelleri, Enum'lar ve Temel Arayüzler
│   ├── StockTracker.Application/     # DTO'lar, Servis Arayüzleri ve İş Mantığı
│   ├── StockTracker.Infrastructure/  # EF Core SQLite, Mağaza Adaptörleri, Telegram, Playwright
│   └── StockTracker.Api/             # ASP.NET Core Kestrel REST API & SPA Frontend (wwwroot)
├── tests/
│   └── StockTracker.Tests/           # 253 Adet XUnit, Integration ve Playwright E2E Testi
├── scripts/
│   ├── backup.sh                     # SQLite Hot-Backup Scripti (Zero-Locking)
│   └── restore.sh                    # Güvenli Geri Yükleme Scripti
├── docs/                             # Üretim, Dağıtım, Güvenlik ve Altyapı Kılavuzları
├── Dockerfile                        # Multi-stage .NET 8 + Headless Playwright Chromium
└── docker-compose.prod.yml           # Üretim Compose Yığını (API + Nginx Reverse Proxy)
```

---

## 📦 GitHub Container Registry (GHCR)

Bu depo GitHub Actions ile entegredir. `main` branch'ine yapılan her güncellemede Docker imajı otomatik olarak derlenip GHCR'ye yüklenir:

```bash
docker pull ghcr.io/enesakkuss/stocktracker:latest
docker run -d -p 5000:5000 ghcr.io/enesakkuss/stocktracker:latest
```

---

## 📄 Lisans
Bu proje [MIT Lisansı](LICENSE) altında lisanslanmıştır.
