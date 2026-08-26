/**
 * UI Utilities for Global Loading, Status Banners, Toasts, Error States,
 * Navigation & Application Shell View Switching.
 */
class UIManager {
  constructor() {
    this.createToastContainer();
    this.currentView = 'inspector';
  }

  createToastContainer() {
    let container = document.getElementById('toast-container');
    if (!container) {
      container = document.createElement('div');
      container.id = 'toast-container';
      container.className = 'toast-container';
      document.body.appendChild(container);
    }
    this.toastContainer = container;
  }

  showToast(message, type = 'info', duration = 4000) {
    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;

    const iconMap = {
      success: '🟢',
      error: '🔴',
      warning: '⚠️',
      info: 'ℹ️'
    };

    const icon = iconMap[type] || 'ℹ️';
    toast.innerHTML = `<span class="toast-icon">${icon}</span> <span class="toast-message">${this.escapeHtml(message)}</span>`;

    this.toastContainer.appendChild(toast);

    setTimeout(() => {
      toast.classList.add('toast-fadeout');
      setTimeout(() => toast.remove(), 300);
    }, duration);
  }

  setStatus(element, text, type = '', showSpinner = false) {
    if (!element) return;
    element.className = 'status-box ' + type;
    element.innerHTML = '';

    if (showSpinner) {
      const s = document.createElement('span');
      s.className = 'spinner';
      element.appendChild(s);
    }

    if (text) {
      const textNode = document.createElement('span');
      textNode.textContent = text;
      element.appendChild(textNode);
    }
  }

  renderEmptyState(container, message = 'Kayıt bulunamadı.', icon = '📦') {
    if (!container) return;
    container.innerHTML = `
      <div class="empty-state-box">
        <div class="empty-state-icon">${icon}</div>
        <div class="empty-state-message">${this.escapeHtml(message)}</div>
      </div>
    `;
  }

  renderErrorState(container, error, retryCallback = null) {
    if (!container) return;
    const msg = error?.message || 'Veriler yüklenirken bir hata oluştu.';
    const code = error?.code ? `<div class="error-code">Hata Kodu: ${this.escapeHtml(error.code)}</div>` : '';

    container.innerHTML = `
      <div class="error-state-box">
        <div class="error-state-icon">⚠️</div>
        <div class="error-state-message">${this.escapeHtml(msg)}</div>
        ${code}
        ${retryCallback ? `<button class="btn-outline-primary btn-retry" style="margin-top:0.8rem;">Yeniden Dene</button>` : ''}
      </div>
    `;

    if (retryCallback) {
      const retryBtn = container.querySelector('.btn-retry');
      if (retryBtn) {
        retryBtn.addEventListener('click', retryCallback);
      }
    }
  }

  showAuthModalOrTab(tab = 'login') {
    const tabLoginBtn = document.getElementById('tab-login-btn');
    const tabRegisterBtn = document.getElementById('tab-register-btn');
    const loginForm = document.getElementById('login-form');
    const registerForm = document.getElementById('register-form');

    if (tab === 'login') {
      if (tabLoginBtn) tabLoginBtn.classList.add('active');
      if (tabRegisterBtn) tabRegisterBtn.classList.remove('active');
      if (loginForm) loginForm.style.display = 'block';
      if (registerForm) registerForm.style.display = 'none';
      const emailInput = document.getElementById('login-email');
      if (emailInput) emailInput.focus();
    } else if (tab === 'register') {
      if (tabRegisterBtn) tabRegisterBtn.classList.add('active');
      if (tabLoginBtn) tabLoginBtn.classList.remove('active');
      if (registerForm) registerForm.style.display = 'block';
      if (loginForm) loginForm.style.display = 'none';
      const firstNameInput = document.getElementById('reg-firstname');
      if (firstNameInput) firstNameInput.focus();
    }
  }

  switchView(viewName) {
    this.currentView = viewName;

    // Update Nav Active State
    const navItems = document.querySelectorAll('.nav-item');
    navItems.forEach(item => {
      const target = item.getAttribute('data-view');
      item.classList.toggle('active', target === viewName);
    });

    // Update View Sections
    const views = document.querySelectorAll('.view-section');
    views.forEach(v => {
      v.classList.toggle('active', v.id === `view-${viewName}`);
    });

    // Update Topbar Title
    const titleEl = document.getElementById('current-view-title');
    const titles = {
      dashboard: 'Dashboard',
      inspector: 'Ürün İnceleme & Takip',
      monitors: 'Kayıtlı Stok Takipleri',
      notifications: 'Bildirim Geçmişi',
      telegram: 'Telegram Ayarları',
      settings: 'Hesap & Ayarlar'
    };
    if (titleEl) {
      titleEl.textContent = titles[viewName] || 'StockTracker';
    }

    // Close mobile sidebar if open
    this.toggleSidebar(false);
  }

  toggleSidebar(forceState = null) {
    const sidebar = document.querySelector('.app-sidebar');
    const backdrop = document.querySelector('.sidebar-backdrop');
    if (!sidebar) return;

    const isOpen = forceState !== null ? forceState : !sidebar.classList.contains('open');
    sidebar.classList.toggle('open', isOpen);
    if (backdrop) backdrop.classList.toggle('active', isOpen);
  }

  escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }
}

window.UI = new UIManager();
