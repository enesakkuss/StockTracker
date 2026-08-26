/**
 * StockTracker Frontend Configuration
 * Manages API endpoints, storage keys, and environment variables.
 */
const AppConfig = {
  // Base API URL (empty for same-origin, or custom endpoint if hosted separately)
  apiBaseUrl: window.STOCKTRACKER_API_URL || '',

  // Storage Keys
  storageKeys: {
    accessToken: 'stocktracker_access_token',
    refreshToken: 'stocktracker_refresh_token',
    user: 'stocktracker_user'
  },

  // Token Expiration Buffer (seconds before expiry to preemptively refresh)
  tokenExpiryBufferSeconds: 30,

  // Feature Flags
  features: {
    billingEnabled: false // Disabled in FAZ 13.1 as required
  }
};

window.AppConfig = AppConfig;
