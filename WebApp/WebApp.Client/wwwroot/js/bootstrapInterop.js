const tooltipSelector = '[data-bs-toggle="tooltip"]';

export function refreshTooltips(root) {
    const Tooltip = globalThis.bootstrap?.Tooltip;
    if (!root || !Tooltip) {
        return;
    }

    root.querySelectorAll(tooltipSelector).forEach(element => {
        const title = element.dataset.bsTitle ?? element.getAttribute('aria-label') ?? '';
        const instance = Tooltip.getInstance(element);
        if (instance && element.dataset.tooltipTitle !== title) {
            instance.dispose();
        }

        if (!Tooltip.getInstance(element)) {
            new Tooltip(element, { title });
            element.dataset.tooltipTitle = title;
        }
    });
}

export function disposeTooltips(root) {
    const Tooltip = globalThis.bootstrap?.Tooltip;
    if (!root || !Tooltip) {
        return;
    }

    root.querySelectorAll(tooltipSelector).forEach(element => {
        Tooltip.getInstance(element)?.dispose();
        delete element.dataset.tooltipTitle;
    });
}

export function clickElement(element) {
    element?.click();
}

export function hideModal(id) {
    const Modal = globalThis.bootstrap?.Modal;
    const element = document.getElementById(id);
    if (!Modal || !element) {
        return;
    }

    Modal.getOrCreateInstance(element).hide();
}

export function addOutsideClickListener(root, dotNetRef, methodName) {
    const handler = (event) => {
        if (root && !root.contains(event.target)) {
            dotNetRef.invokeMethodAsync(methodName);
        }
    };

    document.addEventListener('click', handler);
    return {
        dispose: () => document.removeEventListener('click', handler)
    };
}
