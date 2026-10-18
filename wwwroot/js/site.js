// Drogueria - Interacciones del Dashboard, Mini Sidebar y Modo Oscuro

document.addEventListener('DOMContentLoaded', function () {
    const appLayout = document.querySelector('.app-layout');
    const sidebar = document.getElementById('appSidebar');
    const sidebarToggle = document.getElementById('sidebarToggle');
    const mobileSidebarToggle = document.getElementById('mobileSidebarToggle');
    const backdrop = document.getElementById('sidebarBackdrop');
    const themeToggleBtn = document.getElementById('themeToggleBtn');
    const themeIcon = document.getElementById('themeIcon');

    // =========================================================================
    // 1. MODO OSCURO / MODO CLARO (Theme Switcher)
    // =========================================================================
    function applyTheme(theme) {
        document.documentElement.setAttribute('data-bs-theme', theme);
        localStorage.setItem('app_theme', theme);

        if (themeIcon) {
            if (theme === 'dark') {
                themeIcon.classList.remove('bi-moon-stars');
                themeIcon.classList.add('bi-sun');
                if (themeToggleBtn) themeToggleBtn.title = 'Cambiar a modo claro';
            } else {
                themeIcon.classList.remove('bi-sun');
                themeIcon.classList.add('bi-moon-stars');
                if (themeToggleBtn) themeToggleBtn.title = 'Cambiar a modo oscuro';
            }
        }
    }

    // Inicializar icono según el tema actual guardado en localStorage o aplicado en <head>
    const savedTheme = localStorage.getItem('app_theme') || document.documentElement.getAttribute('data-bs-theme') || 'light';
    applyTheme(savedTheme);

    if (themeToggleBtn) {
        themeToggleBtn.addEventListener('click', function () {
            const activeTheme = document.documentElement.getAttribute('data-bs-theme') || 'light';
            const newTheme = activeTheme === 'dark' ? 'light' : 'dark';
            applyTheme(newTheme);
        });
    }

    // =========================================================================
    // 2. MINI SIDEBAR (Colapsar a solo iconos) Y MÓVIL
    // =========================================================================
    function updateToggleTitle() {
        if (!sidebarToggle) return;
        if (appLayout && appLayout.classList.contains('sidebar-collapsed')) {
            sidebarToggle.title = 'Expandir barra lateral';
        } else {
            sidebarToggle.title = 'Colapsar barra lateral a iconos';
        }
    }

    // Restaurar estado del sidebar en escritorio
    if (appLayout && window.innerWidth >= 992) {
        const isCollapsed = localStorage.getItem('sidebar_collapsed') === 'true';
        if (isCollapsed) {
            appLayout.classList.add('sidebar-collapsed');
        }
        updateToggleTitle();
    }

    // Función de alternancia para escritorio
    function toggleSidebar() {
        if (window.innerWidth >= 992) {
            if (appLayout) {
                appLayout.classList.toggle('sidebar-collapsed');
                const isCollapsed = appLayout.classList.contains('sidebar-collapsed');
                localStorage.setItem('sidebar_collapsed', isCollapsed);
                updateToggleTitle();
            }
        } else {
            // Modo Móvil
            if (sidebar && backdrop) {
                sidebar.classList.toggle('show');
                backdrop.classList.toggle('show');
            }
        }
    }

    // Botón flotante en el borde divisorio (Edge Toggle)
    if (sidebarToggle) {
        sidebarToggle.addEventListener('click', function (e) {
            e.preventDefault();
            toggleSidebar();
        });
    }

    // Botón hamburguesa para móviles
    if (mobileSidebarToggle) {
        mobileSidebarToggle.addEventListener('click', function (e) {
            e.preventDefault();
            if (sidebar && backdrop) {
                sidebar.classList.add('show');
                backdrop.classList.add('show');
            }
        });
    }

    // Cerrar al hacer clic en el backdrop (móvil)
    if (backdrop) {
        backdrop.addEventListener('click', function () {
            if (sidebar) sidebar.classList.remove('show');
            backdrop.classList.remove('show');
        });
    }

    // =========================================================================
    // 3. SUBMENÚS Y DROPDOWNS
    // =========================================================================
    const navToggles = document.querySelectorAll('[data-nav-toggle]');
    navToggles.forEach(function (toggle) {
        toggle.addEventListener('click', function (e) {
            e.preventDefault();
            // Si el sidebar está en modo mini y se hace clic en un grupo, primero expandir
            if (appLayout && appLayout.classList.contains('sidebar-collapsed') && window.innerWidth >= 992) {
                appLayout.classList.remove('sidebar-collapsed');
                localStorage.setItem('sidebar_collapsed', 'false');
                updateToggleTitle();
            }

            const group = this.closest('[data-nav-group]');
            if (group) {
                group.classList.toggle('is-open');
            }
        });
    });

    // Menú de Perfil
    const profileBtn = document.getElementById('profileDropdownBtn');
    const profileMenu = document.getElementById('profileDropdownMenu');

    if (profileBtn && profileMenu) {
        profileBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            profileMenu.classList.toggle('show');
        });

        document.addEventListener('click', function (e) {
            if (!profileMenu.contains(e.target) && !profileBtn.contains(e.target)) {
                profileMenu.classList.remove('show');
            }
        });
    }
    // =========================================================================
    // 4. PREVENCIÓN GLOBAL DE NÚMEROS NEGATIVOS Y LÍMITES EN INPUTS
    // =========================================================================
    document.addEventListener('input', function (e) {
        if (e.target && e.target.tagName === 'INPUT' && e.target.type === 'number') {
            const minAttr = e.target.getAttribute('min');
            const minVal = minAttr !== null ? parseFloat(minAttr) : 0;
            if (e.target.value !== '' && parseFloat(e.target.value) < minVal) {
                e.target.value = minVal;
                if (window.showAppToast) {
                    window.showAppToast(`No se permiten valores numéricos inferiores a ${minVal}.`, 'warning');
                }
            }
            const maxAttr = e.target.getAttribute('max');
            if (maxAttr !== null) {
                const maxVal = parseFloat(maxAttr);
                if (!isNaN(maxVal) && e.target.value !== '' && parseFloat(e.target.value) > maxVal) {
                    e.target.value = maxVal;
                    if (window.showAppToast) {
                        window.showAppToast(`El límite máximo permitido para este campo es ${maxVal}.`, 'warning');
                    }
                }
            }
        }
    });

    // =========================================================================
    // 5. AUTO-DISMISS GLOBAL DE ALERTAS (TempData y Bootstrap .alert)
    // =========================================================================
    function autoDismissAlerts() {
        document.querySelectorAll('.alert.alert-dismissible').forEach(function (alert) {
            // Ya procesada
            if (alert.dataset.autoDismiss) return;
            alert.dataset.autoDismiss = '1';

            setTimeout(function () {
                alert.style.transition = 'opacity 0.5s ease, transform 0.5s ease, margin 0.4s ease, padding 0.4s ease, max-height 0.4s ease';
                alert.style.opacity = '0';
                alert.style.transform = 'translateY(-8px) scale(0.98)';
                setTimeout(function () {
                    alert.style.maxHeight = '0';
                    alert.style.margin = '0';
                    alert.style.padding = '0';
                    alert.style.overflow = 'hidden';
                    setTimeout(function () { alert.remove(); }, 420);
                }, 480);
            }, 3500);
        });
    }

    // Ejecutar al cargar
    autoDismissAlerts();

    // También observar alertas que se inserten dinámicamente
    const alertObserver = new MutationObserver(function () { autoDismissAlerts(); });
    alertObserver.observe(document.body, { childList: true, subtree: true });
});

// =========================================================================
// 4. SISTEMA GLOBAL DE NOTIFICACIONES TOAST (AJAX FEEDBACK)
// =========================================================================
window.showAppToast = function (message, type = 'success') {
    let container = document.getElementById('appToastContainer');
    if (!container) {
        container = document.createElement('div');
        container.id = 'appToastContainer';
        container.style.cssText = 'position: fixed; top: 20px; right: 20px; z-index: 99999; display: flex; flex-direction: column; gap: 10px; max-width: 380px; width: calc(100% - 40px); pointer-events: none;';
        document.body.appendChild(container);
    }

    const toast = document.createElement('div');
    toast.className = `app-toast toast-${type}`;
    toast.style.cssText = `
        pointer-events: auto;
        display: flex;
        align-items: center;
        gap: 12px;
        padding: 14px 18px;
        background: var(--bg-card, #ffffff);
        color: var(--t-base, #1e293b);
        border: 1px solid var(--border, rgba(0,0,0,0.1));
        border-radius: 14px;
        box-shadow: 0 10px 30px rgba(0,0,0,0.15);
        font-size: 14px;
        font-weight: 500;
        opacity: 0;
        transform: translateY(-15px) scale(0.95);
        transition: all 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    `;

    let iconHtml = '<i class="bi bi-check-circle-fill text-success fs-5"></i>';
    if (type === 'error' || type === 'danger') {
        iconHtml = '<i class="bi bi-exclamation-octagon-fill text-danger fs-5"></i>';
        toast.style.borderLeft = '4px solid #ef4444';
    } else if (type === 'warning') {
        iconHtml = '<i class="bi bi-exclamation-triangle-fill text-warning fs-5"></i>';
        toast.style.borderLeft = '4px solid #f59e0b';
    } else if (type === 'info') {
        iconHtml = '<i class="bi bi-info-circle-fill text-primary fs-5"></i>';
        toast.style.borderLeft = '4px solid #7c3aed';
    } else {
        toast.style.borderLeft = '4px solid #10b981';
    }

    toast.innerHTML = `
        <div class="d-flex align-items-center" style="flex-shrink:0;">${iconHtml}</div>
        <div style="flex:1; line-height: 1.4;">${message}</div>
        <button type="button" style="background:none;border:none;color:var(--t-muted,#94a3b8);cursor:pointer;padding:0;font-size:16px;" aria-label="Cerrar">&times;</button>
    `;

    const closeBtn = toast.querySelector('button');
    closeBtn.addEventListener('click', () => {
        toast.style.opacity = '0';
        toast.style.transform = 'translateY(-15px) scale(0.95)';
        setTimeout(() => toast.remove(), 300);
    });

    container.appendChild(toast);

    // Animación de entrada
    requestAnimationFrame(() => {
        toast.style.opacity = '1';
        toast.style.transform = 'translateY(0) scale(1)';
    });

    // Auto eliminar a los 3.5 segundos
    setTimeout(() => {
        if (toast.parentElement) {
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(-15px) scale(0.95)';
            setTimeout(() => toast.remove(), 300);
        }
    }, 3500);
};

// Helper universal para llamadas AJAX con token antiforgery
window.postFormAjax = async function (url, formData) {
    try {
        const response = await fetch(url, {
            method: 'POST',
            body: formData,
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                'Accept': 'application/json'
            }
        });
        return await response.json();
    } catch (err) {
        console.error('Error en postFormAjax:', err);
        return { success: false, message: 'Ocurrió un error en la comunicación con el servidor.' };
    }
};

