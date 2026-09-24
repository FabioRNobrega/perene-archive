const sessions = new WeakMap();

const HORIZONTAL_FOV_DEGREES = 100;
const MIN_VERTICAL_FOV = 60;
const MAX_VERTICAL_FOV = 110;
const LON_SEGMENTS = 48;
const LAT_SEGMENTS = 32;

const vertexSource = `
attribute vec3 aPosition;
attribute vec2 aUv;
uniform mat4 uProjection;
varying vec2 vUv;
void main() {
    vUv = aUv;
    gl_Position = uProjection * vec4(aPosition, 1.0);
}`;

const fragmentSource = `
precision mediump float;
uniform sampler2D uTexture;
varying vec2 vUv;
void main() {
    gl_FragColor = texture2D(uTexture, vUv);
}`;

function compile(gl, type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        const log = gl.getShaderInfoLog(shader);
        gl.deleteShader(shader);
        throw new Error(`Shader compile failed: ${log}`);
    }
    return shader;
}

function buildProgram(gl) {
    const vs = compile(gl, gl.VERTEX_SHADER, vertexSource);
    const fs = compile(gl, gl.FRAGMENT_SHADER, fragmentSource);
    const program = gl.createProgram();
    gl.attachShader(program, vs);
    gl.attachShader(program, fs);
    gl.linkProgram(program);
    gl.deleteShader(vs);
    gl.deleteShader(fs);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        const log = gl.getProgramInfoLog(program);
        gl.deleteProgram(program);
        throw new Error(`Program link failed: ${log}`);
    }
    return program;
}

// 180-degree hemisphere seen from its center. Longitude/latitude in [-90, 90] map to
// the left eye's u in [0, 0.5] and v in [0, 1] of the side-by-side frame.
function buildMesh() {
    const positions = [];
    const uvs = [];
    const indices = [];
    for (let y = 0; y <= LAT_SEGMENTS; y++) {
        const t = y / LAT_SEGMENTS;
        const lat = (0.5 - t) * Math.PI;
        for (let x = 0; x <= LON_SEGMENTS; x++) {
            const s = x / LON_SEGMENTS;
            const lon = (s - 0.5) * Math.PI;
            positions.push(
                Math.cos(lat) * Math.sin(lon),
                Math.sin(lat),
                -Math.cos(lat) * Math.cos(lon));
            uvs.push(s * 0.5, t);
        }
    }
    const stride = LON_SEGMENTS + 1;
    for (let y = 0; y < LAT_SEGMENTS; y++) {
        for (let x = 0; x < LON_SEGMENTS; x++) {
            const a = y * stride + x;
            const b = a + stride;
            indices.push(a, b, a + 1, a + 1, b, b + 1);
        }
    }
    return {
        positions: new Float32Array(positions),
        uvs: new Float32Array(uvs),
        indices: new Uint16Array(indices)
    };
}

function perspective(fovY, aspect, near, far) {
    const f = 1 / Math.tan(fovY / 2);
    const nf = 1 / (near - far);
    return new Float32Array([
        f / aspect, 0, 0, 0,
        0, f, 0, 0,
        0, 0, (far + near) * nf, -1,
        0, 0, 2 * far * near * nf, 0
    ]);
}

// The eye image is the left half of the frame, so its aspect comes from the real decoded size.
function verticalFov(videoWidth, videoHeight) {
    const eyeAspect = (videoWidth / 2) / videoHeight;
    const fov = 2 * Math.atan(Math.tan((HORIZONTAL_FOV_DEGREES * Math.PI / 180) / 2) / eyeAspect);
    const degrees = fov * 180 / Math.PI;
    return Math.min(MAX_VERTICAL_FOV, Math.max(MIN_VERTICAL_FOV, degrees)) * Math.PI / 180;
}

function resize(session) {
    const { canvas, gl } = session;
    const dpr = window.devicePixelRatio || 1;
    const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
    const height = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
    gl.viewport(0, 0, canvas.width, canvas.height);
    session.projectionDirty = true;
}

function draw(session) {
    const { gl, video, canvas } = session;
    if (video.readyState < 2 || video.videoWidth === 0 || video.videoHeight === 0) {
        return;
    }

    if (session.videoWidth !== video.videoWidth || session.videoHeight !== video.videoHeight) {
        session.videoWidth = video.videoWidth;
        session.videoHeight = video.videoHeight;
        session.projectionDirty = true;
    }

    if (session.projectionDirty) {
        const aspect = canvas.width / Math.max(1, canvas.height);
        const matrix = perspective(verticalFov(session.videoWidth, session.videoHeight), aspect, 0.1, 10);
        gl.uniformMatrix4fv(session.projectionLocation, false, matrix);
        session.projectionDirty = false;
    }

    gl.bindTexture(gl.TEXTURE_2D, session.texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, video);
    gl.clearColor(0, 0, 0, 1);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.drawElements(gl.TRIANGLES, session.indexCount, gl.UNSIGNED_SHORT, 0);
}

function frame(session) {
    if (session.stopped) {
        return;
    }
    if (!session.canvas.isConnected || !session.video.isConnected) {
        teardown(session);
        return;
    }
    try {
        draw(session);
    } catch {
        teardown(session);
        return;
    }
    session.frameHandle = requestAnimationFrame(() => frame(session));
}

function teardown(session) {
    if (session.stopped) {
        return;
    }
    session.stopped = true;
    cancelAnimationFrame(session.frameHandle);
    session.observer?.disconnect();
    const { gl } = session;
    if (!gl.isContextLost()) {
        gl.deleteTexture(session.texture);
        gl.deleteBuffer(session.positionBuffer);
        gl.deleteBuffer(session.uvBuffer);
        gl.deleteBuffer(session.indexBuffer);
        gl.deleteProgram(session.program);
    }
    sessions.delete(session.canvas);
}

export function startVrPov(video, canvas) {
    stopVrPov(canvas);

    const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
    if (!gl) {
        return false;
    }

    let session;
    try {
        const program = buildProgram(gl);
        gl.useProgram(program);

        const mesh = buildMesh();
        const positionBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, mesh.positions, gl.STATIC_DRAW);
        const positionLocation = gl.getAttribLocation(program, 'aPosition');
        gl.enableVertexAttribArray(positionLocation);
        gl.vertexAttribPointer(positionLocation, 3, gl.FLOAT, false, 0, 0);

        const uvBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, uvBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, mesh.uvs, gl.STATIC_DRAW);
        const uvLocation = gl.getAttribLocation(program, 'aUv');
        gl.enableVertexAttribArray(uvLocation);
        gl.vertexAttribPointer(uvLocation, 2, gl.FLOAT, false, 0, 0);

        const indexBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indexBuffer);
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, mesh.indices, gl.STATIC_DRAW);

        const texture = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        gl.uniform1i(gl.getUniformLocation(program, 'uTexture'), 0);

        session = {
            gl, video, canvas, program, texture, positionBuffer, uvBuffer, indexBuffer,
            projectionLocation: gl.getUniformLocation(program, 'uProjection'),
            indexCount: mesh.indices.length,
            videoWidth: 0,
            videoHeight: 0,
            projectionDirty: true,
            stopped: false,
            frameHandle: 0,
            observer: null
        };
    } catch {
        return false;
    }

    sessions.set(canvas, session);
    session.observer = new ResizeObserver(() => resize(session));
    session.observer.observe(canvas);
    resize(session);
    session.frameHandle = requestAnimationFrame(() => frame(session));
    return true;
}

export function resizeVrPov(canvas) {
    const session = sessions.get(canvas);
    if (session) {
        resize(session);
    }
}

export function stopVrPov(canvas) {
    const session = sessions.get(canvas);
    if (session) {
        teardown(session);
    }
}
