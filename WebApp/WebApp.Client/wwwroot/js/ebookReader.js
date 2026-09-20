let pageNavigationHandler = null;
let tapNavigationHandlers = null;
let selectionObserverHandlers = null;

// A completed pointer gesture only counts as a tap (page turn) if it barely moved and was quick -
// this is what tells a tap apart from a drag-to-select, a flick, or a long-press-to-select.
const TAP_MAX_DISTANCE_PX = 10;
const TAP_MAX_DURATION_MS = 500;
const SIDE_ZONE_RATIO = 0.35;

function requestPageChange(dotNetReference, direction) {
    const method = direction === "previous"
        ? "GoToPreviousPageFromInputAsync"
        : direction === "next"
            ? "GoToNextPageFromInputAsync"
            : null;

    if (!method) {
        return;
    }

    dotNetReference.invokeMethodAsync(method).catch(() => { });
}

export function registerPageNavigation(dotNetReference) {
    unregisterPageNavigation();

    pageNavigationHandler = event => {
        const direction = event.key === "ArrowLeft"
            ? "previous"
            : event.key === "ArrowRight"
                ? "next"
                : null;

        if (!direction) {
            return;
        }

        event.preventDefault();

        if (event.repeat) {
            return;
        }

        requestPageChange(dotNetReference, direction);
    };

    window.addEventListener("keydown", pageNavigationHandler);
}

export function unregisterPageNavigation() {
    if (!pageNavigationHandler) {
        return;
    }

    window.removeEventListener("keydown", pageNavigationHandler);
    pageNavigationHandler = null;
}

function isTapGesture(maxDistance, durationMs) {
    return maxDistance <= TAP_MAX_DISTANCE_PX && durationMs <= TAP_MAX_DURATION_MS;
}

function isInteractiveTarget(target) {
    return !!target?.closest?.("a, button, input, select, textarea");
}

function resolveZone(container, clientX) {
    const rect = container.getBoundingClientRect();
    if (rect.width <= 0) {
        return null;
    }

    const relativeX = (clientX - rect.left) / rect.width;
    if (relativeX < SIDE_ZONE_RATIO) {
        return "previous";
    }

    if (relativeX > 1 - SIDE_ZONE_RATIO) {
        return "next";
    }

    return null;
}

export function registerTapNavigation(container, dotNetReference) {
    unregisterTapNavigation();

    if (!container) {
        return;
    }

    let activePointerId = null;
    let startX = 0;
    let startY = 0;
    let startTime = 0;
    let maxDistance = 0;

    const resetGesture = () => {
        activePointerId = null;
    };

    const onPointerDown = event => {
        if (activePointerId !== null) {
            return;
        }

        activePointerId = event.pointerId;
        startX = event.clientX;
        startY = event.clientY;
        startTime = event.timeStamp;
        maxDistance = 0;
    };

    const onPointerMove = event => {
        if (event.pointerId !== activePointerId) {
            return;
        }

        const distance = Math.hypot(event.clientX - startX, event.clientY - startY);
        maxDistance = Math.max(maxDistance, distance);
    };

    const onPointerUp = event => {
        if (event.pointerId !== activePointerId) {
            return;
        }

        const durationMs = event.timeStamp - startTime;
        resetGesture();

        if (!isTapGesture(maxDistance, durationMs)) {
            return;
        }

        if (isInteractiveTarget(event.target)) {
            return;
        }

        if (!window.getSelection()?.isCollapsed) {
            return;
        }

        const direction = resolveZone(container, event.clientX);
        if (!direction) {
            return;
        }

        requestPageChange(dotNetReference, direction);
    };

    const onPointerCancel = event => {
        if (event.pointerId === activePointerId) {
            resetGesture();
        }
    };

    container.addEventListener("pointerdown", onPointerDown);
    container.addEventListener("pointermove", onPointerMove);
    container.addEventListener("pointerup", onPointerUp);
    container.addEventListener("pointercancel", onPointerCancel);

    tapNavigationHandlers = { container, onPointerDown, onPointerMove, onPointerUp, onPointerCancel };
}

export function unregisterTapNavigation() {
    if (!tapNavigationHandlers) {
        return;
    }

    const { container, onPointerDown, onPointerMove, onPointerUp, onPointerCancel } = tapNavigationHandlers;
    container.removeEventListener("pointerdown", onPointerDown);
    container.removeEventListener("pointermove", onPointerMove);
    container.removeEventListener("pointerup", onPointerUp);
    container.removeEventListener("pointercancel", onPointerCancel);
    tapNavigationHandlers = null;
}

const SELECTION_MENU_EDGE_MARGIN_PX = 8;
const SELECTION_MENU_ESTIMATED_WIDTH_PX = 260;
const SELECTION_MENU_ESTIMATED_HEIGHT_PX = 48;

function getSelectionDetails(container) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) {
        return null;
    }

    const range = selection.getRangeAt(0);
    if (!container || !container.contains(range.commonAncestorContainer)) {
        return null;
    }

    const text = selection.toString().trim();
    // Anchor outside the entire selection so the actions never cover any line in a multi-line
    // passage. The browser's union rectangle gives us the first and last selected line.
    const rect = range.getBoundingClientRect();
    if (!text || (rect.width === 0 && rect.height === 0)) {
        return null;
    }

    const menuWidth = Math.min(
        SELECTION_MENU_ESTIMATED_WIDTH_PX,
        Math.max(0, window.innerWidth - (SELECTION_MENU_EDGE_MARGIN_PX * 2)));
    const halfMenuWidth = menuWidth / 2;
    const left = Math.min(
        Math.max(halfMenuWidth + SELECTION_MENU_EDGE_MARGIN_PX, rect.left + (rect.width / 2)),
        window.innerWidth - halfMenuWidth - SELECTION_MENU_EDGE_MARGIN_PX);
    // Android Chrome renders its own selection toolbar above the passage. Keep the app's
    // complementary actions below on touch-first devices, while preserving desktop placement.
    const isCoarsePointer = window.matchMedia("(pointer: coarse)").matches;
    const menuAbove = !isCoarsePointer && rect.top >= SELECTION_MENU_ESTIMATED_HEIGHT_PX + SELECTION_MENU_EDGE_MARGIN_PX;

    return {
        text,
        left,
        top: menuAbove ? rect.top - SELECTION_MENU_EDGE_MARGIN_PX : rect.bottom + SELECTION_MENU_EDGE_MARGIN_PX,
        menuAbove
    };
}

function normalizeText(text) {
    return text.replace(/\s+/g, " ").trim();
}

function getTextNodes(container) {
    const nodes = [];
    const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) {
        nodes.push(walker.currentNode);
    }
    return nodes;
}

function rawOffsetForBoundary(container, boundaryNode, boundaryOffset) {
    const range = document.createRange();
    range.setStart(container, 0);
    range.setEnd(boundaryNode, boundaryOffset);
    return range.toString().length;
}

function normalizedOffset(rawText, rawOffset) {
    return normalizeText(rawText.slice(0, rawOffset)).length;
}

function findTextPosition(chapterText, selectedText, contextBefore, contextAfter) {
    let fallback = -1;
    for (let position = chapterText.indexOf(selectedText); position >= 0; position = chapterText.indexOf(selectedText, position + 1)) {
        if (fallback < 0) fallback = position;
        const before = chapterText.slice(Math.max(0, position - contextBefore.length), position).trim();
        const after = chapterText.slice(position + selectedText.length, position + selectedText.length + contextAfter.length).trim();
        if (before.endsWith(contextBefore.trim()) && after.startsWith(contextAfter.trim())) return position;
    }
    return fallback;
}

export function getSelectionRange(container, normalizedChapterText) {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed || !container) return null;
    const range = selection.getRangeAt(0);
    const chapter = container.querySelector(".epub-chapter");
    if (!chapter || !chapter.contains(range.commonAncestorContainer)) return null;
    const rawText = chapter.textContent ?? "";
    const selectedText = normalizeText(range.toString());
    const domStart = normalizedOffset(rawText, rawOffsetForBoundary(chapter, range.startContainer, range.startOffset));
    const domEnd = normalizedOffset(rawText, rawOffsetForBoundary(chapter, range.endContainer, range.endOffset));
    if (!selectedText || domEnd <= domStart) return null;
    const normalizedDomText = normalizeText(rawText);
    const domContextBefore = normalizedDomText.slice(Math.max(0, domStart - 40), domStart);
    const domContextAfter = normalizedDomText.slice(domEnd, domEnd + 40);
    const normalizedChapter = normalizeText(normalizedChapterText ?? "");
    const start = findTextPosition(normalizedChapter, selectedText, domContextBefore, domContextAfter);
    if (start < 0) return null;
    const end = start + selectedText.length;
    return {
        textOffsetStart: start,
        textOffsetEnd: end,
        selectedText,
        contextBefore: normalizedChapter.slice(Math.max(0, start - 40), start),
        contextAfter: normalizedChapter.slice(end, end + 40)
    };
}

function normalizedCharacterRawOffsets(rawText) {
    const offsets = [];
    let pendingWhitespace = false;
    let started = false;
    for (let index = 0; index < rawText.length; index++) {
        if (/\s/.test(rawText[index])) {
            pendingWhitespace = started;
            continue;
        }
        if (pendingWhitespace) {
            offsets.push(index - 1);
            pendingWhitespace = false;
        }
        offsets.push(index);
        started = true;
    }
    return offsets;
}

function boundaryForRawOffset(nodes, rawOffset) {
    let position = 0;
    for (const node of nodes) {
        const nextPosition = position + node.nodeValue.length;
        if (rawOffset <= nextPosition) return { node, offset: Math.max(0, rawOffset - position) };
        position = nextPosition;
    }
    return null;
}

export function applyHighlights(container, highlights) {
    const chapter = container?.querySelector(".epub-chapter");
    if (!chapter) return;
    chapter.querySelectorAll(".epub-saved-highlight").forEach(element => element.replaceWith(...element.childNodes));
    const initialNodes = getTextNodes(chapter);
    const rawText = initialNodes.map(node => node.nodeValue).join("");
    const normalizedText = normalizeText(rawText);
    const offsets = normalizedCharacterRawOffsets(rawText);
    for (const highlight of highlights ?? []) {
        const start = findTextPosition(normalizedText, highlight.selectedText, highlight.contextBefore ?? "", highlight.contextAfter ?? "");
        const end = start + (highlight.selectedText?.length ?? 0);
        if (start < 0 || end > offsets.length) continue;
        const rawStart = offsets[start];
        const rawEnd = offsets[end - 1] + 1;
        let position = 0;
        for (const node of [...getTextNodes(chapter)]) {
            const nodeEnd = position + node.nodeValue.length;
            const overlapStart = Math.max(rawStart, position);
            const overlapEnd = Math.min(rawEnd, nodeEnd);
            if (overlapStart < overlapEnd && node.parentNode) {
                const before = node.nodeValue.slice(0, overlapStart - position);
                const selected = node.nodeValue.slice(overlapStart - position, overlapEnd - position);
                const after = node.nodeValue.slice(overlapEnd - position);
                const mark = document.createElement("mark");
                mark.className = "epub-saved-highlight";
                mark.textContent = selected;
                node.replaceWith(document.createTextNode(before), mark, document.createTextNode(after));
            }
            position = nodeEnd;
        }
    }
}

export function registerSelectionObserver(container, dotNetReference) {
    unregisterSelectionObserver();

    if (!container || !dotNetReference) {
        return;
    }

    let pendingFrame = null;
    const notifySelection = () => {
        pendingFrame = null;
        const selection = getSelectionDetails(container);
        const invocation = selection
            ? dotNetReference.invokeMethodAsync("UpdateSelectionFromBrowserAsync", selection.text, selection.left, selection.top, selection.menuAbove)
            : dotNetReference.invokeMethodAsync("ClearSelectionFromBrowserAsync");
        invocation.catch(() => { });
    };
    const scheduleSelectionNotification = () => {
        if (pendingFrame !== null) {
            return;
        }

        pendingFrame = window.requestAnimationFrame(notifySelection);
    };

    document.addEventListener("selectionchange", scheduleSelectionNotification);
    container.addEventListener("pointerup", scheduleSelectionNotification);
    container.addEventListener("touchend", scheduleSelectionNotification);
    selectionObserverHandlers = { container, scheduleSelectionNotification, pendingFrame: () => pendingFrame };
}

export function unregisterSelectionObserver() {
    if (!selectionObserverHandlers) {
        return;
    }

    const { container, scheduleSelectionNotification, pendingFrame } = selectionObserverHandlers;
    document.removeEventListener("selectionchange", scheduleSelectionNotification);
    container.removeEventListener("pointerup", scheduleSelectionNotification);
    container.removeEventListener("touchend", scheduleSelectionNotification);
    const frame = pendingFrame();
    if (frame !== null) {
        window.cancelAnimationFrame(frame);
    }
    selectionObserverHandlers = null;
}

export function getSelectionText(container) {
    return getSelectionDetails(container)?.text ?? "";
}

export function clearSelection() {
    window.getSelection()?.removeAllRanges();
}

export async function copyText(text) {
    if (!text) {
        return false;
    }

    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

export function getDefaultContentPaddingPercent() {
    return window.matchMedia("(max-width: 47.98rem)").matches ? 5 : 25;
}

// Reserve a couple of CSS pixels inside the column edge so sub-pixel rounding
// between the measured container width and the browser's actual (fractional)
// column layout never clips a glyph at the page boundary.
const PAGE_WIDTH_SAFETY_MARGIN_PX = 2;

function getPageMetrics(container) {
    const preciseWidth = container.getBoundingClientRect().width;
    const pageWidth = Math.max(1, Math.floor(preciseWidth) - PAGE_WIDTH_SAFETY_MARGIN_PX);
    container.style.setProperty("--epub-reader-page-width", `${pageWidth}px`);

    const chapter = container.querySelector(".epub-chapter");
    const chapterStyle = chapter ? getComputedStyle(chapter) : null;
    const columnGap = Math.max(0, Number.parseFloat(chapterStyle?.columnGap ?? "0") || 0);

    return {
        pageWidth,
        columnGap,
        pageStride: pageWidth + columnGap
    };
}

export function measurePagination(container) {
    if (!container) {
        return 1;
    }

    const { pageWidth, pageStride } = getPageMetrics(container);
    container.scrollLeft = Math.min(container.scrollLeft, Math.max(0, container.scrollWidth - pageWidth));

    return Math.max(1, Math.round((container.scrollWidth + pageStride - pageWidth) / pageStride));
}

export function goToPage(container, pageIndex) {
    if (!container) {
        return;
    }

    const { pageWidth, pageStride } = getPageMetrics(container);
    const maxScrollLeft = Math.max(0, container.scrollWidth - pageWidth);
    container.scrollLeft = Math.min(maxScrollLeft, Math.max(0, pageIndex) * pageStride);
}

export function getPageFraction(container) {
    if (!container) {
        return 0;
    }

    const { pageWidth, pageStride } = getPageMetrics(container);
    const pageCount = Math.max(1, Math.round((container.scrollWidth + pageStride - pageWidth) / pageStride));
    if (pageCount <= 1) {
        return 0;
    }

    return Math.min(1, Math.max(0, Math.round(container.scrollLeft / pageStride) / (pageCount - 1)));
}

export function getVisibleWordOffset(container, chapterWordCount) {
    const wordCount = Math.max(0, Number.parseInt(chapterWordCount, 10) || 0);
    if (!container || wordCount === 0) {
        return 0;
    }

    return Math.round(getPageFraction(container) * wordCount);
}
const fullscreenHandlers = new WeakMap();

export function registerFullscreenChange(element, dotNetReference) {
    const handler = () => dotNetReference.invokeMethodAsync("OnFullscreenChanged", document.fullscreenElement === element).catch(() => { });
    fullscreenHandlers.set(element, handler);
    document.addEventListener("fullscreenchange", handler);
}

export function unregisterFullscreenChange(element) {
    const handler = fullscreenHandlers.get(element);
    if (handler) document.removeEventListener("fullscreenchange", handler);
    fullscreenHandlers.delete(element);
}

export async function toggleFullscreen(element) {
    if (document.fullscreenElement === element) await document.exitFullscreen();
    else await element.requestFullscreen();
}
