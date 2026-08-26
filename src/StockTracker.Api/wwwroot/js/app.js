/**
 * StockTracker Main Application Script (FAZ 13.6)
 * Handles Application Shell, Dashboard Summary, Monitor Management CRUD,
 * Notification History, Telegram Configuration, User Profile/Settings,
 * and Production-Ready Product Inspector & Monitor Creation.
 */
document.addEventListener('DOMContentLoaded', () => {
  // ── State ────────────────────────────────────────────────────────────────
  let currentProductData = null;
  let currentMonitorsPage = 1;
  let currentMonitorsPageSize = 20;
  let cachedMonitors = [];

  let currentNotifsPage = 1;
  let currentNotifsPageSize = 20;
  let supportedStoresLoaded = false;
  let cachedUserProfile = null;

  // ── Shell Elements ───────────────────────────────────────────────────────
  const authViewWrapper = document.getElementById('auth-view-wrapper');
  const appShellWrapper = document.getElementById('app-shell-wrapper');
  const sidebarToggleBtn = document.getElementById('sidebar-toggle-btn');
  const sidebarCloseBtn = document.getElementById('sidebar-close-btn');
  const sidebarBackdrop = document.querySelector('.sidebar-backdrop');
  const navItems = document.querySelectorAll('.nav-item');

  // Auth DOM
  const authLoggedOut = document.getElementById('auth-logged-out');
  const authLoggedIn = document.getElementById('auth-logged-in');
  const tabLoginBtn = document.getElementById('tab-login-btn');
  const tabRegisterBtn = document.getElementById('tab-register-btn');
  const loginForm = document.getElementById('login-form');
  const registerForm = document.getElementById('register-form');
  const authStatusMsg = document.getElementById('auth-status-msg');
  const currentUserName = document.getElementById('current-user-name');
  const currentUserEmail = document.getElementById('current-user-email');
  const sidebarUserName = document.getElementById('sidebar-user-name');
  const sidebarUserEmail = document.getElementById('sidebar-user-email');
  const sidebarUserAvatar = document.getElementById('sidebar-user-avatar');
  const logoutBtn = document.getElementById('logout-btn');
  const topbarLogoutBtn = document.getElementById('topbar-logout-btn');

  // Dashboard DOM
  const refreshDashboardBtn = document.getElementById('refresh-dashboard-btn');
  const dashTotalMonitors = document.getElementById('dash-total-monitors');
  const dashActiveMonitors = document.getElementById('dash-active-monitors');
  const dashPausedMonitors = document.getElementById('dash-paused-monitors');
  const dashAvailableItems = document.getElementById('dash-available-items');
  const dashNotifications = document.getElementById('dash-notifications-today');
  const dashPlanInfo = document.getElementById('dash-plan-info');
  const dashActiveBar = document.getElementById('dash-active-progress');
  const dashOverviewContainer = document.getElementById('dash-overview-container');

  // Inspector DOM
  const urlInput = document.getElementById('url-input');
  const fetchBtn = document.getElementById('fetch-btn');
  const fetchStatusMsg = document.getElementById('fetch-status-msg');
  const productCard = document.getElementById('product-card');
  const emptyCard = document.getElementById('empty-card');
  const inspectStatusBanner = document.getElementById('inspect-status-banner');
  const selectAllVariantsBtn = document.getElementById('select-all-variants-btn');
  const deselectAllVariantsBtn = document.getElementById('deselect-all-variants-btn');

  // Inspector Telegram DOM
  const tgTokenInput = document.getElementById('tg-token');
  const tgChatIdInput = document.getElementById('tg-chat-id');
  const tgTestBtn = document.getElementById('tg-test-btn');
  const tgStatusMsg = document.getElementById('tg-status-msg');
  const tgChoiceSaved = document.getElementById('tg-choice-saved');
  const tgChoiceCustom = document.getElementById('tg-choice-custom');
  const tgOptionSavedWrapper = document.getElementById('tg-option-saved-wrapper');
  const tgOptionCustomWrapper = document.getElementById('tg-option-custom-wrapper');
  const tgSavedBotTitle = document.getElementById('tg-saved-bot-title');
  const tgSavedBotChatIdLabel = document.getElementById('tg-saved-bot-chatid-label');
  const tgCustomFields = document.getElementById('tg-custom-fields');
  const tgNoSavedBotHint = document.getElementById('tg-no-saved-bot-hint');
  const tgLinkToSettings = document.getElementById('tg-link-to-settings');

  // Monitor Config DOM
  const intervalSelect = document.getElementById('interval-select');
  const startMonitorBtn = document.getElementById('start-monitor-btn');
  const startMonitorStatus = document.getElementById('start-monitor-status-msg');
  const monitorUsageBadge = document.getElementById('monitor-usage-badge');

  // Monitors List & Pagination DOM
  const monitorsContainer = document.getElementById('monitors-container');
  const refreshMonitorsBtn = document.getElementById('refresh-monitors-btn');
  const monitorsSearchInput = document.getElementById('monitors-search-input');
  const prevPageBtn = document.getElementById('prev-page-btn');
  const nextPageBtn = document.getElementById('next-page-btn');
  const paginationInfo = document.getElementById('pagination-info');
  const pageSizeSelect = document.getElementById('page-size-select');
  const addMonitorCtaBtn = document.getElementById('add-monitor-cta-btn');

  // Edit Modal DOM
  const editModal = document.getElementById('edit-monitor-modal');
  const editForm = document.getElementById('edit-monitor-form');
  const editCloseBtn = document.getElementById('edit-modal-close-btn');
  const editCancelBtn = document.getElementById('edit-modal-cancel-btn');
  const editMonitorId = document.getElementById('edit-monitor-id');
  const editProductName = document.getElementById('edit-product-name');
  const editVariantsInput = document.getElementById('edit-variants-input');
  const editIntervalSelect = document.getElementById('edit-interval-select');
  const editChatIdInput = document.getElementById('edit-chat-id-input');
  const editStatusMsg = document.getElementById('edit-status-msg');

  // Notifications View DOM
  const notifFilterStore = document.getElementById('notif-filter-store');
  const notifFilterDateFrom = document.getElementById('notif-filter-date-from');
  const notifFilterDateTo = document.getElementById('notif-filter-date-to');
  const notifFilterBtn = document.getElementById('notif-filter-btn');
  const notifResetBtn = document.getElementById('notif-reset-btn');
  const notificationsContainer = document.getElementById('notifications-container');
  const notifPaginationInfo = document.getElementById('notif-pagination-info');
  const notifPrevPageBtn = document.getElementById('notif-prev-page-btn');
  const notifNextPageBtn = document.getElementById('notif-next-page-btn');
  const notifPageSizeSelect = document.getElementById('notif-page-size-select');

  // Telegram View DOM
  const tgStatusBadge = document.getElementById('tg-status-badge');
  const tgStatusMaskedToken = document.getElementById('tg-status-masked-token');
  const tgStatusChatId = document.getElementById('tg-status-chat-id');
  const tgStatusUpdatedAt = document.getElementById('tg-status-updated-at');
  const viewTgForm = document.getElementById('view-tg-settings-form');
  const viewTgToken = document.getElementById('view-tg-token');
  const viewTgChatId = document.getElementById('view-tg-chat-id');
  const viewTgTestBtn = document.getElementById('view-tg-test-btn');
  const viewTgDisconnectBtn = document.getElementById('view-tg-disconnect-btn');
  const viewTgStatusMsg = document.getElementById('view-tg-status-msg');

  // Settings View DOM
  const settingsProfileForm = document.getElementById('settings-profile-form');
  const settingsFirstnameInput = document.getElementById('settings-firstname-input');
  const settingsLastnameInput = document.getElementById('settings-lastname-input');
  const settingsEmailDisplay = document.getElementById('settings-email-display');
  const settingsProfileStatus = document.getElementById('settings-profile-status-msg');

  const settingsPreferencesForm = document.getElementById('settings-preferences-form');
  const settingsPrefTgEnabled = document.getElementById('settings-pref-telegram-enabled');
  const settingsPrefLangSelect = document.getElementById('settings-pref-language-select');
  const settingsPrefIntervalSelect = document.getElementById('settings-pref-interval-select');
  const settingsPrefTimezoneSelect = document.getElementById('settings-pref-timezone-select');
  const settingsPrefStatus = document.getElementById('settings-pref-status-msg');

  const settingsAccountEmail = document.getElementById('settings-account-email');
  const settingsAccountCreatedAt = document.getElementById('settings-account-created-at');
  const settingsAccountLastLogin = document.getElementById('settings-account-last-login');
  const settingsAccountTgStatus = document.getElementById('settings-account-tg-status');
  const settingsRevokeAllBtn = document.getElementById('settings-revoke-all-btn');

  // ── Navigation & View Switching ──────────────────────────────────────────
  navItems.forEach(item => {
    item.addEventListener('click', (e) => {
      e.preventDefault();
      const view = item.getAttribute('data-view');
      if (view) {
        window.UI.switchView(view);
        if (view === 'dashboard') loadDashboard();
        if (view === 'monitors') loadMonitors(currentMonitorsPage, currentMonitorsPageSize);
        if (view === 'inspector') {
          loadInspectorUsageInfo();
          loadUserTelegramSettings();
        }
        if (view === 'notifications') {
          loadSupportedStores();
          loadNotifications(1, currentNotifsPageSize);
        }
        if (view === 'telegram') loadDedicatedTelegramSettings();
        if (view === 'settings') loadUserProfileSettings();
      }
    });
  });

  if (sidebarToggleBtn) sidebarToggleBtn.addEventListener('click', () => window.UI.toggleSidebar());
  if (sidebarCloseBtn) sidebarCloseBtn.addEventListener('click', () => window.UI.toggleSidebar(false));
  if (sidebarBackdrop) sidebarBackdrop.addEventListener('click', () => window.UI.toggleSidebar(false));

  if (addMonitorCtaBtn) {
    addMonitorCtaBtn.addEventListener('click', () => window.UI.switchView('inspector'));
  }

  // ── Auth Tab Switching ───────────────────────────────────────────────────
  if (tabLoginBtn) tabLoginBtn.addEventListener('click', () => window.UI.showAuthModalOrTab('login'));
  if (tabRegisterBtn) tabRegisterBtn.addEventListener('click', () => window.UI.showAuthModalOrTab('register'));

  // ── Login Form Submission ────────────────────────────────────────────────
  loginForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const email = document.getElementById('login-email').value.trim();
    const password = document.getElementById('login-password').value.trim();

    if (!email || !password) {
      window.UI.setStatus(authStatusMsg, 'Lütfen e-posta ve şifrenizi girin.', 'error');
      return;
    }

    window.UI.setStatus(authStatusMsg, 'Giriş yapılıyor...', 'loading', true);
    const submitBtn = loginForm.querySelector('button[type="submit"]');
    if (submitBtn) submitBtn.disabled = true;

    try {
      await window.AuthManager.login(email, password);
      window.UI.setStatus(authStatusMsg, '🟢 Giriş başarılı!', 'success');
      window.UI.showToast('Başarıyla giriş yaptınız.', 'success');
      loadUserTelegramSettings();
      loadDashboard();
      loadMonitors(1, currentMonitorsPageSize);
      loadUserProfileSettings();
      loadInspectorUsageInfo();
    } catch (err) {
      window.UI.setStatus(authStatusMsg, `🔴 ${err.message}`, 'error');
      window.UI.showToast(err.message, 'error');
    } finally {
      if (submitBtn) submitBtn.disabled = false;
    }
  });

  // ── Register Form Submission ─────────────────────────────────────────────
  registerForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const firstName = document.getElementById('reg-firstname').value.trim();
    const lastName = document.getElementById('reg-lastname').value.trim();
    const email = document.getElementById('reg-email').value.trim();
    const password = document.getElementById('reg-password').value.trim();

    if (!firstName || !lastName || !email || !password) {
      window.UI.setStatus(authStatusMsg, 'Lütfen tüm alanları doldurun.', 'error');
      return;
    }
    if (password.length < 6) {
      window.UI.setStatus(authStatusMsg, 'Şifre en az 6 karakter olmalıdır.', 'error');
      return;
    }

    window.UI.setStatus(authStatusMsg, 'Hesap oluşturuluyor...', 'loading', true);
    const submitBtn = registerForm.querySelector('button[type="submit"]');
    if (submitBtn) submitBtn.disabled = true;

    try {
      await window.AuthManager.register({ firstName, lastName, email, password });
      window.UI.setStatus(authStatusMsg, '🟢 Hesap başarıyla oluşturuldu!', 'success');
      window.UI.showToast('Hesabınız oluşturuldu ve giriş yapıldı.', 'success');
      loadUserTelegramSettings();
      loadDashboard();
      loadMonitors(1, currentMonitorsPageSize);
      loadUserProfileSettings();
      loadInspectorUsageInfo();
    } catch (err) {
      window.UI.setStatus(authStatusMsg, `🔴 ${err.message}`, 'error');
      window.UI.showToast(err.message, 'error');
    } finally {
      if (submitBtn) submitBtn.disabled = false;
    }
  });

  // ── Logout ───────────────────────────────────────────────────────────────
  const performLogout = async () => {
    await window.AuthManager.logout();
    window.UI.showToast('Çıkış yapıldı.', 'info');
  };

  if (logoutBtn) logoutBtn.addEventListener('click', performLogout);
  if (topbarLogoutBtn) topbarLogoutBtn.addEventListener('click', performLogout);

  // ── Auth State Change Listener ───────────────────────────────────────────
  window.AuthManager.onAuthStateChanged((isAuthenticated, user) => {
    if (isAuthenticated && user) {
      if (authViewWrapper) authViewWrapper.style.display = 'none';
      if (appShellWrapper) appShellWrapper.style.display = 'flex';

      updateUserBadgeDisplay(user);

      if (authLoggedOut) authLoggedOut.style.display = 'none';
      if (authLoggedIn) authLoggedIn.style.display = 'flex';

      loadUserProfileSettings();
      loadDashboard();
      loadMonitors(1, currentMonitorsPageSize);
      loadInspectorUsageInfo();
    } else {
      if (authViewWrapper) authViewWrapper.style.display = 'flex';
      if (appShellWrapper) appShellWrapper.style.display = 'none';
      if (authLoggedOut) authLoggedOut.style.display = 'block';
      if (authLoggedIn) authLoggedIn.style.display = 'none';
    }
  });

  function updateUserBadgeDisplay(user) {
    if (!user) return;
    const fn = user.firstName || user.FirstName || '';
    const ln = user.lastName || user.LastName || '';
    const email = user.email || user.Email || '';
    const fullName = `${fn} ${ln}`.trim() || email;
    const initials = (fn ? fn[0] : '') + (ln ? ln[0] : 'U');

    if (currentUserName) currentUserName.textContent = `👤 ${fullName}`;
    if (currentUserEmail) currentUserEmail.textContent = `(${email})`;
    if (sidebarUserName) sidebarUserName.textContent = fullName;
    if (sidebarUserEmail) sidebarUserEmail.textContent = email;
    if (sidebarUserAvatar) sidebarUserAvatar.textContent = initials.toUpperCase();
  }

  // ── 1. Dashboard View Logic ──────────────────────────────────────────────
  if (refreshDashboardBtn) {
    refreshDashboardBtn.addEventListener('click', loadDashboard);
  }

  async function loadDashboard() {
    if (!window.AuthManager.isAuthenticated()) return;

    try {
      const [summary, usage] = await Promise.all([
        window.apiClient.get('/api/dashboard/summary'),
        window.apiClient.get('/api/subscriptions/usage')
      ]);

      if (summary) {
        const total = summary.totalMonitors ?? 0;
        const active = summary.activeMonitors ?? 0;
        const paused = summary.pausedMonitors ?? 0;
        const available = summary.availableItems ?? 0;
        const notifs = summary.notificationsToday ?? 0;

        const maxTotal = usage?.limits?.maxTotalMonitors ?? 10;
        const maxActive = usage?.limits?.maxActiveMonitors ?? 5;
        const maxNotifs = usage?.limits?.maxNotificationsPerDay ?? 20;
        const planName = usage?.plan ?? 'FREE';
        const minInterval = usage?.limits?.minCheckIntervalMinutes ?? 60;

        if (dashTotalMonitors) dashTotalMonitors.textContent = `${total} / ${maxTotal}`;
        if (dashActiveMonitors) dashActiveMonitors.textContent = `${active} / ${maxActive}`;
        if (dashPausedMonitors) dashPausedMonitors.textContent = `${paused}`;
        if (dashAvailableItems) dashAvailableItems.textContent = `${available}`;
        if (dashNotifications) dashNotifications.textContent = `${notifs} / ${maxNotifs}`;
        if (dashPlanInfo) dashPlanInfo.textContent = `${planName} Planı (${minInterval} dk)`;

        if (dashActiveBar && maxActive > 0) {
          const pct = Math.min(100, Math.round((active / maxActive) * 100));
          dashActiveBar.style.width = `${pct}%`;
        }

        if (dashOverviewContainer) {
          if (total === 0) {
            dashOverviewContainer.innerHTML = `
              <div class="empty-state-box">
                <div class="empty-state-icon">🔍</div>
                <div class="empty-state-message">Henüz takip ettiğiniz bir ürün bulunmuyor.</div>
                <button type="button" class="btn-primary" id="dash-start-cta-btn" style="margin-top:1rem;">
                  Yeni Ürün İncele ve Takip Başlat
                </button>
              </div>
            `;
            const cta = document.getElementById('dash-start-cta-btn');
            if (cta) cta.addEventListener('click', () => window.UI.switchView('inspector'));
          } else {
            dashOverviewContainer.innerHTML = `
              <div style="font-size:0.88rem; color:#475569; line-height:1.6;">
                <div>🟢 <strong>Sistem Durumu:</strong> Arka plan takip motoru aktif ve çalışıyor.</div>
                <div>🔔 <strong>Son Stok Bildirimi:</strong> ${summary.lastNotificationAt ? new Date(summary.lastNotificationAt).toLocaleString('tr-TR') : 'Henüz bildirim gönderilmedi'}</div>
                <div style="margin-top:0.8rem;">
                  <button type="button" class="btn-outline-primary" id="dash-view-all-monitors-btn" style="font-size:0.85rem; padding:0.4rem 0.8rem;">
                    Tüm Takipleri Görüntüle &rarr;
                  </button>
                </div>
              </div>
            `;
            const viewAll = document.getElementById('dash-view-all-monitors-btn');
            if (viewAll) viewAll.addEventListener('click', () => window.UI.switchView('monitors'));
          }
        }
      }
    } catch (err) {
      console.warn('Dashboard load error:', err);
    }
  }

  // ── 2. Monitors Management & Pagination Logic ────────────────────────────
  if (refreshMonitorsBtn) {
    refreshMonitorsBtn.addEventListener('click', () => loadMonitors(currentMonitorsPage, currentMonitorsPageSize));
  }

  if (prevPageBtn) {
    prevPageBtn.addEventListener('click', () => {
      if (currentMonitorsPage > 1) {
        loadMonitors(currentMonitorsPage - 1, currentMonitorsPageSize);
      }
    });
  }

  if (nextPageBtn) {
    nextPageBtn.addEventListener('click', () => {
      loadMonitors(currentMonitorsPage + 1, currentMonitorsPageSize);
    });
  }

  if (pageSizeSelect) {
    pageSizeSelect.addEventListener('change', (e) => {
      currentMonitorsPageSize = parseInt(e.target.value, 10) || 20;
      loadMonitors(1, currentMonitorsPageSize);
    });
  }

  if (monitorsSearchInput) {
    monitorsSearchInput.addEventListener('input', (e) => {
      const q = e.target.value.trim().toLowerCase();
      if (!q) {
        renderMonitorsList(cachedMonitors);
        return;
      }
      const filtered = cachedMonitors.filter(m =>
        (m.productName && m.productName.toLowerCase().includes(q)) ||
        (m.store && m.store.toLowerCase().includes(q)) ||
        (m.selectedVariants && m.selectedVariants.some(v => v.toLowerCase().includes(q)))
      );
      renderMonitorsList(filtered);
    });
  }

  async function loadMonitors(page = 1, pageSize = 20) {
    if (!window.AuthManager.isAuthenticated()) {
      window.UI.renderEmptyState(monitorsContainer, 'Takip kayıtlarınızı görmek için lütfen giriş yapın.', '🔒');
      return;
    }

    currentMonitorsPage = page;
    currentMonitorsPageSize = pageSize;

    try {
      window.UI.setStatus(monitorsContainer, 'Takipler yükleniyor...', 'loading', true);
      const res = await window.apiClient.get(`/api/monitors?page=${page}&pageSize=${pageSize}`);

      const items = res?.items ?? (Array.isArray(res) ? res : []);
      cachedMonitors = items;

      renderMonitorsList(items);
      updatePaginationControls(res);
    } catch (err) {
      window.UI.renderErrorState(monitorsContainer, err, () => loadMonitors(page, pageSize));
    }
  }

  function updatePaginationControls(paged) {
    if (!paged || !paginationInfo) return;

    const totalCount = paged.totalCount ?? cachedMonitors.length;
    const page = paged.page ?? currentMonitorsPage;
    const totalPages = paged.totalPages ?? (totalCount > 0 ? Math.ceil(totalCount / currentMonitorsPageSize) : 1);
    const hasPrev = paged.hasPreviousPage ?? (page > 1);
    const hasNext = paged.hasNextPage ?? (page < totalPages);

    paginationInfo.textContent = `Toplam ${totalCount} kayıttan Sayfa ${page} / ${totalPages}`;
    if (prevPageBtn) prevPageBtn.disabled = !hasPrev;
    if (nextPageBtn) nextPageBtn.disabled = !hasNext;
  }

  function renderMonitorsList(monitors) {
    if (!monitors || monitors.length === 0) {
      window.UI.renderEmptyState(monitorsContainer, 'Henüz kayıtlı bir stok takibi bulunmuyor.', '📦');
      return;
    }

    monitorsContainer.innerHTML = '';

    monitors.forEach(m => {
      const item = document.createElement('div');
      item.className = 'monitor-item';

      const header = document.createElement('div');
      header.className = 'monitor-header';

      const mainInfo = document.createElement('div');
      mainInfo.className = 'monitor-main-info';

      const thumbImg = document.createElement('img');
      thumbImg.className = 'monitor-thumb';
      thumbImg.src = m.imageUrl || 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="50" height="70" viewBox="0 0 50 70"><rect width="50" height="70" fill="%23e2e8f0"/><text x="50%" y="50%" dominant-baseline="middle" text-anchor="middle" fill="%2394a3b8" font-size="10">Görsel Yok</text></svg>';
      thumbImg.alt = m.productName;

      const titleWrap = document.createElement('div');
      const storeBadge = document.createElement('span');
      storeBadge.className = 'badge-store';
      storeBadge.textContent = m.store || 'MAĞAZA';

      const titleLink = document.createElement('a');
      titleLink.className = 'monitor-title-link';
      titleLink.href = m.productUrl;
      titleLink.target = '_blank';
      titleLink.rel = 'noopener';
      titleLink.textContent = m.productName;

      const variantsWrap = document.createElement('div');
      (m.selectedVariants || []).forEach(v => {
        const vTag = document.createElement('span');
        vTag.className = 'variant-tag';
        vTag.textContent = v;
        variantsWrap.appendChild(vTag);
      });

      titleWrap.appendChild(storeBadge);
      titleWrap.appendChild(titleLink);
      titleWrap.appendChild(variantsWrap);

      mainInfo.appendChild(thumbImg);
      mainInfo.appendChild(titleWrap);

      const statusBadge = document.createElement('span');
      statusBadge.className = m.isActive ? 'badge-active' : 'badge-stopped';
      statusBadge.textContent = m.isActive ? '🟢 Takip Aktif' : '⚪ Durduruldu';

      header.appendChild(mainInfo);
      header.appendChild(statusBadge);

      const detailsGrid = document.createElement('div');
      detailsGrid.className = 'monitor-details-grid';

      const lastCheckText = m.lastCheckedAt
        ? `${new Date(m.lastCheckedAt).toLocaleTimeString('tr-TR')} (${m.lastCheckStatus === 'Success' ? '🟢 Başarılı' : '🔴 ' + (m.lastCheckError || 'Hata')})`
        : 'Henüz yapılmadı';

      const nextCheckText = m.nextCheckAt
        ? new Date(m.nextCheckAt).toLocaleTimeString('tr-TR')
        : '—';

      const lastNotifiedText = m.lastNotifiedAt
        ? `${m.lastNotifiedVariant ? m.lastNotifiedVariant + ' — ' : ''}${new Date(m.lastNotifiedAt).toLocaleTimeString('tr-TR')}`
        : 'Henüz bildirim yok';

      detailsGrid.innerHTML = `
        <div><strong>Kontrol Sıklığı:</strong> ${m.checkIntervalMinutes} dakikada bir</div>
        <div><strong>Son Kontrol:</strong> ${window.UI.escapeHtml(lastCheckText)}</div>
        <div><strong>Sonraki Kontrol:</strong> ${window.UI.escapeHtml(nextCheckText)}</div>
        <div><strong>Son Bildirim:</strong> 🔔 ${window.UI.escapeHtml(lastNotifiedText)}</div>
      `;

      const actions = document.createElement('div');
      actions.className = 'monitor-actions';

      const checkBtn = document.createElement('button');
      checkBtn.className = 'btn-outline-primary';
      checkBtn.textContent = '🔄 Kontrol Et';
      checkBtn.onclick = () => manualCheck(m.id, checkBtn);
      actions.appendChild(checkBtn);

      if (m.isActive) {
        const pauseBtn = document.createElement('button');
        pauseBtn.className = 'btn-outline-warning';
        pauseBtn.textContent = '⏸ Durdur';
        pauseBtn.onclick = () => toggleMonitorStatus(m.id, 'pause');
        actions.appendChild(pauseBtn);
      } else {
        const resumeBtn = document.createElement('button');
        resumeBtn.className = 'btn-outline-success';
        resumeBtn.textContent = '▶ Başlat';
        resumeBtn.onclick = () => toggleMonitorStatus(m.id, 'resume');
        actions.appendChild(resumeBtn);
      }

      const editBtn = document.createElement('button');
      editBtn.className = 'btn-secondary';
      editBtn.textContent = '✏️ Düzenle';
      editBtn.onclick = () => openEditModal(m);
      actions.appendChild(editBtn);

      const deleteBtn = document.createElement('button');
      deleteBtn.className = 'btn-outline-danger';
      deleteBtn.textContent = '🗑 Sil';
      deleteBtn.onclick = () => deleteMonitor(m.id);
      actions.appendChild(deleteBtn);

      item.appendChild(header);
      item.appendChild(detailsGrid);
      item.appendChild(actions);
      monitorsContainer.appendChild(item);
    });
  }

  // ── 3. Edit Monitor Modal ────────────────────────────────────────────────
  function openEditModal(monitor) {
    if (!editModal) return;
    editMonitorId.value = monitor.id;
    if (editProductName) editProductName.textContent = `${monitor.store} — ${monitor.productName}`;
    if (editVariantsInput) editVariantsInput.value = (monitor.selectedVariants || []).join(', ');
    if (editIntervalSelect) editIntervalSelect.value = monitor.checkIntervalMinutes || 60;
    if (editChatIdInput) editChatIdInput.value = '';
    window.UI.setStatus(editStatusMsg, '');
    editModal.classList.add('active');
  }

  function closeEditModal() {
    if (editModal) editModal.classList.remove('active');
  }

  if (editCloseBtn) editCloseBtn.addEventListener('click', closeEditModal);
  if (editCancelBtn) editCancelBtn.addEventListener('click', closeEditModal);

  if (editForm) {
    editForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const id = editMonitorId.value;
      const variantsRaw = editVariantsInput.value;
      const variants = variantsRaw.split(',').map(s => s.trim()).filter(Boolean);
      const interval = parseInt(editIntervalSelect.value, 10) || 60;
      const chatId = editChatIdInput.value.trim();

      if (variants.length === 0) {
        window.UI.setStatus(editStatusMsg, 'En az bir beden/varyant girmelisiniz.', 'error');
        return;
      }

      window.UI.setStatus(editStatusMsg, 'Kaydediliyor...', 'loading', true);

      try {
        const payload = {
          selectedVariants: variants,
          checkIntervalMinutes: interval
        };
        if (chatId) {
          payload.telegramChatId = chatId;
        }

        await window.apiClient.put(`/api/monitors/${id}`, payload);
        window.UI.showToast('Takip başarıyla güncellendi.', 'success');
        closeEditModal();
        loadMonitors(currentMonitorsPage, currentMonitorsPageSize);
        loadDashboard();
      } catch (err) {
        window.UI.setStatus(editStatusMsg, err.message, 'error');
        window.UI.showToast(err.message, 'error');
      }
    });
  }

  // ── 4. Manual Check, Pause/Resume, Delete ────────────────────────────────
  async function manualCheck(id, btn) {
    const originalText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Kontrol ediliyor...';

    try {
      const data = await window.apiClient.post(`/api/monitors/${id}/check`, {});
      let msg = `✅ Kontrol tamamlandı. Durum: ${data.status}`;
      if (data.changes && data.changes.length > 0) {
        msg += `\nDeğişiklik: ` + data.changes.map(c => `${c.variantName} (${c.previousAvailability ? 'VAR' : 'YOK'} -> ${c.currentAvailability ? 'VAR' : 'YOK'})`).join(', ');
      } else {
        msg += `\nStok durumunda bir değişiklik yok.`;
      }
      window.UI.showToast(msg, 'success');
      loadMonitors(currentMonitorsPage, currentMonitorsPageSize);
      loadDashboard();
    } catch (err) {
      window.UI.showToast(`Kontrol başarısız: ${err.message}`, 'error');
    } finally {
      btn.disabled = false;
      btn.textContent = originalText;
    }
  }

  async function toggleMonitorStatus(id, action) {
    try {
      await window.apiClient.post(`/api/monitors/${id}/${action}`, {});
      window.UI.showToast(`Takip durumu güncellendi (${action === 'pause' ? 'Durduruldu' : 'Başlatıldı'}).`, 'info');
      loadMonitors(currentMonitorsPage, currentMonitorsPageSize);
      loadDashboard();
    } catch (err) {
      window.UI.showToast(err.message, 'error');
    }
  }

  async function deleteMonitor(id) {
    if (!confirm('Bu stok takibini silmek istediğinizden emin misiniz?')) return;
    try {
      await window.apiClient.delete(`/api/monitors/${id}`);
      window.UI.showToast('Takip kaydı başarıyla silindi.', 'info');
      loadMonitors(currentMonitorsPage, currentMonitorsPageSize);
      loadDashboard();
    } catch (err) {
      window.UI.showToast(err.message, 'error');
    }
  }

  // ── 5. Product Inspection & Monitor Creation Logic ───────────────────────
  async function loadInspectorUsageInfo() {
    if (!window.AuthManager.isAuthenticated()) return;
    try {
      const usage = await window.apiClient.get('/api/subscriptions/usage');
      if (usage && monitorUsageBadge) {
        const active = usage.activeMonitors ?? 0;
        const maxActive = usage.limits?.maxActiveMonitors ?? 5;
        monitorUsageBadge.textContent = `Aktif Takip: ${active} / ${maxActive}`;
      }
    } catch (err) {
      console.warn('Failed to load usage for inspector:', err);
    }
  }

  if (fetchBtn) fetchBtn.addEventListener('click', fetchProduct);
  if (urlInput) urlInput.addEventListener('keydown', e => { if (e.key === 'Enter') fetchProduct(); });

  if (selectAllVariantsBtn) {
    selectAllVariantsBtn.addEventListener('click', () => {
      const checkboxes = document.querySelectorAll('#variant-grid input[type="checkbox"]');
      checkboxes.forEach(cb => {
        cb.checked = true;
        cb.closest('.variant-row')?.classList.add('selected');
      });
    });
  }

  if (deselectAllVariantsBtn) {
    deselectAllVariantsBtn.addEventListener('click', () => {
      const checkboxes = document.querySelectorAll('#variant-grid input[type="checkbox"]');
      checkboxes.forEach(cb => {
        cb.checked = false;
        cb.closest('.variant-row')?.classList.remove('selected');
      });
    });
  }

  async function fetchProduct() {
    const rawUrl = urlInput.value.trim();
    if (!rawUrl) {
      window.UI.setStatus(fetchStatusMsg, 'Lütfen bir ürün URL\'si girin.', 'error');
      return;
    }

    if (!rawUrl.startsWith('http://') && !rawUrl.startsWith('https://')) {
      window.UI.setStatus(fetchStatusMsg, 'Geçerli bir web adresi (http:// veya https://) girin.', 'error');
      return;
    }

    window.UI.setStatus(fetchStatusMsg, 'Ürün bilgileri ve bedenler alınıyor…', 'loading', true);
    fetchBtn.disabled = true;
    hideProduct();

    try {
      const data = await window.apiClient.post('/api/products/inspect', { url: rawUrl });
      currentProductData = data;
      window.UI.setStatus(fetchStatusMsg, '');
      renderProduct(data);
      window.UI.showToast('Ürün başarıyla incelendi.', 'success');
      loadInspectorUsageInfo();
    } catch (err) {
      console.error(err);
      window.UI.setStatus(fetchStatusMsg, err.message, 'error');
      window.UI.showToast(err.message, 'error');
    } finally {
      fetchBtn.disabled = false;
    }
  }

  function renderProduct(data) {
    const storeName = (data.store || 'MAĞAZA').toUpperCase();
    document.getElementById('product-store').textContent = `🟢 ${storeName}`;
    document.getElementById('product-name').textContent = data.name ?? '—';

    const img = document.getElementById('product-img');
    if (data.imageUrl) {
      img.src = data.imageUrl;
      img.className = 'visible';
    } else {
      img.className = '';
    }

    // Inspect Status & Message Handling
    if (inspectStatusBanner) {
      const status = (data.inspectStatus || 'success').toLowerCase();
      const userMsg = data.userMessage || '';

      if (status === 'incomplete') {
        inspectStatusBanner.className = 'inspect-banner inspect-banner-warning';
        inspectStatusBanner.innerHTML = `⚠️ <strong>Kısmi Bilgi:</strong> ${window.UI.escapeHtml(userMsg || 'Bazı varyant bilgileri eksik okunmuş olabilir.')}`;
        inspectStatusBanner.style.display = 'flex';
      } else if (status === 'blocked') {
        inspectStatusBanner.className = 'inspect-banner inspect-banner-error';
        inspectStatusBanner.innerHTML = `🔴 <strong>Erişim Engellendi:</strong> ${window.UI.escapeHtml(userMsg || 'Mağaza güvenlik duvarı/bot koruması devreye girdi.')}`;
        inspectStatusBanner.style.display = 'flex';
      } else if (status === 'not_found') {
        inspectStatusBanner.className = 'inspect-banner inspect-banner-error';
        inspectStatusBanner.innerHTML = `🔴 <strong>Ürün Bulunamadı:</strong> ${window.UI.escapeHtml(userMsg || 'Ürün sayfası mevcut değil.')}`;
        inspectStatusBanner.style.display = 'flex';
      } else if (userMsg) {
        inspectStatusBanner.className = 'inspect-banner inspect-banner-info';
        inspectStatusBanner.innerHTML = `ℹ️ ${window.UI.escapeHtml(userMsg)}`;
        inspectStatusBanner.style.display = 'flex';
      } else {
        inspectStatusBanner.style.display = 'none';
      }
    }

    const grid = document.getElementById('variant-grid');
    const placeholder = document.getElementById('variants-placeholder');
    grid.innerHTML = '';

    const variants = data.variants ?? [];
    if (variants.length === 0) {
      placeholder.style.display = 'block';
      grid.style.display = 'none';
    } else {
      placeholder.style.display = 'none';
      grid.style.display = 'flex';

      variants.forEach(v => {
        const row = document.createElement('label');
        row.className = `variant-row ${v.available ? 'in-stock' : 'out-stock'}`;

        const cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.value = v.name;
        cb.checked = true; // Default selected

        cb.addEventListener('change', () => {
          row.classList.toggle('selected', cb.checked);
        });
        row.classList.add('selected');

        const dot = document.createElement('span');
        dot.className = 'variant-dot';
        dot.textContent = v.available ? '🟢' : '🔴';

        const size = document.createElement('span');
        size.className = 'variant-size';
        size.textContent = v.name;

        const status = document.createElement('span');
        status.className = 'variant-status';
        status.textContent = v.available ? 'Stokta' : 'Stok yok (Tükendi)';

        row.appendChild(cb);
        row.appendChild(dot);
        row.appendChild(size);
        row.appendChild(status);
        grid.appendChild(row);
      });
    }

    emptyCard.style.display = 'none';
    productCard.style.display = 'block';
  }

  function hideProduct() {
    currentProductData = null;
    if (productCard) productCard.style.display = 'none';
    if (emptyCard) emptyCard.style.display = 'block';
    if (inspectStatusBanner) inspectStatusBanner.style.display = 'none';
  }

  // ── 6. Inspector Telegram Setup ──────────────────────────────────────────
  let userSavedTelegramData = null;

  function updateBotChoiceUI(mode) {
    if (mode === 'saved') {
      if (tgOptionSavedWrapper) {
        tgOptionSavedWrapper.style.borderColor = '#3b82f6';
        tgOptionSavedWrapper.style.backgroundColor = '#eff6ff';
      }
      if (tgOptionCustomWrapper) {
        tgOptionCustomWrapper.style.borderColor = '#cbd5e1';
        tgOptionCustomWrapper.style.backgroundColor = '#ffffff';
      }
      if (tgCustomFields) tgCustomFields.style.display = 'none';
      if (userSavedTelegramData?.chatId) {
        window.UI.setStatus(tgStatusMsg, `🟢 Kayıtlı bot hazır (${userSavedTelegramData.chatId})`, 'success');
      } else {
        window.UI.setStatus(tgStatusMsg, '⚪ Durum: Bağlı değil', 'neutral');
      }
    } else {
      if (tgOptionSavedWrapper) {
        tgOptionSavedWrapper.style.borderColor = '#cbd5e1';
        tgOptionSavedWrapper.style.backgroundColor = '#ffffff';
      }
      if (tgOptionCustomWrapper) {
        tgOptionCustomWrapper.style.borderColor = '#3b82f6';
        tgOptionCustomWrapper.style.backgroundColor = '#eff6ff';
      }
      if (tgCustomFields) tgCustomFields.style.display = 'block';
      window.UI.setStatus(tgStatusMsg, '⚪ Özel bot bilgileri giriniz.', 'neutral');
    }
  }

  if (tgChoiceSaved) {
    tgChoiceSaved.addEventListener('change', () => {
      if (tgChoiceSaved.checked) updateBotChoiceUI('saved');
    });
  }

  if (tgChoiceCustom) {
    tgChoiceCustom.addEventListener('change', () => {
      if (tgChoiceCustom.checked) updateBotChoiceUI('custom');
    });
  }

  if (tgLinkToSettings) {
    tgLinkToSettings.addEventListener('click', (e) => {
      e.preventDefault();
      window.UI.switchView('telegram');
      loadDedicatedTelegramSettings();
    });
  }

  if (tgTestBtn) tgTestBtn.addEventListener('click', testTelegram);

  async function testTelegram() {
    const isSavedMode = Boolean(tgChoiceSaved && tgChoiceSaved.checked && userSavedTelegramData?.isConfigured);
    let botToken = '';
    let chatId = '';

    if (isSavedMode) {
      chatId = userSavedTelegramData.chatId;
    } else {
      botToken = tgTokenInput.value.trim();
      chatId = tgChatIdInput.value.trim();
      if (!botToken || !chatId) {
        window.UI.setStatus(tgStatusMsg, '🔴 Lütfen Özel Bot Token ve Chat ID girin.', 'error');
        return;
      }
    }

    window.UI.setStatus(tgStatusMsg, 'Telegram bağlantısı test ediliyor...', 'loading', true);
    tgTestBtn.disabled = true;

    try {
      const data = await window.apiClient.post('/api/telegram/test', { botToken, chatId });
      if (data && data.success) {
        window.UI.setStatus(tgStatusMsg, '🟢 Telegram bağlantısı başarılı!', 'success');
        window.UI.showToast('Telegram bağlantısı başarılı!', 'success');
      } else {
        const msg = data?.message || 'Telegram bağlantısı başarısız.';
        window.UI.setStatus(tgStatusMsg, `🔴 ${msg}`, 'error');
        window.UI.showToast(msg, 'error');
      }
    } catch (err) {
      window.UI.setStatus(tgStatusMsg, `🔴 ${err.message}`, 'error');
      window.UI.showToast(err.message, 'error');
    } finally {
      tgTestBtn.disabled = false;
    }
  }

  async function loadUserTelegramSettings() {
    if (!window.AuthManager.isAuthenticated()) return;
    try {
      const data = await window.apiClient.get('/api/users/me/telegram');
      userSavedTelegramData = data;

      if (data && data.isConfigured && data.chatId) {
        if (tgOptionSavedWrapper) tgOptionSavedWrapper.style.display = 'flex';
        if (tgSavedBotTitle) {
          tgSavedBotTitle.textContent = `🟢 Kayıtlı Profil Botum (${data.maskedBotToken || '******'})`;
        }
        if (tgSavedBotChatIdLabel) {
          tgSavedBotChatIdLabel.textContent = data.chatId;
        }
        if (tgChoiceSaved) tgChoiceSaved.checked = true;
        updateBotChoiceUI('saved');
        if (tgNoSavedBotHint) tgNoSavedBotHint.style.display = 'none';
      } else {
        if (tgOptionSavedWrapper) tgOptionSavedWrapper.style.display = 'none';
        if (tgChoiceCustom) tgChoiceCustom.checked = true;
        updateBotChoiceUI('custom');
        if (tgNoSavedBotHint) tgNoSavedBotHint.style.display = 'block';
      }
    } catch (err) {
      console.warn('Failed to load user Telegram settings:', err);
    }
  }

  // ── 7. Start Monitoring from Inspector ───────────────────────────────────
  if (startMonitorBtn) {
    startMonitorBtn.addEventListener('click', () => {
      window.AuthManager.requireAuth(createMonitor);
    });
  }

  async function createMonitor() {
    if (!currentProductData) {
      window.UI.setStatus(startMonitorStatus, '🔴 Lütfen önce geçerli bir ürün URL\'si girip ürünü inceleyin.', 'error');
      return;
    }

    const checkboxes = document.querySelectorAll('#variant-grid input[type="checkbox"]:checked');
    const selectedVariants = Array.from(checkboxes).map(c => c.value);

    if (selectedVariants.length === 0) {
      window.UI.setStatus(startMonitorStatus, '🔴 Lütfen takip edilecek en az bir beden seçin.', 'error');
      window.UI.showToast('En az bir beden seçilmesi gerekir.', 'warning');
      return;
    }

    const isSavedMode = Boolean(tgChoiceSaved && tgChoiceSaved.checked && userSavedTelegramData?.isConfigured);
    let botToken = '';
    let chatId = '';

    if (isSavedMode) {
      chatId = userSavedTelegramData.chatId;
    } else {
      botToken = tgTokenInput.value.trim();
      chatId = tgChatIdInput.value.trim();
      if (!botToken || !chatId) {
        window.UI.setStatus(startMonitorStatus, '🔴 Lütfen Telegram Bot Token ve Chat ID alanlarını doldurun.', 'error');
        return;
      }
    }

    const checkIntervalMinutes = parseInt(intervalSelect.value, 10) || 60;

    window.UI.setStatus(startMonitorStatus, 'Takip kaydı oluşturuluyor...', 'loading', true);
    startMonitorBtn.disabled = true;

    try {
      const payload = {
        productUrl: currentProductData.url,
        store: currentProductData.store || 'Zara',
        productName: currentProductData.name,
        imageUrl: currentProductData.imageUrl,
        selectedVariants: selectedVariants,
        telegramBotToken: botToken,
        telegramChatId: chatId,
        checkIntervalMinutes: checkIntervalMinutes
      };

      await window.apiClient.post('/api/monitors', payload);
      window.UI.setStatus(startMonitorStatus, '🟢 Takip başarıyla başlatıldı!', 'success');
      window.UI.showToast('Ürün takibi başarıyla başlatıldı!', 'success');

      loadMonitors(1, currentMonitorsPageSize);
      loadDashboard();
      loadInspectorUsageInfo();
      setTimeout(() => window.UI.switchView('monitors'), 800);
    } catch (err) {
      console.error(err);
      window.UI.setStatus(startMonitorStatus, `🔴 ${err.message}`, 'error');
      window.UI.showToast(err.message, 'error');
    } finally {
      startMonitorBtn.disabled = false;
    }
  }

  // ── 8. Notification History Logic ────────────────────────────────────────
  async function loadSupportedStores() {
    if (supportedStoresLoaded || !notifFilterStore) return;
    try {
      const stores = await window.apiClient.get('/api/products/stores');
      if (Array.isArray(stores)) {
        notifFilterStore.innerHTML = '<option value="">Tüm Mağazalar</option>';
        stores.forEach(s => {
          const storeName = typeof s === 'object' && s !== null ? (s.name || s.displayName) : String(s);
          const opt = document.createElement('option');
          opt.value = storeName;
          opt.textContent = storeName;
          notifFilterStore.appendChild(opt);
        });
        supportedStoresLoaded = true;
      }
    } catch (err) {
      console.warn('Failed to load stores for notification filter:', err);
    }
  }

  if (notifFilterBtn) {
    notifFilterBtn.addEventListener('click', () => loadNotifications(1, currentNotifsPageSize));
  }

  if (notifResetBtn) {
    notifResetBtn.addEventListener('click', () => {
      if (notifFilterStore) notifFilterStore.value = '';
      if (notifFilterDateFrom) notifFilterDateFrom.value = '';
      if (notifFilterDateTo) notifFilterDateTo.value = '';
      loadNotifications(1, currentNotifsPageSize);
    });
  }

  if (notifPrevPageBtn) {
    notifPrevPageBtn.addEventListener('click', () => {
      if (currentNotifsPage > 1) {
        loadNotifications(currentNotifsPage - 1, currentNotifsPageSize);
      }
    });
  }

  if (notifNextPageBtn) {
    notifNextPageBtn.addEventListener('click', () => {
      loadNotifications(currentNotifsPage + 1, currentNotifsPageSize);
    });
  }

  if (notifPageSizeSelect) {
    notifPageSizeSelect.addEventListener('change', (e) => {
      currentNotifsPageSize = parseInt(e.target.value, 10) || 20;
      loadNotifications(1, currentNotifsPageSize);
    });
  }

  async function loadNotifications(page = 1, pageSize = 20) {
    if (!window.AuthManager.isAuthenticated()) {
      window.UI.renderEmptyState(notificationsContainer, 'Bildirim geçmişinizi görmek için lütfen giriş yapın.', '🔒');
      return;
    }

    currentNotifsPage = page;
    currentNotifsPageSize = pageSize;

    let query = `?page=${page}&pageSize=${pageSize}`;
    if (notifFilterStore && notifFilterStore.value) {
      query += `&store=${encodeURIComponent(notifFilterStore.value)}`;
    }
    if (notifFilterDateFrom && notifFilterDateFrom.value) {
      query += `&dateFrom=${encodeURIComponent(notifFilterDateFrom.value)}`;
    }
    if (notifFilterDateTo && notifFilterDateTo.value) {
      query += `&dateTo=${encodeURIComponent(notifFilterDateTo.value)}`;
    }

    try {
      window.UI.setStatus(notificationsContainer, 'Bildirimler yükleniyor...', 'loading', true);
      const res = await window.apiClient.get(`/api/notifications${query}`);

      const items = res?.items ?? (Array.isArray(res) ? res : []);
      renderNotificationsList(items);
      updateNotifPaginationControls(res);
    } catch (err) {
      window.UI.renderErrorState(notificationsContainer, err, () => loadNotifications(page, pageSize));
    }
  }

  function updateNotifPaginationControls(paged) {
    if (!paged || !notifPaginationInfo) return;

    const totalCount = paged.totalCount ?? 0;
    const page = paged.page ?? currentNotifsPage;
    const totalPages = paged.totalPages ?? (totalCount > 0 ? Math.ceil(totalCount / currentNotifsPageSize) : 1);
    const hasPrev = paged.hasPreviousPage ?? (page > 1);
    const hasNext = paged.hasNextPage ?? (page < totalPages);

    notifPaginationInfo.textContent = `Toplam ${totalCount} bildirimden Sayfa ${page} / ${totalPages}`;
    if (notifPrevPageBtn) notifPrevPageBtn.disabled = !hasPrev;
    if (notifNextPageBtn) notifNextPageBtn.disabled = !hasNext;
  }

  function renderNotificationsList(notifs) {
    if (!notifs || notifs.length === 0) {
      notificationsContainer.innerHTML = `
        <div class="empty-state-box">
          <div class="empty-state-icon">📬</div>
          <div class="empty-state-message">Henüz iletilen bir stok bildirimi bulunmuyor.</div>
          <div style="font-size:0.8rem; color:#64748b; margin-top:0.4rem;">
            Takip ettiğiniz ürünlerin bedenlerinde stok tespit edildiğinde Telegram alarmlarınız burada listelenecektir.
          </div>
        </div>
      `;
      return;
    }

    notificationsContainer.innerHTML = '';

    notifs.forEach(n => {
      const item = document.createElement('div');
      item.className = 'notification-item';

      const header = document.createElement('div');
      header.className = 'notification-header';

      const mainInfo = document.createElement('div');
      mainInfo.className = 'notification-main-info';

      const thumbImg = document.createElement('img');
      thumbImg.className = 'notification-thumb';
      thumbImg.src = n.productImageUrl || 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="50" height="70" viewBox="0 0 50 70"><rect width="50" height="70" fill="%23e2e8f0"/><text x="50%" y="50%" dominant-baseline="middle" text-anchor="middle" fill="%2394a3b8" font-size="10">Görsel Yok</text></svg>';
      thumbImg.alt = n.productName;

      const titleWrap = document.createElement('div');
      const storeBadge = document.createElement('span');
      storeBadge.className = 'badge-store';
      storeBadge.textContent = n.store || 'MAĞAZA';

      const prodTitle = document.createElement('div');
      prodTitle.style.fontWeight = '700';
      prodTitle.style.fontSize = '0.98rem';
      prodTitle.style.color = '#0f172a';
      prodTitle.textContent = n.productName;

      const variantTag = document.createElement('span');
      variantTag.className = 'variant-tag';
      variantTag.textContent = `Beden: ${n.variantName}`;

      titleWrap.appendChild(storeBadge);
      titleWrap.appendChild(prodTitle);
      titleWrap.appendChild(variantTag);

      mainInfo.appendChild(thumbImg);
      mainInfo.appendChild(titleWrap);

      const statusBadge = document.createElement('span');
      statusBadge.className = n.success ? 'badge-active' : 'badge-stopped';
      statusBadge.textContent = n.success ? '🟢 İletildi (Telegram)' : '🔴 İletilemedi';

      header.appendChild(mainInfo);
      header.appendChild(statusBadge);

      const detailsGrid = document.createElement('div');
      detailsGrid.className = 'notification-details-grid';

      const sentTime = n.notificationSentAt ? new Date(n.notificationSentAt).toLocaleString('tr-TR') : '—';
      const stockChange = `${n.previousAvailability ? '🟢 Stokta' : '🔴 Stok Yok'} → ${n.currentAvailability ? '🟢 Stokta' : '🔴 Stok Yok'}`;

      detailsGrid.innerHTML = `
        <div><strong>Tarih / Saat:</strong> ${sentTime}</div>
        <div><strong>Stok Değişimi:</strong> ${stockChange}</div>
        <div><strong>Telegram Durumu:</strong> ${n.success ? '✅ Gönderildi' : '❌ Hata: ' + window.UI.escapeHtml(n.error || 'Bilinmeyen hata')}</div>
        <div><strong>Monitor ID:</strong> #${n.monitorId}</div>
      `;

      item.appendChild(header);
      item.appendChild(detailsGrid);
      notificationsContainer.appendChild(item);
    });
  }

  // ── 9. Dedicated Telegram View Logic ─────────────────────────────────────
  async function loadDedicatedTelegramSettings() {
    if (!window.AuthManager.isAuthenticated()) return;

    try {
      const data = await window.apiClient.get('/api/users/me/telegram');
      if (data) {
        const isConfigured = Boolean(data.isConfigured && data.chatId);

        if (tgStatusBadge) {
          tgStatusBadge.className = isConfigured ? 'badge-active' : 'badge-stopped';
          tgStatusBadge.textContent = isConfigured ? '🟢 Yapılandırıldı & Aktif' : '⚪ Yapılandırılmadı';
        }
        if (tgStatusMaskedToken) {
          tgStatusMaskedToken.textContent = data.maskedBotToken || 'Tanımlanmadı';
        }
        if (tgStatusChatId) {
          tgStatusChatId.textContent = data.chatId || 'Tanımlanmadı';
        }
        if (tgStatusUpdatedAt) {
          tgStatusUpdatedAt.textContent = data.updatedAt ? new Date(data.updatedAt).toLocaleString('tr-TR') : '—';
        }

        if (viewTgChatId) viewTgChatId.value = data.chatId || '';
        if (viewTgToken) {
          viewTgToken.value = ''; // Plaintext token never held in memory
          viewTgToken.placeholder = data.maskedBotToken ? `Mevcut Token: ${data.maskedBotToken}` : '123456789:AA...';
        }
        if (viewTgDisconnectBtn) {
          viewTgDisconnectBtn.style.display = isConfigured ? 'inline-block' : 'none';
        }
      }
    } catch (err) {
      console.warn('Failed to load dedicated Telegram settings:', err);
    }
  }

  if (viewTgTestBtn) {
    viewTgTestBtn.addEventListener('click', async () => {
      const botToken = viewTgToken.value.trim();
      const chatId = viewTgChatId.value.trim();

      if (!botToken) {
        window.UI.setStatus(viewTgStatusMsg, '🔴 Test etmek için lütfen Bot Token alanını doldurun.', 'error');
        return;
      }
      if (!chatId) {
        window.UI.setStatus(viewTgStatusMsg, '🔴 Test etmek için lütfen Chat ID girin.', 'error');
        return;
      }

      window.UI.setStatus(viewTgStatusMsg, 'Telegram bağlantısı test ediliyor...', 'loading', true);
      viewTgTestBtn.disabled = true;

      try {
        const data = await window.apiClient.post('/api/telegram/test', { botToken, chatId });
        if (data && data.success) {
          window.UI.setStatus(viewTgStatusMsg, '🟢 Telegram bağlantı testi başarılı!', 'success');
          window.UI.showToast('Telegram bağlantısı başarılı!', 'success');
        } else {
          const msg = data?.message || 'Telegram bağlantısı kurulamadı.';
          window.UI.setStatus(viewTgStatusMsg, `🔴 ${msg}`, 'error');
          window.UI.showToast(msg, 'error');
        }
      } catch (err) {
        window.UI.setStatus(viewTgStatusMsg, `🔴 ${err.message}`, 'error');
        window.UI.showToast(err.message, 'error');
      } finally {
        viewTgTestBtn.disabled = false;
      }
    });
  }

  if (viewTgForm) {
    viewTgForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const botToken = viewTgToken.value.trim();
      const chatId = viewTgChatId.value.trim();

      if (!botToken || !chatId) {
        window.UI.setStatus(viewTgStatusMsg, 'Lütfen Bot Token ve Chat ID alanlarını doldurun.', 'error');
        return;
      }

      window.UI.setStatus(viewTgStatusMsg, 'Telegram ayarları kaydediliyor...', 'loading', true);
      const saveBtn = viewTgForm.querySelector('button[type="submit"]');
      if (saveBtn) saveBtn.disabled = true;

      try {
        await window.apiClient.put('/api/users/me/telegram', { botToken, chatId });
        window.UI.setStatus(viewTgStatusMsg, '🟢 Telegram ayarları başarıyla kaydedildi!', 'success');
        window.UI.showToast('Telegram ayarlarınız güncellendi.', 'success');
        viewTgToken.value = '';
        loadDedicatedTelegramSettings();
      } catch (err) {
        window.UI.setStatus(viewTgStatusMsg, `🔴 ${err.message}`, 'error');
        window.UI.showToast(err.message, 'error');
      } finally {
        if (saveBtn) saveBtn.disabled = false;
      }
    });
  }

  if (viewTgDisconnectBtn) {
    viewTgDisconnectBtn.addEventListener('click', async () => {
      if (!confirm('Kayıtlı Telegram bot ve bildirim ayarlarınızı silmek istediğinizden emin misiniz?')) {
        return;
      }

      try {
        await window.apiClient.delete('/api/users/me/telegram');
        window.UI.showToast('Telegram bağlantısı kaldırıldı.', 'info');
        loadDedicatedTelegramSettings();
      } catch (err) {
        window.UI.showToast(err.message, 'error');
      }
    });
  }

  // ── 10. Settings & User Profile Logic ────────────────────────────────────
  async function loadUserProfileSettings() {
    if (!window.AuthManager.isAuthenticated()) return;
    try {
      const user = await window.apiClient.get('/api/users/me');
      cachedUserProfile = user;

      // Profile Form
      if (settingsFirstnameInput) settingsFirstnameInput.value = user.firstName || '';
      if (settingsLastnameInput) settingsLastnameInput.value = user.lastName || '';
      if (settingsEmailDisplay) settingsEmailDisplay.textContent = user.email || '—';

      // Preferences Form
      const prefs = user.preferences || {};
      if (settingsPrefTgEnabled) settingsPrefTgEnabled.checked = prefs.telegramNotificationsEnabled !== false;
      if (settingsPrefLangSelect) settingsPrefLangSelect.value = prefs.notificationLanguage || 'tr';
      if (settingsPrefIntervalSelect) settingsPrefIntervalSelect.value = String(prefs.defaultCheckIntervalMinutes || 60);
      if (settingsPrefTimezoneSelect) settingsPrefTimezoneSelect.value = prefs.timezone || 'Europe/Istanbul';

      // Account Details
      if (settingsAccountEmail) settingsAccountEmail.textContent = user.email || '—';
      if (settingsAccountCreatedAt) settingsAccountCreatedAt.textContent = user.createdAt ? new Date(user.createdAt).toLocaleString('tr-TR') : '—';
      if (settingsAccountLastLogin) settingsAccountLastLogin.textContent = user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString('tr-TR') : 'İlk oturum';
      if (settingsAccountTgStatus) {
        settingsAccountTgStatus.innerHTML = user.hasTelegramConfigured
          ? '<span style="color:#16a34a; font-weight:700;">🟢 Yapılandırıldı</span>'
          : '<span style="color:#64748b; font-weight:700;">⚪ Yapılandırılmadı</span>';
      }

      updateUserBadgeDisplay(user);
    } catch (err) {
      console.warn('Failed to load profile for settings:', err);
    }
  }

  // Save Profile Form Submit
  if (settingsProfileForm) {
    settingsProfileForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const firstName = settingsFirstnameInput.value.trim();
      const lastName = settingsLastnameInput.value.trim();

      if (!firstName || !lastName) {
        window.UI.setStatus(settingsProfileStatus, 'Lütfen ad ve soyad alanlarını doldurun.', 'error');
        return;
      }

      window.UI.setStatus(settingsProfileStatus, 'Profil güncelleniyor...', 'loading', true);
      const submitBtn = settingsProfileForm.querySelector('button[type="submit"]');
      if (submitBtn) submitBtn.disabled = true;

      try {
        const updated = await window.apiClient.put('/api/users/me', { firstName, lastName });
        window.UI.setStatus(settingsProfileStatus, '🟢 Profil bilgileriniz kaydedildi.', 'success');
        window.UI.showToast('Profil bilgileriniz güncellendi.', 'success');

        // Update local session user & badges
        const currentUser = window.AuthManager.getUser();
        if (currentUser) {
          currentUser.firstName = firstName;
          currentUser.lastName = lastName;
          window.AuthManager.setSession(window.AuthManager.getToken(), window.AuthManager.getRefreshToken(), currentUser);
        }
        updateUserBadgeDisplay(currentUser || { firstName, lastName, email: settingsEmailDisplay?.textContent });
      } catch (err) {
        window.UI.setStatus(settingsProfileStatus, `🔴 ${err.message}`, 'error');
        window.UI.showToast(err.message, 'error');
      } finally {
        if (submitBtn) submitBtn.disabled = false;
      }
    });
  }

  // Save Preferences Form Submit
  if (settingsPreferencesForm) {
    settingsPreferencesForm.addEventListener('submit', async (e) => {
      e.preventDefault();

      const preferences = {
        telegramNotificationsEnabled: settingsPrefTgEnabled ? settingsPrefTgEnabled.checked : true,
        notificationLanguage: settingsPrefLangSelect ? settingsPrefLangSelect.value : 'tr',
        defaultCheckIntervalMinutes: settingsPrefIntervalSelect ? parseInt(settingsPrefIntervalSelect.value, 10) : 60,
        timezone: settingsPrefTimezoneSelect ? settingsPrefTimezoneSelect.value : 'Europe/Istanbul'
      };

      window.UI.setStatus(settingsPrefStatus, 'Tercihler güncelleniyor...', 'loading', true);
      const submitBtn = settingsPreferencesForm.querySelector('button[type="submit"]');
      if (submitBtn) submitBtn.disabled = true;

      try {
        await window.apiClient.put('/api/users/me', { preferences });
        window.UI.setStatus(settingsPrefStatus, '🟢 Bildirim ve takip tercihleriniz kaydedildi.', 'success');
        window.UI.showToast('Uygulama tercihleriniz kaydedildi.', 'success');
      } catch (err) {
        window.UI.setStatus(settingsPrefStatus, `🔴 ${err.message}`, 'error');
        window.UI.showToast(err.message, 'error');
      } finally {
        if (submitBtn) submitBtn.disabled = false;
      }
    });
  }

  // Revoke All Sessions
  if (settingsRevokeAllBtn) {
    settingsRevokeAllBtn.addEventListener('click', async () => {
      if (!confirm('Tüm cihazlardaki açık oturumlarınızı kapatmak istediğinizden emin misiniz?\n\nBu işlemden sonra tekrar giriş yapmanız gerekecektir.')) {
        return;
      }

      try {
        await window.apiClient.post('/api/auth/revoke-all', {});
        window.AuthManager.clearSession();
        window.UI.showToast('Tüm oturumlar başarıyla sonlandırıldı.', 'info');
      } catch (err) {
        window.UI.showToast(`Oturumlar sonlandırılırken hata: ${err.message}`, 'error');
      }
    });
  }

  // ── Init ─────────────────────────────────────────────────────────────────
  window.AuthManager.init();
});
