# StockTracker API Documentation (v1.2 Production, Monetization & Payments)

Bu doküman, StockTracker backend API'sini tüketen **Web, iOS ve Android (Mobil)** istemcileri için eksiksiz entegrasyon sözleşmesini tanımlar.

---

## 1. Genel Standartlar

- **Base URL:** `http://localhost:5066` (veya production domain)
- **Veri Formatı:** `application/json` (UTF-8)
- **Tarih & Saat:** ISO-8601 UTC formatı (`2026-08-25T14:30:00.000Z`)
- **Correlation ID:** Her istekte otomatik olarak üretilir veya `X-Correlation-ID` header'ı ile izlenir.

---

## 2. API Response & Hata Standardı

### Başarılı Yanıt Formatı:
```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "correlationId": "4a7b9c1d..."
}
```

### Hatalı Yanıt Formatı:
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "PLAN_LIMIT_REACHED",
    "message": "FREE planınızdaki aktif takip limitine (5) ulaştınız.",
    "details": null
  },
  "correlationId": "4a7b9c1d..."
}
```

### Standart Hata Kodları (Error Codes):
- `VALIDATION_ERROR`: Geçersiz veya eksik parametre (`400 Bad Request`).
- `UNAUTHORIZED`: Token geçersiz, süresi dolmuş veya geçersiz webhook imzası (`401 Unauthorized`).
- `NOT_FOUND`: Kaynak bulunamadı veya IDOR koruması (`404 Not Found`).
- `CONFLICT`: E-posta veya aktif abonelik çakışması (`409 Conflict`).
- `UNSUPPORTED_STORE`: Desteklenmeyen mağaza domaini (`422 Unprocessable Entity`).
- `PLAN_LIMIT_REACHED`: Aktif veya toplam takip limiti aşıldı (`422 Unprocessable Entity`).
- `CHECK_INTERVAL_NOT_ALLOWED`: Seçilen kontrol sıklığı plan sınırının altında (`422 Unprocessable Entity`).
- `DAILY_INSPECT_LIMIT_REACHED`: Günlük ürün inceleme limiti aşıldı (`429 Too Many Requests`).
- `INTERNAL_SERVER_ERROR`: Beklenmeyen sunucu hatası (`500 Internal Server Error`).

---

## 3. Sayfalama (Pagination) Standardı

Tüm liste sorguları `page` (varsayılan: 1) ve `pageSize` (varsayılan: 20, tavan sınır: 100) parametrelerini destekler.

```json
{
  "items": [ ... ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 42,
  "totalPages": 3,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

---

## 4. Kimlik Doğrulama (Authentication Flow)

API, **JWT Access Token (Kısa Ömürlü)** ve **Refresh Token (30 Günlük, DB'de SHA-256 Hashli, Token Rotation)** mimarisini kullanır.

- `POST /api/auth/register`: Yeni kullanıcı kaydı.
- `POST /api/auth/login`: Giriş ve token üretimi.
- `POST /api/auth/refresh`: Token rotation ile yeni access ve refresh token.
- `POST /api/auth/logout`: Oturum sonlandırma.
- `POST /api/auth/revoke-all`: Tüm aktif oturumları kapatma.

---

## 5. Abonelik Yönetimi (Subscriptions API)

### 5.1. Aktif Aboneliğimi Getir
- **Endpoint:** `GET /api/subscriptions/me` [Korumalı]

### 5.2. Aktif Planları Listele
- **Endpoint:** `GET /api/subscriptions/plans` [Açık]

### 5.3. Kullanım Özeti
- **Endpoint:** `GET /api/subscriptions/usage` [Korumalı]

### 5.4. Checkout Oturumu Başlatma (Idempotent)
- **Endpoint:** `POST /api/subscriptions/checkout` [Korumalı]
- **Header:** `X-Idempotency-Key` (İsteğe bağlı tekil anahtar)
- **İstek Gövdesi:**
```json
{
  "planId": 2,
  "successUrl": "https://app.stocktracker.local/payment/success",
  "cancelUrl": "https://app.stocktracker.local/payment/cancel",
  "idempotencyKey": "unique_tx_key_123"
}
```
- **Yanıt (200 OK):**
```json
{
  "success": true,
  "sessionId": "mock_sess_abc123",
  "checkoutUrl": "https://checkout.stocktracker.local/pay/mock_sess_abc123",
  "provider": "Mock",
  "errorCode": null,
  "errorMessage": null
}
```

### 5.5. Abonelik İptali
- **Endpoint:** `POST /api/subscriptions/cancel` [Korumalı] (`204 No Content`)

---

## 6. Ödeme & İşlem Yönetimi (Payments API)

### 6.1. Ödeme Geçmişi
- **Endpoint:** `GET /api/payments/history?page=1&pageSize=20` [Korumalı]
- **Yanıt (200 OK):**
```json
{
  "items": [
    {
      "id": 1,
      "userId": 5,
      "subscriptionId": 2,
      "provider": "Iyzico",
      "providerTransactionId": "iyzico_tx_987654",
      "amount": 199.00,
      "currency": "TRY",
      "status": "Succeeded",
      "paymentType": "Upgrade",
      "createdAt": "2026-08-25T15:00:00Z",
      "completedAt": "2026-08-25T15:00:02Z",
      "failureReason": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

### 6.2. Ödeme Detayı
- **Endpoint:** `GET /api/payments/{id}` [Korumalı - IDOR Safe]

### 6.3. Webhook Bildirimleri (Provider Callbacks)
- **Endpoint:** `POST /api/payments/webhook/{provider}` [Açık / İmza Kontrollü]
- **Desteklenen Sağlayıcılar:** `Iyzico`, `Mock`
- **Header:** `X-Signature` veya `X-Iyzico-Signature` (HMAC-SHA256)
- **Örnek Payload:**
```json
{
  "eventId": "mock_sess_abc123",
  "eventType": "payment.success"
}
```
- **Yanıt (200 OK):**
```json
{
  "success": true,
  "eventId": "mock_sess_abc123",
  "eventType": "payment.success",
  "message": "Processed successfully"
}
```

### 6.4. Ödeme İadesi (Refund)
- **Endpoint:** `POST /api/payments/{id}/refund` [Korumalı]

---

## 7. Kullanıcı Profili, Dashboard, Monitors & Notifications

- `GET /api/users/me` / `PUT /api/users/me`: Profil ve tercihler.
- `GET /api/users/me/telegram`: Telegram yapılandırması.
- `GET /api/dashboard/summary`: Anlık kullanıcı özet istatistikleri.
- `POST /api/products/inspect`: Ürün inceleme (günlük plan limiti uygulanır).
- `GET, POST, PUT, DELETE /api/monitors`: Stok takip yönetimi.
- `GET /api/notifications`: Filtrelenebilir bildirim geçmişi.
- `GET /health`: Health checks (`live`, `ready`).
