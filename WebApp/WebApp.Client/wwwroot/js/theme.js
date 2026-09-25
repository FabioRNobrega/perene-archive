(function () {
    "use strict";

    const storageKey = "perenearchive-theme";
    const darkTheme = "dark";
    const lightTheme = "light";

    function isTheme(value) {
        return value === darkTheme || value === lightTheme;
    }

    function readStoredTheme() {
        try {
            const value = window.localStorage.getItem(storageKey);
            return isTheme(value) ? value : null;
        } catch {
            return null;
        }
    }

    function applyTheme(theme) {
        const value = isTheme(theme) ? theme : darkTheme;
        document.documentElement.setAttribute("data-bs-theme", value);
        document.documentElement.style.colorScheme = value;
        return value;
    }

    function getTheme() {
        const current = document.documentElement.getAttribute("data-bs-theme");
        return isTheme(current) ? current : applyTheme(darkTheme);
    }

    function setTheme(theme) {
        const value = applyTheme(theme);

        try {
            window.localStorage.setItem(storageKey, value);
        } catch {
            // The in-page theme still works when storage is blocked or unavailable.
        }

        return value;
    }

    function toggleTheme() {
        return setTheme(getTheme() === darkTheme ? lightTheme : darkTheme);
    }

    // Global modal auto-focus: when a Bootstrap modal finishes opening, focus
    // its [autofocus] element or, failing that, its first text-entry control.
    document.addEventListener('shown.bs.modal', event => {
        const target = event.target.querySelector('[autofocus]:not([disabled])')
            ?? event.target.querySelector(
                'input:not([type=hidden]):not([type=checkbox]):not([type=radio]):not([type=range]):not([disabled]), textarea:not([disabled]), select:not([disabled])');
        target?.focus();
        if (target && typeof target.select === 'function' && target.value) {
            target.select();
        }
    });

    applyTheme(readStoredTheme() ?? darkTheme);
    window.videoManagerTheme = Object.freeze({ getTheme, toggleTheme });
})();
