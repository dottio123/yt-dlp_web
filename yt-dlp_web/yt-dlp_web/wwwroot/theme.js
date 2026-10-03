// YT-DLP Web Theme Management & Enhanced Navigation Persistence
(function () {
    const STORAGE_KEY = 'yt_dlp_theme';

    function getPreferredTheme() {
        try {
            const stored = localStorage.getItem(STORAGE_KEY);
            if (stored === 'dark' || stored === 'light') {
                return stored;
            }
        } catch (e) { }
        return 'dark'; // Default to sleek dark mode
    }

    function applyTheme(theme) {
        if (!theme) theme = getPreferredTheme();
        if (document.documentElement) {
            document.documentElement.setAttribute('data-theme', theme);
            document.documentElement.setAttribute('data-bs-theme', theme);
        }
        if (document.body) {
            document.body.setAttribute('data-theme', theme);
            document.body.setAttribute('data-bs-theme', theme);
        }
    }

    // Apply immediately to prevent flash
    const initialTheme = getPreferredTheme();
    applyTheme(initialTheme);

    // Watch for Blazor enhanced navigation resetting documentElement attributes
    let isApplying = false;
    const observer = new MutationObserver(function (mutations) {
        if (isApplying) return;
        const expected = getPreferredTheme();
        const current = document.documentElement.getAttribute('data-theme');
        if (current !== expected) {
            isApplying = true;
            applyTheme(expected);
            isApplying = false;
        }
    });

    if (document.documentElement) {
        observer.observe(document.documentElement, {
            attributes: true,
            attributeFilter: ['data-theme', 'data-bs-theme']
        });
    }

    // Also hook Blazor enhanced navigation events
    function hookBlazor() {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener('enhancedload', function () {
                applyTheme(getPreferredTheme());
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', hookBlazor);
    } else {
        hookBlazor();
    }

    window.ytTheme = {
        getTheme: function () {
            return getPreferredTheme();
        },
        setTheme: function (theme) {
            if (theme !== 'dark' && theme !== 'light') return;
            try {
                localStorage.setItem(STORAGE_KEY, theme);
            } catch (e) { }
            applyTheme(theme);
        },
        toggleTheme: function () {
            const current = getPreferredTheme();
            const next = current === 'dark' ? 'light' : 'dark';
            this.setTheme(next);
            return next;
        }
    };
})();
