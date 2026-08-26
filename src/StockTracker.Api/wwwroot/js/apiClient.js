/**
 * Centralized API Client with JWT & Refresh Token Rotation Interceptor,
 * Concurrency Lock, and Unified Error Handling.
 */
class ApiError extends Error {
  constructor(status, code, message, details = null, raw = null) {
    super(message || 'Bir hata oluştu.');
    this.name = 'ApiError';
    this.status = status;
    this.code = code || `HTTP_${status}`;
    this.details = details;
    this.raw = raw;
  }
}

class ApiClient {
  constructor() {
    this.isRefreshing = false;
    this.failedQueue = [];
  }

  processQueue(error, token = null) {
    this.failedQueue.forEach(prom => {
      if (error) {
        prom.reject(error);
      } else {
        prom.resolve(token);
      }
    });
    this.failedQueue = [];
  }

  /**
   * Translates backend error codes into user-friendly Turkish messages.
   */
  mapError(status, data) {
    const code = data?.error?.code || data?.errorCode || `HTTP_${status}`;
    let message = data?.error?.message || data?.error || data?.message;

    if (!message) {
      switch (status) {
        case 400:
          message = 'Geçersiz veya eksik parametre gönderildi.';
          break;
        case 401:
          message = 'Oturum süreniz doldu veya giriş yapmanız gerekiyor.';
          break;
        case 403:
          message = 'Bu işlem için yetkiniz bulunmuyor.';
          break;
        case 404:
          message = 'İstenen kaynak bulunamadı.';
          break;
        case 409:
          message = 'Bu işlem mevcut verilerle çakışıyor (E-posta veya aktif kayıt mevcut).';
          break;
        case 422:
          if (code === 'UNSUPPORTED_STORE') {
            message = 'Bu mağaza domaini henüz desteklenmiyor.';
          } else if (code === 'PLAN_LIMIT_REACHED') {
            message = 'Planınızdaki takip limitine ulaştınız.';
          } else if (code === 'CHECK_INTERVAL_NOT_ALLOWED') {
            message = 'Seçilen kontrol sıklığı mevcut planınız için izin verilmiyor.';
          } else {
            message = 'İstek işlenemedi.';
          }
          break;
        case 429:
          if (code === 'DAILY_INSPECT_LIMIT_REACHED') {
            message = 'Günlük ürün inceleme limitinize ulaştınız. Lütfen yarın tekrar deneyin.';
          } else {
            message = 'Çok fazla istek gönderdiniz. Lütfen biraz bekleyin.';
          }
          break;
        case 500:
        default:
          message = 'Sunucuda beklenmeyen bir hata oluştu.';
          break;
      }
    }

    return new ApiError(status, code, message, data?.error?.details || null, data);
  }

  /**
   * Performs an HTTP request with automatic token injection and refresh retry.
   */
  async request(endpoint, options = {}) {
    const url = endpoint.startsWith('http') ? endpoint : `${AppConfig.apiBaseUrl}${endpoint}`;
    const headers = {
      'Content-Type': 'application/json',
      ...(options.headers || {})
    };

    const token = window.AuthManager ? window.AuthManager.getAccessToken() : localStorage.getItem(AppConfig.storageKeys.accessToken);
    if (token && !headers['Authorization']) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const fetchConfig = {
      ...options,
      headers
    };

    if (fetchConfig.body && typeof fetchConfig.body === 'object' && !(fetchConfig.body instanceof FormData)) {
      fetchConfig.body = JSON.stringify(fetchConfig.body);
    }

    try {
      const response = await fetch(url, fetchConfig);

      // Handle 204 No Content
      if (response.status === 204) {
        return null;
      }

      const contentType = response.headers.get('content-type');
      const isJson = contentType && contentType.includes('application/json');
      const data = isJson ? await response.json() : await response.text();

      // If response is OK, return data or standard response data property
      if (response.ok) {
        if (data && typeof data === 'object' && 'success' in data && 'data' in data) {
          return data.data !== undefined ? data.data : data;
        }
        return data;
      }

      // Handle 401 Unauthorized (Token Expiration) -> Attempt Refresh
      const isAuthEndpoint = endpoint.includes('/api/auth/login') || endpoint.includes('/api/auth/register') || endpoint.includes('/api/auth/refresh');
      if (response.status === 401 && !isAuthEndpoint && window.AuthManager && window.AuthManager.getRefreshToken()) {
        if (this.isRefreshing) {
          return new Promise((resolve, reject) => {
            this.failedQueue.push({ resolve, reject });
          }).then(newToken => {
            fetchConfig.headers['Authorization'] = `Bearer ${newToken}`;
            return fetch(url, fetchConfig).then(r => r.json());
          });
        }

        this.isRefreshing = true;

        try {
          const newToken = await window.AuthManager.refreshSession();
          this.processQueue(null, newToken);
          fetchConfig.headers['Authorization'] = `Bearer ${newToken}`;
          return this.request(endpoint, options);
        } catch (refreshErr) {
          this.processQueue(refreshErr, null);
          window.AuthManager.handleSessionExpired();
          throw this.mapError(401, { error: { code: 'UNAUTHORIZED', message: 'Oturum süreniz doldu, lütfen tekrar giriş yapın.' } });
        } finally {
          this.isRefreshing = false;
        }
      }

      throw this.mapError(response.status, data);

    } catch (err) {
      if (err instanceof ApiError) {
        throw err;
      }
      // Network failure or abort
      throw new ApiError(0, 'NETWORK_ERROR', 'Sunucuya ulaşılamıyor. Lütfen internet bağlantınızı kontrol edin.', null, err);
    }
  }

  get(endpoint, options = {}) {
    return this.request(endpoint, { ...options, method: 'GET' });
  }

  post(endpoint, body, options = {}) {
    return this.request(endpoint, { ...options, method: 'POST', body });
  }

  put(endpoint, body, options = {}) {
    return this.request(endpoint, { ...options, method: 'PUT', body });
  }

  delete(endpoint, options = {}) {
    return this.request(endpoint, { ...options, method: 'DELETE' });
  }
}

window.apiClient = new ApiClient();
