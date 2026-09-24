const DEFAULT_SENSITIVITY = 0.002;
const DRAG_THRESHOLD = 6;
const MAX_PITCH = Math.PI / 3;
const MIN_ZOOM = 1;
const MAX_ZOOM = 3;
const WHEEL_SENSITIVITY = 0.001;
const HALF_PI = Math.PI / 2;

// Keeps the visible frustum inside the recorded 180-degree hemisphere.
export function computeLimits(horizontalFov, verticalFov) {
    return {
        maxYaw: Math.max(0, HALF_PI - horizontalFov / 2),
        maxPitch: Math.max(0, Math.min(MAX_PITCH, HALF_PI - verticalFov / 2))
    };
}

export function clamp(value, limit) {
    return Math.max(-limit, Math.min(limit, value));
}

export function clampZoom(value) {
    return Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, value));
}

// Zooming in narrows the vertical FOV; tan-space scaling keeps the projection consistent.
export function zoomedFov(baseFov, zoom) {
    return 2 * Math.atan(Math.tan(baseFov / 2) / zoom);
}

// Column-major view matrix: inverse of the camera rotation Ry(yaw) * Rx(pitch).
export function buildViewMatrix(yaw, pitch, out) {
    const cy = Math.cos(-yaw), sy = Math.sin(-yaw);
    const cp = Math.cos(-pitch), sp = Math.sin(-pitch);
    // Rx(-pitch) * Ry(-yaw)
    out[0] = cy;       out[1] = sp * sy;   out[2] = -cp * sy;  out[3] = 0;
    out[4] = 0;        out[5] = cp;        out[6] = sp;        out[7] = 0;
    out[8] = sy;       out[9] = -sp * cy;  out[10] = cp * cy;  out[11] = 0;
    out[12] = 0;       out[13] = 0;        out[14] = 0;        out[15] = 1;
    return out;
}

// Owns camera orientation for one canvas. Knows nothing about WebGL or Blazor.
export function createCameraController(canvas, { getLimits, sensitivity = DEFAULT_SENSITIVITY }) {
    const matrix = new Float32Array(16);
    let yaw = 0;
    let pitch = 0;
    let zoom = 1;
    let currentSensitivity = sensitivity;
    let drag = null;
    const pointers = new Map();
    let pinch = null;
    let suppressUp = false;

    function applyLimits() {
        const { maxYaw, maxPitch } = getLimits();
        yaw = clamp(yaw, maxYaw);
        pitch = clamp(pitch, maxPitch);
    }

    function pointerDistance() {
        const [a, b] = [...pointers.values()];
        return Math.hypot(a.x - b.x, a.y - b.y);
    }

    function capture(pointerId) {
        try {
            canvas.setPointerCapture(pointerId);
        } catch {
            // Capture is best-effort.
        }
    }

    function onPointerDown(event) {
        pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        if (pointers.size === 2) {
            // A second pointer turns the gesture into a pinch and cancels the drag.
            capture(event.pointerId);
            suppressUp = true;
            pinch = { startDistance: Math.max(1, pointerDistance()), startZoom: zoom };
            if (drag) {
                drag.moved = true;
            }
            return;
        }
        if (pointers.size > 2 || drag) {
            return;
        }
        drag = {
            pointerId: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            lastX: event.clientX,
            lastY: event.clientY,
            moved: false
        };
        capture(event.pointerId);
    }

    function onPointerMove(event) {
        if (pointers.has(event.pointerId)) {
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        }
        if (pinch && pointers.size === 2) {
            zoom = clampZoom(pinch.startZoom * pointerDistance() / pinch.startDistance);
            return;
        }
        if (!drag || drag.pointerId !== event.pointerId) {
            return;
        }
        if (!drag.moved) {
            drag.moved = Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) >= DRAG_THRESHOLD;
        }
        yaw += (event.clientX - drag.lastX) * currentSensitivity;
        pitch -= (event.clientY - drag.lastY) * currentSensitivity;
        drag.lastX = event.clientX;
        drag.lastY = event.clientY;
        applyLimits();
    }

    // Capture phase so a real drag never reaches Blazor's delegated pointerup (RevealControls).
    function onPointerUpCapture(event) {
        if (drag && drag.pointerId === event.pointerId && drag.moved) {
            event.stopPropagation();
        } else if (suppressUp && pointers.has(event.pointerId)) {
            event.stopPropagation();
        }
        release(event.pointerId);
    }

    function onPointerCancel(event) {
        release(event.pointerId);
    }

    function release(pointerId) {
        pointers.delete(pointerId);
        if (pointers.size === 0) {
            suppressUp = false;
        }
        if (pinch) {
            pinch = null;
            drag = null;
        } else if (drag && drag.pointerId === pointerId) {
            drag = null;
        }
        endDrag(pointerId);
    }

    function onWheel(event) {
        event.preventDefault();
        const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? 100 : 1;
        zoom = clampZoom(zoom * Math.exp(-event.deltaY * unit * WHEEL_SENSITIVITY));
    }

    function endDrag(pointerId) {
        try {
            if (canvas.hasPointerCapture?.(pointerId)) {
                canvas.releasePointerCapture(pointerId);
            }
        } catch {
            // Already released.
        }
    }

    function reset() {
        yaw = 0;
        pitch = 0;
        zoom = 1;
    }

    canvas.addEventListener('pointerdown', onPointerDown);
    canvas.addEventListener('pointermove', onPointerMove);
    canvas.addEventListener('pointerup', onPointerUpCapture, true);
    canvas.addEventListener('pointercancel', onPointerCancel);
    canvas.addEventListener('lostpointercapture', onPointerCancel);
    canvas.addEventListener('dblclick', reset);
    canvas.addEventListener('wheel', onWheel, { passive: false });

    return {
        getViewMatrix() {
            applyLimits();
            return buildViewMatrix(yaw, pitch, matrix);
        },
        getZoom() {
            return zoom;
        },
        reset,
        setSensitivity(value) {
            if (Number.isFinite(value) && value > 0) {
                currentSensitivity = value;
            }
        },
        dispose() {
            for (const pointerId of pointers.keys()) {
                endDrag(pointerId);
            }
            pointers.clear();
            drag = null;
            pinch = null;
            canvas.removeEventListener('pointerdown', onPointerDown);
            canvas.removeEventListener('pointermove', onPointerMove);
            canvas.removeEventListener('pointerup', onPointerUpCapture, true);
            canvas.removeEventListener('pointercancel', onPointerCancel);
            canvas.removeEventListener('lostpointercapture', onPointerCancel);
            canvas.removeEventListener('dblclick', reset);
            canvas.removeEventListener('wheel', onWheel);
        }
    };
}
