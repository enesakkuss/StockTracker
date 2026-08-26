/**
 * Authentication and Session Management.
 * Handles JWT Access Tokens, Refresh Token Rotation, User State, and Route Protection.
 */
class AuthManager {
  constructor() {
    this.accessToken = localStorage.getItem(AppConfig.storageKeys.accessToken) || null;
    this.refreshToken = localStorage.getItem(AppConfig.storageKeys.refreshToken) || null;
    this.currentUser = null;
    this.listeners = [];

    const storedUser = localStorage.getItem(AppConfig.storageKeys.user);
    if (storedUser) {
      try {
        this.currentUser = JSON.parse(storedUser);
      } catch (e) {
        this.currentUser = null;
      }
    }
  }

  getAccessToken() {
    return this.accessToken;
  }

  getToken() {
    return this.accessToken;
  }

  getRefreshToken() {
    return this.refreshToken;
  }

  getUser() {
    return this.currentUser;
  }

  isAuthenticated() {
    return !!(this.accessToken && this.currentUser);
  }

  onAuthStateChanged(callback) {
    if (typeof callback === 'function') {
      this.listeners.push(callback);
      // Immediately notify current state
      callback(this.isAuthenticated(), this.currentUser);
    }
  }

  notifyListeners() {
    const isAuth = this.isAuthenticated();
    this.listeners.forEach(cb => {
      try {
        cb(isAuth, this.currentUser);
      } catch (e) {
        console.error('Auth state listener error:', e);
      }
    });
  }

  setSession(token, refreshToken, user) {
    this.accessToken = token;
    this.refreshToken = refreshToken;
    this.currentUser = user;

    if (token) {
      localStorage.setItem(AppConfig.storageKeys.accessToken, token);
    } else {
      localStorage.removeItem(AppConfig.storageKeys.accessToken);
    }

    if (refreshToken) {
      localStorage.setItem(AppConfig.storageKeys.refreshToken, refreshToken);
    } else {
      localStorage.removeItem(AppConfig.storageKeys.refreshToken);
    }

    if (user) {
      localStorage.setItem(AppConfig.storageKeys.user, JSON.stringify(user));
    } else {
      localStorage.removeItem(AppConfig.storageKeys.user);
    }

    this.notifyListeners();
  }

  clearSession() {
    this.accessToken = null;
    this.refreshToken = null;
    this.currentUser = null;
    localStorage.removeItem(AppConfig.storageKeys.accessToken);
    localStorage.removeItem(AppConfig.storageKeys.refreshToken);
    localStorage.removeItem(AppConfig.storageKeys.user);
    this.notifyListeners();
  }

  /**
   * Performs user login and saves Access + Refresh tokens.
   */
  async login(email, password) {
    const response = await window.apiClient.post('/api/auth/login', { email, password });
    if (response && response.token) {
      this.setSession(response.token, response.refreshToken, response.user);
      return response.user;
    }
    throw new Error('Giriş başarısız: Geçersiz sunucu yanıtı.');
  }

  /**
   * Performs user registration and saves Access + Refresh tokens.
   */
  async register(userData) {
    const response = await window.apiClient.post('/api/auth/register', userData);
    if (response && response.token) {
      this.setSession(response.token, response.refreshToken, response.user);
      return response.user;
    }
    throw new Error('Kayıt başarısız: Geçersiz sunucu yanıtı.');
  }

  /**
   * Refreshes access token using stored refresh token (Token Rotation).
   */
  async refreshSession() {
    if (!this.refreshToken) {
      throw new Error('Yenileme tokenı bulunamadı.');
    }

    const response = await fetch(`${AppConfig.apiBaseUrl}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: this.refreshToken })
    });

    if (!response.ok) {
      throw new Error('Oturum yenilenemedi.');
    }

    const data = await response.json();
    const result = (data && data.success && data.data) ? data.data : data;

    if (result && result.token) {
      this.setSession(result.token, result.refreshToken, result.user || this.currentUser);
      return result.token;
    }

    throw new Error('Geçersiz yenileme yanıtı.');
  }

  /**
   * Performs graceful logout, notifying backend and clearing local state.
   */
  async logout() {
    if (this.refreshToken) {
      try {
        await window.apiClient.post('/api/auth/logout', { refreshToken: this.refreshToken });
      } catch (err) {
        console.warn('Logout notification error (proceeding with local cleanup):', err);
      }
    }
    this.clearSession();
  }

  handleSessionExpired() {
    this.clearSession();
    if (window.UI) {
      window.UI.showToast('Oturumunuzun süresi doldu. Lütfen tekrar giriş yapın.', 'warning');
      window.UI.showAuthModalOrTab('login');
    }
  }

  /**
   * Validates stored credentials and synchronizes current user profile.
   */
  async init() {
    if (!this.accessToken && !this.refreshToken) {
      this.clearSession();
      return;
    }

    try {
      const userProfile = await window.apiClient.get('/api/users/me');
      if (userProfile) {
        this.currentUser = userProfile;
        localStorage.setItem(AppConfig.storageKeys.user, JSON.stringify(userProfile));
        this.notifyListeners();
      }
    } catch (err) {
      // If token expired, apiClient interceptor will try refreshing.
      // If that also fails, session will be cleared automatically.
      console.warn('Initial session validation failed:', err);
    }
  }

  /**
   * Protected Action / Route Guard.
   * If authenticated, executes the callback.
   * Otherwise, prompts user to log in and prevents unauthorized execution.
   */
  requireAuth(callback) {
    if (this.isAuthenticated()) {
      return callback();
    }
    if (window.UI) {
      window.UI.showToast('Bu işlemi gerçekleştirmek için lütfen önce giriş yapın.', 'warning');
      window.UI.showAuthModalOrTab('login');
    }
    return false;
  }
}

window.AuthManager = new AuthManager();
