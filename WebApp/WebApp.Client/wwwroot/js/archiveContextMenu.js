// DOM-only helpers for the Archive Browser right-click menu and drag-and-drop move. Which items
// are targeted, whether a drop is valid, and every request stay in C# (ArchiveBrowser.razor,
// ArchiveDragDropRules); this file only positions/focuses the menu, dismisses it, and wires the
// HTML Drag and Drop API pieces Blazor cannot do (dataTransfer setup, dragover acceptance).

const menuItemSelector = '[role="menuitem"]:not(:disabled):not(.disabled)';

export function openContextMenu(menu, x, y, dotNetRef, closeMethod) {
    if (!menu) {
        return { dispose() { } };
    }

    const margin = 8;
    const rect = menu.getBoundingClientRect();
    const left = Math.max(margin, Math.min(x, window.innerWidth - rect.width - margin));
    const top = Math.max(margin, Math.min(y, window.innerHeight - rect.height - margin));
    menu.style.left = `${left}px`;
    menu.style.top = `${top}px`;
    menu.querySelector(menuItemSelector)?.focus();

    const close = () => dotNetRef.invokeMethodAsync(closeMethod);
    const onPointerDown = event => {
        if (!menu.contains(event.target)) {
            close();
        }
    };
    const onKeyDown = event => {
        if (event.key === 'Escape') {
            event.preventDefault();
            const originId = menu.dataset.originId;
            close();
            if (originId) {
                document.querySelector(`[data-item-id="${CSS.escape(originId)}"] button`)?.focus();
            }
            return;
        }

        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            const items = Array.from(menu.querySelectorAll(menuItemSelector));
            if (items.length === 0) {
                return;
            }

            event.preventDefault();
            const index = items.indexOf(document.activeElement);
            const next = event.key === 'ArrowDown'
                ? (index + 1) % items.length
                : (index <= 0 ? items.length - 1 : index - 1);
            items[next].focus();
        }
    };

    document.addEventListener('pointerdown', onPointerDown, true);
    document.addEventListener('keydown', onKeyDown, true);
    window.addEventListener('scroll', close, true);
    window.addEventListener('resize', close);
    return {
        dispose() {
            document.removeEventListener('pointerdown', onPointerDown, true);
            document.removeEventListener('keydown', onKeyDown, true);
            window.removeEventListener('scroll', close, true);
            window.removeEventListener('resize', close);
        }
    };
}

// Delegated listeners on the browser root: no per-card registration. Cards carry data-item-id
// (and data-selected when part of the selection); drop targets carry data-drop-folder.
export function installDragDrop(root) {
    if (!root) {
        return { dispose() { } };
    }

    const marker = 'application/x-perene-archive-items';
    let dragging = null;
    let activeTarget = null;

    const setActive = target => {
        if (activeTarget === target) {
            return;
        }
        activeTarget?.removeAttribute('data-drop-active');
        activeTarget = target;
        activeTarget?.setAttribute('data-drop-active', 'true');
    };

    const validTarget = event => {
        const target = event.target instanceof Element ? event.target.closest('[data-drop-folder]') : null;
        if (!dragging || !target || dragging.ids.has(target.dataset.itemId ?? '')) {
            return null;
        }
        return target;
    };

    const onDragStart = event => {
        const card = event.target instanceof Element ? event.target.closest('[data-item-id]') : null;
        if (!card || !event.dataTransfer) {
            return;
        }

        const ids = new Set([card.dataset.itemId]);
        if (card.dataset.selected === 'true') {
            root.querySelectorAll('[data-item-id][data-selected="true"]').forEach(el => ids.add(el.dataset.itemId));
        }

        // Firefox will not start a drag without data; the payload is a neutral marker, never a path.
        event.dataTransfer.setData(marker, 'archive-items');
        event.dataTransfer.setData('text/plain', 'archive-items');
        event.dataTransfer.effectAllowed = 'move';
        // Chrome/Firefox snapshot the whole card (including the off-screen actions panel), so use a
        // small dedicated ghost: the item name, or a count for multi-item drags.
        const ghost = document.createElement('div');
        ghost.className = 'card px-3 py-2 shadow d-inline-flex flex-row align-items-center gap-2';
        ghost.style.cssText = 'position:fixed;top:-1000px;left:-1000px;max-width:16rem;';
        const icon = document.createElement('i');
        icon.className = `bi ${ids.size > 1 ? 'bi-files' : 'bi-arrows-move'}`;
        icon.setAttribute('aria-hidden', 'true');
        const label = document.createElement('span');
        label.className = 'text-truncate';
        label.textContent = ids.size > 1 ? `${ids.size} items` : (card.dataset.itemName ?? '1 item');
        ghost.append(icon, label);
        document.body.appendChild(ghost);
        event.dataTransfer.setDragImage(ghost, 16, 16);
        setTimeout(() => ghost.remove(), 0);

        dragging = { ids };
        root.classList.add('archive-dragging');
        requestAnimationFrame(() => {
            root.querySelectorAll('[data-item-id]').forEach(el => {
                if (ids.has(el.dataset.itemId)) {
                    el.classList.add('archive-drag-source');
                }
            });
        });
    };

    const onDragOver = event => {
        const target = validTarget(event);
        if (target) {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
            setActive(target);
        } else {
            setActive(null);
        }
    };

    const onDragLeave = event => {
        if (!event.relatedTarget || !root.contains(event.relatedTarget)) {
            setActive(null);
        }
    };

    const finish = () => {
        dragging = null;
        setActive(null);
        root.classList.remove('archive-dragging');
        root.querySelectorAll('.archive-drag-source').forEach(el => el.classList.remove('archive-drag-source'));
    };

    const onDrop = event => {
        if (validTarget(event)) {
            event.preventDefault();
        }
        // Blazor's @ondrop handler runs from the same event; clear visuals afterwards.
        setTimeout(finish, 0);
    };

    root.addEventListener('dragstart', onDragStart);
    root.addEventListener('dragover', onDragOver);
    root.addEventListener('dragleave', onDragLeave);
    root.addEventListener('drop', onDrop);
    document.addEventListener('dragend', finish);
    return {
        dispose() {
            root.removeEventListener('dragstart', onDragStart);
            root.removeEventListener('dragover', onDragOver);
            root.removeEventListener('dragleave', onDragLeave);
            root.removeEventListener('drop', onDrop);
            document.removeEventListener('dragend', finish);
            finish();
        }
    };
}
