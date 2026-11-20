// ARES-Editor 3D container viewer (v0): every loot container of a map is a small
// sphere in space, every loose item socket a smaller diamond (loose-sockets.js);
// clicking one reports its index back to Blazor.
//
// Loaded from Blazor with:
//   var module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/scene.js");
// Relies on the index.html import map for "three".
//
// Coordinate conversion (Unreal -> three.js), done once in setContainers:
//   Unreal: centimetres, Z-up, left-handed (X forward, Y right, Z up).
//   three:  metres,      Y-up, right-handed.
//   three = (ue.x, ue.z, ue.y) * 0.01
//   Swapping Y and Z both makes Y the up axis and flips handedness, so the map
//   is not mirrored. All distances below are in metres.
//
// Rendering is on demand: a frame is drawn only when the camera moves (incl.
// held fly keys), data changes or the focus animation runs. Idle = zero GPU work.
// Camera navigation is Unity-style, see editor-camera.js.

import * as THREE from 'three';
import { EditorCamera } from './editor-camera.js';
import { createMapGeometry } from './map-geometry.js';
import { createLootAreas } from './loot-areas.js';
import { createFillGroups } from './fill-groups.js';
import { createLooseSockets } from './loose-sockets.js';

const UE_TO_M = 0.01;
const RADIUS = 0.2;             // container sphere radius (40 cm diameter)
const LOOSE_RADIUS = 0.1;       // loose item socket diamond radius (loose-sockets.js)
const MIN_RADIUS_PX = 2.5;        // spheres never shrink below this on-screen radius (CSS px)
const PICK_TOLERANCE_PX = 3;    // extra slack around the drawn sphere when picking
const DRAG_THRESHOLD_PX = 5;    // pointer travel above this is a drag, not a click
const HOVER_THROTTLE_MS = 40;
const DIM_FACTOR = 0.25;        // brightness of non-highlighted containers
const SELECTED_SCALE = 1.8;
const RING_PX = 30;             // selection ring size on screen (CSS px)
const FOCUS_DISTANCE = 30;      // camera distance after focus()
const FOCUS_MS = 700;
const BACKGROUND = 0x14171c;
const GRID_MAJOR = 0x3a4352;
const GRID_MINOR = 0x222730;
const MARKER = 0xffc83d;
const DEFAULT_COLOR = [0.7, 0.7, 0.7];
const XRAY_OFF = 0, XRAY_NEAR = 1, XRAY_ALL = 2;   // setXrayMode modes
const XRAY_ORDER = 2;           // the X-ray pass draws after the loot areas (loot-areas.js)

/** @type {Map<number, object>} */
const scenes = new Map();
let nextHandle = 1;

// Shared temporaries (single-threaded, never held across calls).
const tmpColor = new THREE.Color();
const tmpMatrix = new THREE.Matrix4();
const tmpVec = new THREE.Vector3();
const viewProj = new THREE.Matrix4();

// ---------------------------------------------------------------------------
// Container material: instanced, per-instance colour, head-light shading, fog,
// and a minimum on-screen size so far-away containers stay visible as dots.
// Containers are drawn in two passes over the same instances, split on the GPU
// by their distance to the camera: the normal pass (depth-tested against the map)
// draws those farther than uXrayDistance, the X-ray pass (xray = true, depth
// cleared first) those within it. The other pass's instances are clipped away.
// ---------------------------------------------------------------------------

function createContainerUniforms() {
    return {
        uRadius: { value: RADIUS },
        uMinPx: { value: MIN_RADIUS_PX },   // device px, set on resize
        uViewPerPx: { value: 0.001 },       // view-space height of 1 px at depth 1
        uXrayDistance: { value: 50 },       // metres; < 0 = none, huge = all
    };
}

/** @param {object} shared uniforms of createContainerUniforms(), shared by both passes. */
function createContainerMaterial(shared, xray) {
    return new THREE.ShaderMaterial({
        fog: true,
        // The X-ray pass is opaque in effect (alpha 1) but sorted with the transparent
        // objects, so it draws after the loot areas.
        transparent: xray,
        defines: xray ? { XRAY_PASS: '' } : {},
        uniforms: { ...THREE.UniformsUtils.clone(THREE.UniformsLib.fog), ...shared },
        vertexShader: /* glsl */ `
            uniform float uRadius;
            uniform float uMinPx;
            uniform float uViewPerPx;
            uniform float uXrayDistance;
            varying vec3 vColor;
            varying vec3 vNormal;
            varying vec3 vViewPos;
            #include <fog_pars_vertex>
            void main() {
                vec3 worldCenter = (modelMatrix * instanceMatrix * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
                bool near = distance(cameraPosition, worldCenter) <= uXrayDistance;
            #ifdef XRAY_PASS
                if (!near) { gl_Position = vec4(0.0, 0.0, -2.0, 1.0); return; }   // clipped: the other pass draws it
            #else
                if (near) { gl_Position = vec4(0.0, 0.0, -2.0, 1.0); return; }
            #endif
                vec4 center = modelViewMatrix * instanceMatrix * vec4(0.0, 0.0, 0.0, 1.0);
                float depth = max(-center.z, 1e-3);
                float instScale = length(instanceMatrix[0].xyz);
                // Grow the sphere when its projected radius would be below uMinPx.
                float grow = max(1.0, uMinPx * depth * uViewPerPx / (uRadius * instScale));
                vec4 mvPosition = modelViewMatrix * instanceMatrix * vec4(position * grow, 1.0);
                vNormal = normalize(normalMatrix * mat3(instanceMatrix) * normal);
                vViewPos = mvPosition.xyz;
                vColor = instanceColor;
                gl_Position = projectionMatrix * mvPosition;
                #include <fog_vertex>
            }`,
        fragmentShader: /* glsl */ `
            varying vec3 vColor;
            varying vec3 vNormal;
            varying vec3 vViewPos;
            #include <fog_pars_fragment>
            void main() {
                float ndl = max(dot(normalize(vNormal), normalize(-vViewPos)), 0.0);
                gl_FragColor = vec4(vColor * (0.45 + 0.55 * ndl), 1.0);
                #include <colorspace_fragment>
                #include <fog_fragment>
            }`,
    });
}

function createRingTexture() {
    const size = 128;
    const c = document.createElement('canvas');
    c.width = c.height = size;
    const g = c.getContext('2d');
    g.strokeStyle = '#ffffff';
    g.lineWidth = 10;
    g.beginPath();
    g.arc(size / 2, size / 2, size / 2 - 8, 0, Math.PI * 2);
    g.stroke();
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace;
    return tex;
}

/** Rounds up to a 1/2/5 x 10^n step. */
function niceStep(raw) {
    const p = Math.pow(10, Math.floor(Math.log10(raw)));
    const m = raw / p;
    return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p;
}

function disposeObject(obj) {
    if (!obj) return;
    obj.parent?.remove(obj);
    obj.geometry?.dispose();
    obj.material?.dispose();
}

function safeInvoke(dotNetRef, method, index) {
    if (!dotNetRef) return;
    try {
        const p = dotNetRef.invokeMethodAsync(method, index);
        p?.catch?.((e) => console.warn(`[scene] ${method} failed:`, e));
    } catch (e) {
        console.warn(`[scene] ${method} failed:`, e);
    }
}

function getState(handle) {
    const s = scenes.get(handle);
    if (!s) console.warn(`[scene] unknown handle ${handle}`);
    return s;
}

// ---------------------------------------------------------------------------
// Scene lifecycle
// ---------------------------------------------------------------------------

/**
 * Creates a viewer on a canvas. The canvas fills its parent element; the
 * parent must have a definite size (it is watched with a ResizeObserver).
 * @param {HTMLCanvasElement} canvas
 * @param {{invokeMethodAsync(method: string, ...args: any[]): Promise<any>}} dotNetRef
 *   DotNetObjectReference; receives OnContainerPicked(int index) on click
 *   (-1 = empty space) and OnContainerHovered(int index) (-1 = none), both
 *   indices into the last setContainers() data; for loose item sockets
 *   OnLooseSocketPicked(int index) on click and OnLooseSocketHovered(int index)
 *   (-1 = none), indices into the last setLooseSockets() data.
 * @param {HTMLElement} [labelLayer] element over the canvas for the loot area
 *   labels (its children are owned by the view); default: the canvas's parent.
 * @returns {number} scene handle for the other exports.
 */
export function createScene(canvas, dotNetRef, labelLayer) {
    const handle = nextHandle++;
    canvas.style.display = 'block';
    canvas.style.width = '100%';
    canvas.style.height = '100%';

    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    renderer.setClearColor(BACKGROUND, 1);

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(BACKGROUND);
    scene.fog = new THREE.Fog(BACKGROUND, 1000, 5000);

    const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 20000);   // near/far adapt per frame
    camera.position.set(0, 300, 500);

    // Unity-style navigation (editor-camera.js); callbacks are wired below.
    const controls = new EditorCamera(camera, canvas);

    const containerUniforms = createContainerUniforms();
    const material = createContainerMaterial(containerUniforms, false);
    const xrayMaterial = createContainerMaterial(containerUniforms, true);
    const geometry = new THREE.IcosahedronGeometry(RADIUS, 1);
    // Loose item sockets share every container uniform except the marker radius.
    const looseUniforms = { ...containerUniforms, uRadius: { value: LOOSE_RADIUS } };

    // Selection marker: constant-size ring (always on top) + drop line to the grid.
    const ringTexture = createRingTexture();
    const ring = new THREE.Sprite(new THREE.SpriteMaterial({
        map: ringTexture, color: MARKER, sizeAttenuation: false,
        depthTest: false, depthWrite: false, transparent: true, fog: false,
    }));
    ring.renderOrder = 10;
    ring.visible = false;
    scene.add(ring);
    const dropLine = new THREE.Line(
        new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3()]),
        new THREE.LineBasicMaterial({ color: MARKER, transparent: true, opacity: 0.55 }));
    dropLine.frustumCulled = false;
    dropLine.visible = false;
    scene.add(dropLine);

    const s = {
        handle, canvas, dotNetRef, renderer, scene, camera, controls, material, xrayMaterial, geometry,
        ring, ringTexture, dropLine,
        mesh: null, xrayMesh: null, grids: [], gridY: 0,
        xrayMode: XRAY_NEAR, xrayDistance: 50,
        count: 0, pos: new Float32Array(0), baseColors: new Float32Array(0),
        highlight: null,              // Uint8Array mask, or null = no highlighting
        selected: -1, hovered: -1, looseHovered: -1,
        dataRadius: 500, framed: false,
        width: 1, height: 1,
        frameId: 0, anim: null, disposed: false,
        down: null, hoverTimer: 0, lastMove: null,
        listeners: [],
    };

    s.requestRender = () => {
        if (s.frameId || s.disposed) return;
        s.frameId = requestAnimationFrame(() => renderFrame(s));
    };
    controls.onChange = s.requestRender;
    s.areas = createLootAreas({ scene, labelLayer: labelLayer || canvas.parentElement || canvas, requestRender: s.requestRender });
    s.limiters = createLootAreas({
        scene, labelLayer: labelLayer || canvas.parentElement || canvas, requestRender: s.requestRender, name: 'loot-limiters', labelsBelow: true,
    });
    s.fillGroups = createFillGroups({ scene, ringTexture, requestRender: s.requestRender });
    s.loose = createLooseSockets({
        scene, geometry: new THREE.OctahedronGeometry(LOOSE_RADIUS),
        material: createContainerMaterial(looseUniforms, false), xrayMaterial: createContainerMaterial(looseUniforms, true),
        xrayOrder: XRAY_ORDER + 1, requestRender: s.requestRender,
    });
    controls.onStart = () => { s.anim = null; };   // user input cancels focus animation
    controls.onFrame = () => {                     // F: selected container or loose socket, else all data
        if (s.selected >= 0) focus(handle, s.selected);
        else if (s.loose.selected >= 0) focusLoose(handle, s.loose.selected);
        else frameAll(s);
    };

    const on = (target, type, fn) => {
        target.addEventListener(type, fn);
        s.listeners.push([target, type, fn]);
    };
    on(canvas, 'pointerdown', (e) => {
        if (e.button === 0 && !e.altKey) s.down = { x: e.clientX, y: e.clientY };   // Alt+LMB orbits
    });
    on(canvas, 'pointerup', (e) => {
        if (e.button !== 0 || !s.down) return;
        const moved = Math.hypot(e.clientX - s.down.x, e.clientY - s.down.y);
        s.down = null;
        if (moved > DRAG_THRESHOLD_PX) return;
        const hit = pick(s, e.clientX, e.clientY);
        if (hit.loose >= 0) safeInvoke(s.dotNetRef, 'OnLooseSocketPicked', hit.loose);
        else safeInvoke(s.dotNetRef, 'OnContainerPicked', hit.container);
    });
    on(canvas, 'pointermove', (e) => {
        if (e.buttons !== 0) return;          // no hover feedback while orbiting/panning
        s.lastMove = { x: e.clientX, y: e.clientY };
        if (!s.hoverTimer) s.hoverTimer = setTimeout(() => updateHover(s), HOVER_THROTTLE_MS);
    });
    on(canvas, 'pointerleave', () => {
        clearTimeout(s.hoverTimer);
        s.hoverTimer = 0;
        s.lastMove = null;
        setHovered(s, { container: -1, loose: -1 });
    });
    on(canvas, 'webglcontextrestored', s.requestRender);

    s.resizeObserver = new ResizeObserver(() => resize(s));
    s.resizeObserver.observe(canvas.parentElement || canvas);

    scenes.set(handle, s);
    rebuildGrid(s, new THREE.Box3(new THREE.Vector3(-250, 0, -250), new THREE.Vector3(250, 0, 250)));
    resize(s);
    return handle;
}

/**
 * Stops rendering, removes listeners and frees GPU resources. The handle is
 * invalid afterwards (later calls with it are ignored).
 * @param {number} handle
 */
export function dispose(handle) {
    const s = scenes.get(handle);
    if (!s) return;
    s.disposed = true;
    scenes.delete(handle);
    cancelAnimationFrame(s.frameId);
    clearTimeout(s.hoverTimer);
    s.resizeObserver.disconnect();
    for (const [t, type, fn] of s.listeners) t.removeEventListener(type, fn);
    s.controls.dispose();
    removeContainerMeshes(s);
    s.areas.dispose();
    s.limiters.dispose();
    s.fillGroups.dispose();
    s.loose.dispose();
    for (const g of s.grids) disposeObject(g);
    s.ring.material.dispose();          // Sprite geometry is shared by three.js: keep it
    disposeObject(s.dropLine);
    s.ringTexture.dispose();
    s.mapGeometry?.dispose();
    s.geometry.dispose();
    s.material.dispose();
    s.xrayMaterial.dispose();
    s.renderer.dispose();
    s.dotNetRef = null;
}

// ---------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------

/**
 * Replaces all containers. Resets highlight and selection (old indices are
 * meaningless for new data). Frames the camera the first time data arrives.
 * @param {number} handle
 * @param {number[]} positions flat [x0,y0,z0, x1,...] in Unreal units (cm, Z-up).
 * @param {number[]} colors flat [r0,g0,b0, ...] sRGB 0..1, one triple per container.
 */
export function setContainers(handle, positions, colors) {
    const s = getState(handle);
    if (!s) return;
    const n = Math.floor((positions?.length ?? 0) / 3);

    removeContainerMeshes(s);
    s.count = n;
    s.pos = new Float32Array(n * 3);
    s.baseColors = new Float32Array(n * 3);
    s.highlight = null;
    s.selected = -1;
    s.hovered = -1;
    updateMarker(s);
    s.fillGroups.clear();

    const box = new THREE.Box3();
    for (let i = 0; i < n; i++) {
        const ux = positions[i * 3], uy = positions[i * 3 + 1], uz = positions[i * 3 + 2];
        // Unreal (x, y, z) cm, Z-up LH  ->  three (x, z, y) m, Y-up RH
        s.pos[i * 3] = ux * UE_TO_M;
        s.pos[i * 3 + 1] = uz * UE_TO_M;
        s.pos[i * 3 + 2] = uy * UE_TO_M;
        box.expandByPoint(tmpVec.fromArray(s.pos, i * 3));
    }
    writeBaseColors(s, colors);

    if (n > 0) {
        const mesh = new THREE.InstancedMesh(s.geometry, s.material, n);
        mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(n * 3), 3);
        mesh.frustumCulled = false;   // min-size growth can exceed the computed bounds
        for (let i = 0; i < n; i++) {
            mesh.setMatrixAt(i, tmpMatrix.makeTranslation(s.pos[i * 3], s.pos[i * 3 + 1], s.pos[i * 3 + 2]));
        }
        mesh.instanceMatrix.needsUpdate = true;
        // The X-ray pass draws the same instances (shared matrices and colours).
        const xrayMesh = new THREE.InstancedMesh(s.geometry, s.xrayMaterial, n);
        xrayMesh.instanceMatrix = mesh.instanceMatrix;
        xrayMesh.instanceColor = mesh.instanceColor;
        xrayMesh.frustumCulled = false;
        xrayMesh.renderOrder = XRAY_ORDER;
        xrayMesh.onBeforeRender = clearDepthBeforeContainers;
        s.mesh = mesh;
        s.xrayMesh = xrayMesh;
        applyXray(s);
        s.scene.add(mesh, xrayMesh);
        applyColors(s);

        const sphere = box.getBoundingSphere(new THREE.Sphere());
        s.dataRadius = Math.max(sphere.radius, 20);
        s.dataCenter = sphere.center;      // F with no selection frames this
        rebuildGrid(s, box);
        if (!s.framed) { frameBounds(s, sphere.center, s.dataRadius); s.framed = true; }
    }
    s.requestRender();
}

/**
 * Recolours containers without rebuilding (same count as setContainers).
 * @param {number} handle
 * @param {number[]} colors flat [r,g,b,...] sRGB 0..1.
 */
export function setColors(handle, colors) {
    const s = getState(handle);
    if (!s) return;
    writeBaseColors(s, colors);
    applyColors(s);
    s.requestRender();
}

/**
 * Keeps the given containers at full brightness and dims all others to 25%
 * (hue preserved). Null turns highlighting off; an empty array dims them all
 * (the highlight covers only loose item sockets).
 * @param {number} handle
 * @param {number[]|null} indices
 */
export function setHighlighted(handle, indices) {
    const s = getState(handle);
    if (!s) return;
    if (!indices) {
        s.highlight = null;
    } else {
        s.highlight = new Uint8Array(s.count);
        for (const i of indices) if (i >= 0 && i < s.count) s.highlight[i] = 1;
    }
    applyColors(s);
    s.requestRender();
}

/**
 * Marks one container as selected (white, enlarged, ring + drop line to the
 * grid). -1 clears the selection.
 * @param {number} handle
 * @param {number} index
 */
export function setSelected(handle, index) {
    const s = getState(handle);
    if (!s) return;
    if (!(index >= 0 && index < s.count)) index = -1;
    const prev = s.selected;
    s.selected = index;
    if (s.mesh) {
        if (prev >= 0) setInstanceScale(s, prev, 1);
        if (index >= 0) setInstanceScale(s, index, SELECTED_SCALE);
        s.mesh.instanceMatrix.needsUpdate = true;
        applyColors(s);
    }
    updateMarker(s);
    s.requestRender();
}

/** Puts the selection ring and drop line on the selected container, else the selected loose socket, else hides them. */
function updateMarker(s) {
    const [pos, index] = s.selected >= 0 ? [s.pos, s.selected] : [s.loose.pos, s.loose.selected];
    if (index >= 0) {
        const x = pos[index * 3], y = pos[index * 3 + 1], z = pos[index * 3 + 2];
        s.ring.position.set(x, y, z);
        const attr = s.dropLine.geometry.attributes.position;
        attr.setXYZ(0, x, y, z);
        attr.setXYZ(1, x, s.gridY, z);
        attr.needsUpdate = true;
    }
    s.ring.visible = s.dropLine.visible = index >= 0;
}

/**
 * Smoothly moves the orbit target to a container and brings the camera within
 * ~30 m, keeping the current viewing direction.
 * @param {number} handle
 * @param {number} index
 */
export function focus(handle, index) {
    const s = getState(handle);
    if (!s || !(index >= 0 && index < s.count)) return;
    flyToPoint(s, s.pos, index);
}

/** Moves the orbit target to a point of a flat position array (focus, focusLoose). */
function flyToPoint(s, pos, index) {
    s.controls.cancel();               // stop any fly input: the animation owns the camera
    const target = new THREE.Vector3().fromArray(pos, index * 3);
    const offset = s.camera.position.clone().sub(s.controls.target);
    const dist = Math.min(offset.length(), FOCUS_DISTANCE);
    offset.setLength(dist);
    s.anim = {
        start: performance.now(),
        fromTarget: s.controls.target.clone(), toTarget: target,
        fromCam: s.camera.position.clone(), toCam: target.clone().add(offset),
    };
    s.requestRender();
}

// ---------------------------------------------------------------------------
// Loose item sockets (loose-sockets.js)
// ---------------------------------------------------------------------------

/**
 * Replaces all loose item sockets (resets their highlight and selection).
 * Picking and hovering report their indices to .NET as
 * OnLooseSocketPicked(int) and OnLooseSocketHovered(int).
 * @param {number} handle
 * @param {number[]} positions flat [x0,y0,z0, ...] in Unreal units (cm, Z-up).
 * @param {number[]} colors flat [r0,g0,b0, ...] sRGB 0..1.
 * @param {boolean[]} nature per socket: whether it is on nature (its own toggle).
 */
export function setLooseSockets(handle, positions, colors, nature) {
    const s = getState(handle);
    if (!s) return;
    s.loose.set(positions, colors, nature);
    s.looseHovered = -1;
    updateMarker(s);
}

/**
 * Shows or hides the loose item sockets: those on nature and the others apart.
 * @param {number} handle
 * @param {boolean} indoor
 * @param {boolean} nature
 */
export function setLooseVisible(handle, indoor, nature) {
    getState(handle)?.loose.setVisible(indoor, nature);
}

/**
 * Keeps the given loose sockets bright and dims the others; null = off.
 * @param {number} handle
 * @param {number[]|null} indices
 */
export function setLooseHighlighted(handle, indices) {
    getState(handle)?.loose.setHighlight(indices);
}

/**
 * Marks one loose socket as selected (white, enlarged, the selection ring
 * when no container is selected); -1 clears.
 * @param {number} handle
 * @param {number} index
 */
export function setLooseSelected(handle, index) {
    const s = getState(handle);
    if (!s) return;
    s.loose.setSelected(index);
    updateMarker(s);
    s.requestRender();
}

/**
 * Flies to a loose socket like focus() does to a container.
 * @param {number} handle
 * @param {number} index
 */
export function focusLoose(handle, index) {
    const s = getState(handle);
    if (!s || !(index >= 0 && index < s.loose.count)) return;
    flyToPoint(s, s.loose.pos, index);
}

/** Animated frame of all containers, keeping the current viewing direction (F with no selection). */
function frameAll(s) {
    if (s.count === 0 || !s.dataCenter) return;
    flyToSphere(s, s.dataCenter, s.dataRadius);
}

/** Animated frame of a sphere (three.js space), keeping the current viewing direction. */
function flyToSphere(s, center, radius) {
    s.controls.cancel();
    const fitDist = radius / Math.sin(THREE.MathUtils.degToRad(s.camera.fov / 2)) * 0.95;
    const dir = s.camera.position.clone().sub(s.controls.target).normalize();
    if (dir.lengthSq() === 0) dir.set(0, 0.75, 1).normalize();
    s.anim = {
        start: performance.now(),
        fromTarget: s.controls.target.clone(), toTarget: center.clone(),
        fromCam: s.camera.position.clone(), toCam: center.clone().addScaledVector(dir, fitDist),
    };
    s.requestRender();
}

/**
 * Outlines the groups of a loot rule's group fill (fill-groups.js), each a list
 * of container indices into the last setContainers() data; empty = none.
 * @param {number} handle
 * @param {number[][]} groups
 */
export function setFillGroups(handle, groups) {
    const s = getState(handle);
    s?.fillGroups.setGroups(groups, s.pos, s.count);
}

/**
 * Marks the selected container's fill group: rings on the other members and
 * spokes from the group's centre (fill-groups.js); empty = none.
 * @param {number} handle
 * @param {number[]} members container indices of the group.
 * @param {number} selected the selected container.
 */
export function setFillGroup(handle, members, selected) {
    const s = getState(handle);
    s?.fillGroups.setSelectedGroup(members, selected, s.pos, s.count);
}

// ---------------------------------------------------------------------------
// Map geometry (map-geometry.js) and X-ray containers
// ---------------------------------------------------------------------------

/**
 * Replaces the map's collision geometry under the containers (null or no
 * chunks = none). Chunks load in the background, nearest to the camera first;
 * the view stays usable and redraws as each arrives. Progress goes to .NET as
 * OnGeometryProgress(loaded, failed). The grid is hidden while a map is shown.
 * @param {number} handle
 * @param {object|null} descriptor see createMapGeometry in map-geometry.js.
 */
export function setMap(handle, descriptor) {
    const s = getState(handle);
    if (!s) return;
    s.mapGeometry?.dispose();
    s.mapGeometry = null;
    if (descriptor?.chunks?.length > 0) {
        s.mapGeometry = createMapGeometry({
            scene: s.scene,
            requestRender: s.requestRender,
            viewPoint: () => s.camera.position,
            onProgress: (loaded, total, failed) => {
                try {
                    s.dotNetRef?.invokeMethodAsync('OnGeometryProgress', loaded, failed)
                        ?.catch?.((e) => console.warn('[scene] OnGeometryProgress failed:', e));
                } catch (e) {
                    console.warn('[scene] OnGeometryProgress failed:', e);
                }
            },
        }, descriptor);
    }
    for (const g of s.grids) g.visible = !s.mapGeometry;
    s.requestRender();
}

/**
 * Shows or hides the map geometry of one data layer (an index into the
 * descriptor's layers). Cells in no layer are always shown; a cell in several
 * layers is shown while any of them is.
 * @param {number} handle
 * @param {number} layer
 * @param {boolean} visible
 */
export function setLayerVisible(handle, layer, visible) {
    const s = getState(handle);
    if (!s) return;
    s.mapGeometry?.setLayerVisible(layer, visible);
}

/**
 * X-ray containers: which containers walls and floors cannot hide. X-rayed
 * containers are drawn last with the depth buffer cleared (they still hide each
 * other correctly); the others use the normal depth test against the map.
 * The distance is to the camera and is evaluated on the GPU every frame, so it
 * follows the camera without rebuilding anything.
 * @param {number} handle
 * @param {number} mode 0 = off (none), 1 = within `metres` of the camera (the
 *   default, 50 m), 2 = all.
 * @param {number} metres distance for mode 1.
 */
export function setXrayMode(handle, mode, metres) {
    const s = getState(handle);
    if (!s) return;
    s.xrayMode = mode === XRAY_OFF || mode === XRAY_ALL ? mode : XRAY_NEAR;
    if (Number.isFinite(metres) && metres >= 0) s.xrayDistance = metres;
    applyXray(s);
    s.requestRender();
}

function applyXray(s) {
    s.material.uniforms.uXrayDistance.value =
        s.xrayMode === XRAY_OFF ? -1 : s.xrayMode === XRAY_ALL ? 1e30 : s.xrayDistance;
    if (s.mesh) s.mesh.visible = s.xrayMode !== XRAY_ALL;
    if (s.xrayMesh) s.xrayMesh.visible = s.xrayMode !== XRAY_OFF;
    s.loose.setPasses(s.xrayMode !== XRAY_ALL, s.xrayMode !== XRAY_OFF);
}

function removeContainerMeshes(s) {
    for (const mesh of [s.mesh, s.xrayMesh]) {
        if (mesh) { s.scene.remove(mesh); mesh.dispose(); }
    }
    s.mesh = s.xrayMesh = null;
}

// ---------------------------------------------------------------------------
// Loot value areas (loot-areas.js)
// ---------------------------------------------------------------------------

/**
 * Replaces the loot value areas: translucent volumes with labels, coloured per
 * area. Not pickable: clicks still pick containers only.
 * @param {number} handle
 * @param {object[]} areas see LootAreas.set in loot-areas.js.
 */
export function setAreas(handle, areas) {
    getState(handle)?.areas.set(areas);
}

/**
 * Replaces the area labels' text (one per area, in setAreas order).
 * @param {number} handle
 * @param {string[]} labels
 */
export function setAreaLabels(handle, labels) {
    getState(handle)?.areas.setLabels(labels);
}

/**
 * Shows or hides the areas and their labels.
 * @param {number} handle
 * @param {boolean} visible
 */
export function setAreasVisible(handle, visible) {
    getState(handle)?.areas.setVisible(visible);
}

/**
 * Draws the areas of one loot value brighter (the value as the game data
 * stores it: 1 low, 2 medium, 3 high; 0 = none).
 * @param {number} handle
 * @param {number} value
 */
export function setAreaHighlight(handle, value) {
    getState(handle)?.areas.setHighlight(value);
}

/**
 * Flies the camera to frame an area (index in setAreas order), with the focus
 * animation.
 * @param {number} handle
 * @param {number} index
 */
export function frameArea(handle, index) {
    const s = getState(handle);
    const sphere = s?.areas.bounds(index);
    if (sphere) flyToSphere(s, sphere.center, Math.max(sphere.radius, 10));
}

// ---------------------------------------------------------------------------
// Loot placement limiters (a second loot-areas.js layer)
// ---------------------------------------------------------------------------

/**
 * Replaces the loot placement limiters: translucent volumes with a label under
 * each, like the areas. Not pickable.
 * @param {number} handle
 * @param {object[]} limiters see LootAreas.set in loot-areas.js.
 */
export function setLimiters(handle, limiters) {
    getState(handle)?.limiters.set(limiters);
}

/**
 * Shows or hides the limiters and their labels.
 * @param {number} handle
 * @param {boolean} visible
 */
export function setLimitersVisible(handle, visible) {
    getState(handle)?.limiters.setVisible(visible);
}

/**
 * Draws one limiter brighter (index in setLimiters order; -1 = none).
 * @param {number} handle
 * @param {number} index
 */
export function setLimiterHighlight(handle, index) {
    getState(handle)?.limiters.setHighlightIndex(index);
}

/**
 * Flies the camera to frame a limiter (index in setLimiters order).
 * @param {number} handle
 * @param {number} index
 */
export function frameLimiter(handle, index) {
    const s = getState(handle);
    const sphere = s?.limiters.bounds(index);
    if (sphere) flyToSphere(s, sphere.center, Math.max(sphere.radius, 10));
}

function clearDepthBeforeContainers(renderer) {
    renderer.state.buffers.depth.setMask(true);
    renderer.clearDepth();
}

// ---------------------------------------------------------------------------
// Internals
// ---------------------------------------------------------------------------

function writeBaseColors(s, colors) {
    const n = s.count;
    const m = Math.min(n, Math.floor((colors?.length ?? 0) / 3));
    for (let i = 0; i < n * 3; i++) {
        s.baseColors[i] = i < m * 3 ? colors[i] : DEFAULT_COLOR[i % 3];
    }
}

/** Base colour x highlight dimming x selection -> instanceColor (linear). */
function applyColors(s) {
    if (!s.mesh) return;
    const out = s.mesh.instanceColor.array;
    const base = s.baseColors;
    for (let i = 0; i < s.count; i++) {
        if (i === s.selected) {
            tmpColor.setRGB(1, 1, 1);
        } else {
            const f = s.highlight && !s.highlight[i] ? DIM_FACTOR : 1;
            tmpColor.setRGB(base[i * 3] * f, base[i * 3 + 1] * f, base[i * 3 + 2] * f, THREE.SRGBColorSpace);
        }
        out[i * 3] = tmpColor.r;
        out[i * 3 + 1] = tmpColor.g;
        out[i * 3 + 2] = tmpColor.b;
    }
    s.mesh.instanceColor.needsUpdate = true;
}

function setInstanceScale(s, i, scale) {
    tmpMatrix.makeScale(scale, scale, scale);
    tmpMatrix.setPosition(s.pos[i * 3], s.pos[i * 3 + 1], s.pos[i * 3 + 2]);
    s.mesh.setMatrixAt(i, tmpMatrix);
}

/** Ground grid under the data: major lines on a 1/2/5 step, minor at 1/5 of it. */
function rebuildGrid(s, box) {
    for (const g of s.grids) disposeObject(g);
    s.grids = [];
    const extent = Math.max(box.max.x - box.min.x, box.max.z - box.min.z, 50) * 1.2;
    const step = niceStep(extent / 24);
    const divisions = Math.ceil(extent / step);
    const size = divisions * step;
    const cx = Math.round((box.min.x + box.max.x) / 2 / step) * step;
    const cz = Math.round((box.min.z + box.max.z) / 2 / step) * step;
    s.gridY = box.min.y - 1;

    // Same colour for centre and other lines: no axis emphasis.
    const minor = new THREE.GridHelper(size, divisions * 5, GRID_MINOR, GRID_MINOR);
    const major = new THREE.GridHelper(size, divisions, GRID_MAJOR, GRID_MAJOR);
    minor.position.set(cx, s.gridY - 0.01, cz);
    major.position.set(cx, s.gridY, cz);
    s.grids.push(minor, major);
    minor.visible = major.visible = !s.mapGeometry;   // the map's landscape replaces the grid
    s.scene.add(minor, major);
}

function frameBounds(s, center, radius) {
    const { camera, controls } = s;
    const fitDist = radius / Math.sin(THREE.MathUtils.degToRad(camera.fov / 2));
    const dir = new THREE.Vector3(0, 0.75, 1).normalize();
    controls.target.copy(center);
    camera.position.copy(center).addScaledVector(dir, fitDist * 0.95);
    controls.update();                 // near/far follow in renderFrame
}

function resize(s) {
    if (s.disposed) return;
    const host = s.canvas.parentElement || s.canvas;
    const w = host.clientWidth, h = host.clientHeight;
    if (w === 0 || h === 0) return;   // hidden (e.g. inactive tab): keep last size
    const dpr = window.devicePixelRatio || 1;
    s.width = w;
    s.height = h;
    s.renderer.setPixelRatio(dpr);
    s.renderer.setSize(w, h, false);
    s.camera.aspect = w / h;
    s.camera.updateProjectionMatrix();
    const viewPerCssPx = 2 * Math.tan(THREE.MathUtils.degToRad(s.camera.fov / 2)) / h;
    s.material.uniforms.uViewPerPx.value = viewPerCssPx / dpr;
    s.material.uniforms.uMinPx.value = MIN_RADIUS_PX * dpr;
    s.ring.scale.setScalar(RING_PX * viewPerCssPx);  // sizeAttenuation:false => view units at depth 1
    s.requestRender();
}

function renderFrame(s) {
    s.frameId = 0;
    if (s.disposed) return;
    let animating = false;
    if (s.anim) {
        const t = Math.min(1, (performance.now() - s.anim.start) / FOCUS_MS);
        const k = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; // easeInOutCubic
        s.controls.target.lerpVectors(s.anim.fromTarget, s.anim.toTarget, k);
        s.camera.position.lerpVectors(s.anim.fromCam, s.anim.toCam, k);
        if (t >= 1) s.anim = null; else animating = true;
    }
    const moved = s.controls.update();   // applies WASD flight; true while keys keep it moving

    // Distance-based fog: near half of the data reads clearly, the far side fades.
    // The pivot travels with the camera while flying, so the fog does too.
    const dist = s.camera.position.distanceTo(s.controls.target);
    s.scene.fog.near = Math.max(0, dist - s.dataRadius * 0.5);
    s.scene.fog.far = dist + s.dataRadius * 2;

    // Clip planes: 0.1 m near when close (fly between containers), up to 1 m from far
    // away for depth precision; far past the fog end, so the whole map fits.
    const near = THREE.MathUtils.clamp(dist * 0.001, 0.1, 1);
    const far = Math.max(2000, dist + s.dataRadius * 4);
    if (near !== s.camera.near || far !== s.camera.far) {
        s.camera.near = near;
        s.camera.far = far;
        s.camera.updateProjectionMatrix();
    }

    s.renderer.render(s.scene, s.camera);
    s.areas.updateLabels(s.camera, s.width, s.height);
    s.limiters.updateLabels(s.camera, s.width, s.height);
    if (moved || animating) s.requestRender();
}

/**
 * Screen-space pick matching what is drawn (including the minimum on-screen
 * size): the nearest-to-camera container or shown loose socket whose disc
 * contains the pointer, as { container, loose } (the other one -1).
 * A plain Raycaster would miss far containers drawn larger than 40 cm.
 */
function pick(s, clientX, clientY) {
    const container = pickPoints(s, clientX, clientY, s.pos, s.count, RADIUS, s.selected, null);
    const loose = pickPoints(s, clientX, clientY, s.loose.pos, s.loose.count, LOOSE_RADIUS, s.loose.selected, (i) => s.loose.isShown(i));
    return loose.index >= 0 && loose.depth < container.depth
        ? { container: -1, loose: loose.index }
        : { container: container.index, loose: -1 };
}

/** The nearest point of a flat position array under the pointer, as { index, depth } (index -1 for none). */
function pickPoints(s, clientX, clientY, pos, count, radius, selected, shown) {
    if (count === 0) return { index: -1, depth: Infinity };
    const rect = s.canvas.getBoundingClientRect();
    const px = clientX - rect.left, py = clientY - rect.top;
    const { camera } = s;
    camera.updateMatrixWorld();
    viewProj.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
    const e = viewProj.elements;
    const pxPerView = 1 / (2 * Math.tan(THREE.MathUtils.degToRad(camera.fov / 2)) / rect.height);
    const near = camera.near;
    let best = -1, bestDepth = Infinity;
    for (let i = 0; i < count; i++) {
        if (shown && !shown(i)) continue;
        const x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2];
        const w = e[3] * x + e[7] * y + e[11] * z + e[15];   // = view depth for perspective
        if (w <= near) continue;
        const sx = ((e[0] * x + e[4] * y + e[8] * z + e[12]) / w * 0.5 + 0.5) * rect.width;
        const sy = (0.5 - (e[1] * x + e[5] * y + e[9] * z + e[13]) / w * 0.5) * rect.height;
        const scale = i === selected ? SELECTED_SCALE : 1;
        const r = Math.max(MIN_RADIUS_PX, radius * scale * pxPerView / w) + PICK_TOLERANCE_PX;
        const dx = sx - px, dy = sy - py;
        if (dx * dx + dy * dy <= r * r && w < bestDepth) { best = i; bestDepth = w; }
    }
    return { index: best, depth: bestDepth };
}

function updateHover(s) {
    s.hoverTimer = 0;
    if (s.disposed || !s.lastMove) return;
    setHovered(s, pick(s, s.lastMove.x, s.lastMove.y));
}

/** Reports a hover change of { container, loose } (the other one -1) to .NET. */
function setHovered(s, hit) {
    s.canvas.style.cursor = hit.container >= 0 || hit.loose >= 0 ? 'pointer' : '';
    if (hit.container !== s.hovered) {
        s.hovered = hit.container;
        safeInvoke(s.dotNetRef, 'OnContainerHovered', hit.container);
    }
    if (hit.loose !== s.looseHovered) {
        s.looseHovered = hit.loose;
        safeInvoke(s.dotNetRef, 'OnLooseSocketHovered', hit.loose);
    }
}
