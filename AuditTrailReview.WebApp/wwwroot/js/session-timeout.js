// Inactivity timeout for signed-in users (Annex 11 s.12). The server is authoritative -- the
// cookie expires after the configured idle time regardless of this script -- this only warns the
// user first and signs them out cleanly instead of leaving a stale page on screen.
//
// Config comes from data-* attributes on #session-timeout-config (rendered by _Layout.cshtml from
// the Security tab settings): data-idle-minutes, data-warning-minutes.
(function () {
    'use strict';

    const cfg = document.getElementById('session-timeout-config');
    if (!cfg) return;

    const idleMs = Number(cfg.dataset.idleMinutes) * 60 * 1000;
    const warnMs = Number(cfg.dataset.warningMinutes) * 60 * 1000;
    if (!(idleMs > 0) || !(warnMs > 0) || warnMs >= idleMs) return;

    const STORAGE_KEY = 'atr.lastActivity';
    const KEEPALIVE_MIN_GAP_MS = Math.max(15000, Math.floor(idleMs / 3));
    // Margin so the client signs out just before the server cookie would have expired.
    const SAFETY_MS = 5000;

    let lastActivity = Date.now();
    let lastKeepAlive = Date.now();
    let warningShown = false;
    let signingOut = false;

    const modalEl = document.getElementById('session-timeout-modal');
    const countdownEl = document.getElementById('session-timeout-countdown');
    const stayBtn = document.getElementById('session-timeout-stay');
    const signOutBtn = document.getElementById('session-timeout-signout');
    const modal = modalEl && window.bootstrap ? new bootstrap.Modal(modalEl, { backdrop: 'static', keyboard: false }) : null;

    function readShared() {
        try {
            const v = Number(localStorage.getItem(STORAGE_KEY));
            return Number.isFinite(v) && v > 0 ? v : 0;
        } catch (e) { return 0; }
    }

    function writeShared(ts) {
        try { localStorage.setItem(STORAGE_KEY, String(ts)); } catch (e) { /* storage may be blocked */ }
    }

    function loginUrl() {
        return '/Identity/Account/Login?returnUrl=' + encodeURIComponent(location.pathname + location.search);
    }

    function signOutNow() {
        if (signingOut) return;
        signingOut = true;
        const form = document.getElementById('logoutForm');
        if (form) {
            // POST the real logout so the cookie is cleared and the event is logged; the redirect
            // fallback covers a form that fails to submit.
            try { form.submit(); } catch (e) { location.href = loginUrl(); }
            setTimeout(function () { location.href = loginUrl(); }, 3000);
        } else {
            location.href = loginUrl();
        }
    }

    function keepAlive() {
        lastKeepAlive = Date.now();
        // No X-Background-Poll header on purpose: this request is what renews the sliding cookie.
        fetch('/Session/KeepAlive', {
            method: 'GET',
            cache: 'no-store',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        }).then(function (r) {
            if (r.status === 401 || r.status === 403) { location.href = loginUrl(); }
        }).catch(function () { /* transient failure; next activity retries */ });
    }

    function markActive() {
        if (signingOut) return;
        const now = Date.now();
        if (warningShown) return; // once warned, only the explicit button extends the session
        if (now - lastActivity < 1000) return; // throttle
        lastActivity = now;
        writeShared(now);
        if (now - lastKeepAlive >= KEEPALIVE_MIN_GAP_MS) keepAlive();
    }

    function stayLoggedIn() {
        warningShown = false;
        if (modal) modal.hide();
        lastActivity = Date.now();
        writeShared(lastActivity);
        keepAlive();
    }

    ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'click'].forEach(function (ev) {
        document.addEventListener(ev, markActive, { passive: true, capture: true });
    });

    if (stayBtn) stayBtn.addEventListener('click', stayLoggedIn);
    if (signOutBtn) signOutBtn.addEventListener('click', signOutNow);

    // Another tab's activity counts too (and its "stay signed in" click closes our warning).
    window.addEventListener('storage', function (e) {
        if (e.key !== STORAGE_KEY) return;
        const shared = Number(e.newValue);
        if (shared > lastActivity) {
            lastActivity = shared;
            if (warningShown) {
                warningShown = false;
                if (modal) modal.hide();
            }
        }
    });

    function formatRemaining(ms) {
        const total = Math.max(0, Math.ceil(ms / 1000));
        const m = Math.floor(total / 60);
        const s = total % 60;
        return m + ':' + (s < 10 ? '0' : '') + s;
    }

    function tick() {
        if (signingOut) return;
        const shared = readShared();
        if (shared > lastActivity) lastActivity = shared;

        const idleFor = Date.now() - lastActivity;
        const remaining = idleMs - SAFETY_MS - idleFor;

        if (remaining <= 0) {
            signOutNow();
            return;
        }

        if (remaining <= warnMs) {
            if (!warningShown) {
                warningShown = true;
                if (modal) modal.show();
            }
            if (countdownEl) countdownEl.textContent = formatRemaining(remaining);
        }
    }

    writeShared(lastActivity);
    setInterval(tick, 1000);
})();
