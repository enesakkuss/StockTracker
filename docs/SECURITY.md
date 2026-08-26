# Stock Tracker — Security Policy & Architecture

Bu doküman, Stock Tracker sisteminde uygulanan güvenlik mekanizmalarını, veri koruma prensiplerini ve operasyonel güvenlik standartlarını açıklar.

---

## 1. Kimlik Doğrulama & Yetkilendirme (Authentication & Authorization)

- **JWT Tabanlı Kimlik Doğrulama:**
  - `HMAC-SHA256` ile imzalanmış kısa ömürlü Access Token'lar kullanılır.
  - Token içinde kullanıcı ID'si `ClaimTypes.NameIdentifier` olarak authoritative şekilde tutulur.
  - Client tarafından gönderilen `userId` parametreleri dikkate alınmaz, JWT claim'i esastır.
- **Refresh Token Rotation:**
  - Her yenileme (refresh) işleminde eski refresh token iptal edilir (`RevokedAt`) ve yeni bir refresh token üretilir.
  - Refresh token'lar veritabanında SHA-256 hash'i olarak saklanır; ham token hiçbir zaman DB'de tutulmaz.
  - Çalınan token tespiti durumunda ilgili kullanıcının tüm oturumları anında geçersiz kılınabilir (`RevokeAllSessionsAsync`).
- **Şifre Güvenliği:**
  - `PBKDF2` (HMAC-SHA256, 100.000 iterasyon) ve kriptografik tuz (salt) ile hashlenir.

---

## 2. IDOR (Insecure Direct Object Reference) Koruması

- Kullanıcıların oluşturduğu tüm kaynaklar (`StockMonitor`, `NotificationLog`, `UserTelegramConfig`, `UserPreferences`) veritabanı sorgularında doğrudan JWT'deki `UserId` ile filtrelenir (`Where(m => m.UserId == userId)`).
- Başka bir kullanıcının ID'si ile yapılan monitor güncelleme, silme veya bildirim görüntüleme istekleri `404 Not Found` ile engellenir.

---

## 3. Hassas Veri & Sır Yönetimi (Secret Management)

- **Telegram Bot Token:**
  - ASP.NET Data Protection API (`ISecretProtector`) ile AES-256 şifrelemeyle veritabanına yazılır.
  - API yanıtlarında ve UI üzerinde sadece maskelenmiş format (`1234••••••5678`) sunulur.
  - Loglara veya hata mesajlarına hiçbir zaman plaintext token yazılmaz.
- **Kredi Kartı / Ödeme Bilgileri:**
  - Sistem Zero-PCI prensibini benimser. Kredi kartı numarası, CVV veya son kullanma tarihi kesinlikle sunucuya veya veritabanına kaydedilmez.
  - Ödeme altyapısı hazır bulunmakla birlikte kullanıcı arayüzünde tamamen pasiftir (`billingEnabled: false`).

---

## 4. Webhook & Entegrasyon Güvenliği

- Gelen ödeme webhook'ları HMAC-SHA256 dijital imzaları ile doğrulanır.
- Geçersiz imza veya eksik header içeren webhook istekleri `400 Bad Request` ile reddedilir.
- Webhook etkinlikleri `PaymentWebhookEvents` tablosunda `IdempotencyKey` ile tekilleştirilir; aynı event birden fazla kez işlenmez.

---

## 5. Ağ & Taşıma Katmanı Güvenliği (Transport & Headers)

- **Security Headers:**
  - `X-Content-Type-Options: nosniff` (MIME sniffing engelleme)
  - `X-Frame-Options: DENY` (Clickjacking engelleme)
  - `Referrer-Policy: strict-origin-when-cross-origin` (Hassas URL referans sızıntısı engelleme)
  - `Content-Security-Policy`: XSS ataklarını önlemek için katı kaynak kısıtlaması.
  - `Strict-Transport-Security (HSTS)`: HTTPS zorunluluğu.
- **Kestrel Hardening:**
  - 10MB maksimum istek boyutu sınırı (`MaxRequestBodySize`).
  - `Server` ve `X-Powered-By` header'ları kaldırılmıştır.

---

## 6. Hata Yönetimi & Bilgi Sızıntısı Engelleme (Zero Info Leakage)

- Tüm istisnalar merkezi `GlobalExceptionMiddleware` tarafından yakalanır.
- Kullanıcıya sunucu iç hata detayları veya stack trace gösterilmez.
- Her hata yanıtına benzersiz bir `X-Correlation-ID` atanarak loglarla eşleştirme sağlanır.
