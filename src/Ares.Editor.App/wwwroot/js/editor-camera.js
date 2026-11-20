// Unity-Editor-style scene navigation for the 3D view (replaces OrbitControls).
//
//   RMB held          mouse-look + WASD fly, Q/E down/up (world vertical),
//                     Shift x4, wheel = fly speed (0.5..500 m/s)
//   MMB drag          pan in the view plane (point under the cursor follows it)
//   Alt + LMB drag    orbit around the pivot
//   Alt + RMB drag    dolly in/out
//   Wheel             dolly toward the pivot; pushes the pivot forward when close
//   F                 frame (callback: selected container, else all data)
//
// The camera always looks at `target` (the pivot); flying moves both. Movement
// keys also work while the canvas has keyboard focus (it takes focus on
// pointerdown). Keys use KeyboardEvent.code, so on AZERTY the same physical
// keys apply (ZQSD + A/E).
//
// Render on demand: the owner calls update() from its frame callback; it
// returns true while keys keep the camera moving. Input calls onChange to ask
// for a frame and onStart when the user starts moving the camera.

import * as THREE from 'three';

const LOOK_RAD_PER_PX = 0.0035;
const ORBIT_RAD_PER_PX = 0.005;
const PITCH_LIMIT = THREE.MathUtils.degToRad(89);
const MIN_PIVOT_DIST = 1;          // wheel/dolly never get closer: the pivot is pushed instead
const MAX_PIVOT_DIST = 10000;
const MIN_PUSH_STEP = 0.25;        // metres per wheel step once the pivot is being pushed
const WHEEL_DOLLY_PER_PX = 0.0015; // a 100 px notch = x0.86 / x1.16 of the pivot distance
const DRAG_DOLLY_PER_PX = 0.005;
const WHEEL_MAX_PX = 200;          // per event, so one fat trackpad/page event can't jump
const LINE_PX = 33, PAGE_PX = 800; // WheelEvent.deltaMode 1/2 -> px
const SPEED_MIN = 0.5, SPEED_MAX = 500, SPEED_DEFAULT = 20;
const SPEED_STEP_PER_100PX = 1.25;
const SHIFT_FACTOR = 4;
const MAX_DT = 0.1;                // seconds; a stalled frame never teleports the camera
const OVERLAY_MS = 900;
const HINT = 'RMB + WASD fly · QE down/up · Shift fast · RMB + wheel speed · MMB pan · Alt+LMB orbit · Alt+RMB dolly · wheel zoom · F frame';

const BUTTON_MASK = [1, 4, 2];     // PointerEvent.button -> PointerEvent.buttons bit
const MOVE_KEYS = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE']);
const WORLD_UP = new THREE.Vector3(0, 1, 0);

// Shared temporaries (single-threaded, never held across calls).
const tmpA = new THREE.Vector3();
const tmpB = new THREE.Vector3();
const tmpEuler = new THREE.Euler(0, 0, 0, 'YXZ');

const clamp = THREE.MathUtils.clamp;

function isEditable(el) {
    return !!el && (el.isContentEditable || el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT');
}

/** View direction for yaw/pitch (camera looks down -Z, Euler order YXZ). */
function forwardOf(yaw, pitch, out) {
    const c = Math.cos(pitch);
    return out.set(-Math.sin(yaw) * c, Math.sin(pitch), -Math.cos(yaw) * c);
}

export class EditorCamera {
    /**
     * @param {THREE.PerspectiveCamera} camera
     * @param {HTMLCanvasElement} canvas
     * @param {{onChange?: () => void, onStart?: () => void, onFrame?: () => void}} [callbacks]
     */
    constructor(camera, canvas, { onChange, onStart, onFrame } = {}) {
        this.camera = camera;
        this.canvas = canvas;
        this.target = new THREE.Vector3();
        this.flySpeed = SPEED_DEFAULT;
        this.onChange = onChange;
        this.onStart = onStart;
        this.onFrame = onFrame;
        this.keys = new Set();
        this.shift = false;
        this.drag = null;          // { mode: 'fly'|'pan'|'orbit'|'dolly', mask, id, x, y }
        this.lastT = 0;
        this.overlayTimer = 0;
        this.listeners = [];

        this.addedTabIndex = !canvas.hasAttribute('tabindex');
        if (this.addedTabIndex) canvas.setAttribute('tabindex', '0');
        canvas.style.outline = 'none';
        canvas.style.touchAction = 'none';
        canvas.style.userSelect = 'none';

        const host = canvas.parentElement;
        const box = 'position:absolute;pointer-events:none;font-size:11px;line-height:1.4;'
            + 'color:rgba(225,230,238,.75);background:rgba(20,22,26,.6);border-radius:6px;padding:3px 8px;';
        this.hint = null;
        this.overlay = null;
        if (host) {
            this.hint = document.createElement('div');
            this.hint.className = 'scene-controls-hint';
            this.hint.textContent = HINT;
            this.hint.style.cssText = box + 'bottom:38px;right:10px;max-width:calc(50% - 20px);text-align:right;';
            this.overlay = document.createElement('div');
            this.overlay.className = 'scene-speed';
            this.overlay.style.cssText = box + 'left:50%;bottom:40px;transform:translateX(-50%);font-size:13px;'
                + 'color:#fff;opacity:0;transition:opacity .25s;white-space:nowrap;';
            host.append(this.hint, this.overlay);
        }

        const on = (t, type, fn, opts) => {
            t.addEventListener(type, fn, opts);
            this.listeners.push([t, type, fn, opts]);
        };
        on(canvas, 'pointerdown', (e) => this.onPointerDown(e));
        on(canvas, 'pointermove', (e) => this.onPointerMove(e));
        on(canvas, 'pointerup', (e) => this.onPointerUp(e));
        on(canvas, 'pointercancel', () => this.cancel());
        on(canvas, 'lostpointercapture', () => { if (this.drag) this.cancel(); });
        on(canvas, 'wheel', (e) => this.onWheel(e), { passive: false });
        on(canvas, 'contextmenu', (e) => e.preventDefault());
        // No middle-click autoscroll / paste, no right-button text selection.
        on(canvas, 'mousedown', (e) => { if (e.button !== 0) e.preventDefault(); });
        on(canvas, 'auxclick', (e) => e.preventDefault());
        on(canvas, 'blur', () => { if (this.drag?.mode !== 'fly') this.releaseKeys(); });
        on(window, 'keydown', (e) => this.onKeyDown(e));
        on(window, 'keyup', (e) => this.onKeyUp(e));
        on(window, 'blur', () => this.cancel());
        on(document, 'visibilitychange', () => { if (document.hidden) this.cancel(); });
    }

    // -- public --------------------------------------------------------------

    /**
     * Applies key movement for the elapsed time and points the camera at the
     * target. Call once per rendered frame.
     * @returns {boolean} true while the camera keeps moving (request another frame).
     */
    update() {
        this.orient();
        const f = this.axis('KeyW', 'KeyS'), r = this.axis('KeyD', 'KeyA'), u = this.axis('KeyE', 'KeyQ');
        if (!f && !r && !u) { this.lastT = 0; return false; }
        const now = performance.now();
        const dt = this.lastT ? Math.min((now - this.lastT) / 1000, MAX_DT) : 1 / 60;
        this.lastT = now;
        const cam = this.camera;
        const dir = tmpA.subVectors(this.target, cam.position).normalize().multiplyScalar(f);
        dir.add(tmpB.set(1, 0, 0).applyQuaternion(cam.quaternion).multiplyScalar(r));
        dir.addScaledVector(WORLD_UP, u);
        if (dir.lengthSq() > 0) {
            dir.setLength(this.flySpeed * (this.shift ? SHIFT_FACTOR : 1) * dt);
            cam.position.add(dir);
            this.target.add(dir);
            this.orient();
        }
        return true;
    }

    /** Drops all held keys and buttons (the camera stops at once). */
    cancel() {
        this.releaseKeys();
        this.endDrag();
    }

    dispose() {
        this.cancel();
        clearTimeout(this.overlayTimer);
        for (const [t, type, fn, opts] of this.listeners) t.removeEventListener(type, fn, opts);
        this.listeners = [];
        this.hint?.remove();
        this.overlay?.remove();
        if (this.addedTabIndex) this.canvas.removeAttribute('tabindex');
    }

    // -- input ---------------------------------------------------------------

    onPointerDown(e) {
        if (e.pointerType !== 'touch') this.canvas.focus({ preventScroll: true });
        if (this.drag) return;
        let mode = null;
        if (e.button === 2) mode = e.altKey ? 'dolly' : 'fly';
        else if (e.button === 1) mode = 'pan';
        else if (e.button === 0 && e.altKey) mode = 'orbit';
        if (!mode) return;                       // plain LMB: picking (scene.js)
        e.preventDefault();
        this.drag = { mode, mask: BUTTON_MASK[e.button], id: e.pointerId, x: e.clientX, y: e.clientY };
        this.shift = e.shiftKey;
        try { this.canvas.setPointerCapture(e.pointerId); } catch { /* synthetic or stale id */ }
        this.onStart?.();
        if (mode === 'fly' && this.keys.size) this.onChange?.();
    }

    onPointerMove(e) {
        const d = this.drag;
        if (!d || e.pointerId !== d.id) return;
        if ((e.buttons & d.mask) === 0) { this.endDrag(); return; }   // button released mid-chord
        this.shift = e.shiftKey;
        const dx = e.clientX - d.x, dy = e.clientY - d.y;
        d.x = e.clientX;
        d.y = e.clientY;
        if (!dx && !dy) return;
        if (d.mode === 'fly') this.look(dx, dy);
        else if (d.mode === 'orbit') this.orbit(dx, dy);
        else if (d.mode === 'pan') this.pan(dx, dy);
        else this.dolly(Math.exp(-(dx - dy) * DRAG_DOLLY_PER_PX));   // right/up = in
        this.onChange?.();
    }

    onPointerUp(e) {
        const d = this.drag;
        if (d && e.pointerId === d.id && (e.buttons & d.mask) === 0) this.endDrag();
    }

    onWheel(e) {
        e.preventDefault();
        const scale = e.deltaMode === 1 ? LINE_PX : e.deltaMode === 2 ? PAGE_PX : 1;
        const px = clamp(e.deltaY * scale, -WHEEL_MAX_PX, WHEEL_MAX_PX);
        if (!px) return;
        if (this.drag?.mode === 'fly') {
            this.flySpeed = clamp(this.flySpeed * Math.pow(SPEED_STEP_PER_100PX, -px / 100), SPEED_MIN, SPEED_MAX);
            this.showSpeed();
            return;
        }
        this.onStart?.();
        this.dolly(Math.exp(px * WHEEL_DOLLY_PER_PX));
        this.onChange?.();
    }

    onKeyDown(e) {
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        const flying = this.drag?.mode === 'fly';
        if (!flying && (document.activeElement !== this.canvas || isEditable(e.target))) return;
        if (e.key === 'Shift') { this.shift = true; return; }
        if (e.code === 'KeyF') {
            e.preventDefault();
            if (!e.repeat) { this.cancel(); this.onFrame?.(); }
            return;
        }
        if (!MOVE_KEYS.has(e.code)) return;
        e.preventDefault();
        this.shift = e.shiftKey;
        if (e.repeat) return;    // a key held through cancel() stays released until pressed again
        if (!this.keys.size) this.lastT = 0;
        this.keys.add(e.code);
        this.onStart?.();
        this.onChange?.();
    }

    onKeyUp(e) {
        if (e.key === 'Shift') this.shift = false;
        else this.shift = e.shiftKey;
        this.keys.delete(e.code);
    }

    releaseKeys() {
        this.keys.clear();
        this.shift = false;
        this.lastT = 0;
    }

    endDrag() {
        const d = this.drag;
        if (!d) return;
        this.drag = null;          // first, so the lostpointercapture it causes is a no-op
        try {
            if (this.canvas.hasPointerCapture?.(d.id)) this.canvas.releasePointerCapture(d.id);
        } catch { /* already released */ }
    }

    // -- camera math ---------------------------------------------------------

    axis(plus, minus) {
        return (this.keys.has(plus) ? 1 : 0) - (this.keys.has(minus) ? 1 : 0);
    }

    /** {yaw, pitch, dist} of the camera -> target vector. */
    angles() {
        const o = tmpA.subVectors(this.target, this.camera.position);
        const dist = o.length();
        if (dist < 1e-9) return { yaw: 0, pitch: 0, dist: 0 };
        return { yaw: Math.atan2(-o.x, -o.z), pitch: Math.asin(clamp(o.y / dist, -1, 1)), dist };
    }

    /** Points the camera at the target, no roll. */
    orient() {
        const { yaw, pitch, dist } = this.angles();
        if (dist === 0) return;
        this.camera.quaternion.setFromEuler(tmpEuler.set(pitch, yaw, 0, 'YXZ'));
    }

    /** Turns the camera in place; the pivot stays at the same distance ahead. */
    look(dx, dy) {
        const a = this.angles();
        const dist = Math.max(a.dist, MIN_PIVOT_DIST);
        const yaw = a.yaw - dx * LOOK_RAD_PER_PX;
        const pitch = clamp(a.pitch - dy * LOOK_RAD_PER_PX, -PITCH_LIMIT, PITCH_LIMIT);
        this.target.copy(this.camera.position).addScaledVector(forwardOf(yaw, pitch, tmpB), dist);
        this.orient();
    }

    /** Moves the camera around the pivot. */
    orbit(dx, dy) {
        const a = this.angles();
        const dist = Math.max(a.dist, MIN_PIVOT_DIST);
        const yaw = a.yaw - dx * ORBIT_RAD_PER_PX;
        const pitch = clamp(a.pitch - dy * ORBIT_RAD_PER_PX, -PITCH_LIMIT, PITCH_LIMIT);
        this.camera.position.copy(this.target).addScaledVector(forwardOf(yaw, pitch, tmpB), -dist);
        this.orient();
    }

    /** Slides camera and pivot in the view plane; 1 px = 1 px at the pivot depth. */
    pan(dx, dy) {
        const cam = this.camera;
        this.orient();
        const h = this.canvas.clientHeight || 1;
        const dist = Math.max(cam.position.distanceTo(this.target), MIN_PIVOT_DIST);
        const k = 2 * dist * Math.tan(THREE.MathUtils.degToRad(cam.fov / 2)) / h;
        const move = tmpA.set(-dx * k, dy * k, 0).applyQuaternion(cam.quaternion);
        cam.position.add(move);
        this.target.add(move);
    }

    /**
     * Multiplies the pivot distance by `scale` (<1 = closer). Never passes
     * through the pivot: when it would get closer than MIN_PIVOT_DIST the
     * camera keeps moving forward and pushes the pivot ahead (like Unity).
     */
    dolly(scale) {
        const cam = this.camera;
        const offset = tmpA.subVectors(cam.position, this.target);
        let dist = offset.length();
        if (dist < 1e-9) { offset.set(0, 0, 1).applyQuaternion(cam.quaternion); dist = MIN_PIVOT_DIST; }
        const wanted = dist * scale;
        if (wanted >= MIN_PIVOT_DIST || scale >= 1) {
            cam.position.copy(this.target).addScaledVector(offset, clamp(wanted, MIN_PIVOT_DIST, MAX_PIVOT_DIST) / dist);
        } else {
            const fwd = offset.multiplyScalar(-1 / dist);
            const step = Math.max(dist - wanted, MIN_PUSH_STEP);
            cam.position.addScaledVector(fwd, step);
            this.target.copy(cam.position).addScaledVector(fwd, Math.max(dist, MIN_PIVOT_DIST));
        }
        this.orient();
    }

    showSpeed() {
        if (!this.overlay) return;
        const v = this.flySpeed;
        this.overlay.textContent = `Fly speed ${v < 10 ? v.toFixed(1) : Math.round(v)} m/s`;
        this.overlay.style.opacity = '1';
        clearTimeout(this.overlayTimer);
        this.overlayTimer = setTimeout(() => { if (this.overlay) this.overlay.style.opacity = '0'; }, OVERLAY_MS);
    }
}
