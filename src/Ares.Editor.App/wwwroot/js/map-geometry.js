// ARES-Editor map geometry (v1): the map's collision geometry (landscape + placed
// bodies) under the containers, loaded chunk by chunk from binary files the
// editor serves next to the page (Ares.Editor.Core MapGeometry / GeometryChunk).
//
// Used by scene.js (setMap / setLayerVisible); it owns one THREE.Group in the scene.
//
// Chunk file layout (little-endian): vertexCount x 3 Float32 positions, already in
// three.js space (metres, Y up: three = (ue.x, ue.z, ue.y) * 0.01, the convention
// of scene.js), then indexCount indices: Uint16 when shortIndices (padded to 4
// bytes), else Uint32.
//
// Shading: flat, from screen-space derivatives of the view position (no normal
// attribute), sun + hemisphere light, a slight height tint and the scene fog.
// Faces are double-sided so walls read from inside buildings.
// Rendering stays on demand: a frame is requested when a chunk arrives or a layer
// is toggled.

import * as THREE from 'three';

const CONCURRENT_FETCHES = 4;
const PROGRESS_INTERVAL_MS = 150;
const LANDSCAPE = 0;          // GeometryKind.Landscape
const LANDSCAPE_COLOR = 0x58604a;   // muted olive
const OBJECT_COLOR = 0xa9afba;      // light cool grey: buildings and props stand out
const SUN = new THREE.Vector3(0.45, 0.8, 0.3).normalize();

function createMaterial(color, heightRange, landscape) {
    return new THREE.ShaderMaterial({
        fog: true,
        side: THREE.DoubleSide,
        // Push the landscape back a little so objects resting on it win the depth test.
        polygonOffset: landscape,
        polygonOffsetFactor: landscape ? 1 : 0,
        polygonOffsetUnits: landscape ? 2 : 0,
        uniforms: THREE.UniformsUtils.merge([
            THREE.UniformsLib.fog,
            {
                uColor: { value: new THREE.Color(color) },
                uSun: { value: SUN.clone() },
                uHeightRange: { value: new THREE.Vector2(heightRange[0], heightRange[1]) },
            },
        ]),
        vertexShader: /* glsl */ `
            varying vec3 vViewPos;
            varying float vHeight;
            #include <fog_pars_vertex>
            void main() {
                vec4 mvPosition = modelViewMatrix * vec4(position, 1.0);
                vViewPos = mvPosition.xyz;
                vHeight = (modelMatrix * vec4(position, 1.0)).y;
                gl_Position = projectionMatrix * mvPosition;
                #include <fog_vertex>
            }`,
        fragmentShader: /* glsl */ `
            uniform vec3 uColor;
            uniform vec3 uSun;
            uniform vec2 uHeightRange;
            varying vec3 vViewPos;
            varying float vHeight;
            #include <fog_pars_fragment>
            void main() {
                // Face normal in view space, turned towards the camera, then to world space.
                vec3 n = normalize(cross(dFdx(vViewPos), dFdy(vViewPos)));
                if (dot(n, vViewPos) > 0.0) n = -n;
                vec3 nw = normalize((vec4(n, 0.0) * viewMatrix).xyz);
                float sun = max(dot(nw, uSun), 0.0);
                float sky = 0.5 + 0.5 * nw.y;
                float h = clamp((vHeight - uHeightRange.x) / max(uHeightRange.y - uHeightRange.x, 1.0), 0.0, 1.0);
                vec3 tint = mix(vec3(0.88, 0.92, 1.0), vec3(1.06, 1.03, 0.97), h);
                vec3 c = uColor * tint * (0.14 + 0.24 * sky + 0.62 * sun);
                gl_FragColor = vec4(c, 1.0);
                #include <colorspace_fragment>
                #include <fog_fragment>
            }`,
    });
}

function disposeArray() {
    this.array = null;   // three keeps the GPU buffer; the CPU copy is not needed after upload
}

/**
 * Creates the map geometry of one map in a scene.
 * @param {{scene: THREE.Scene, requestRender: () => void, viewPoint: () => THREE.Vector3,
 *          onProgress: (loaded: number, total: number, failed: number) => void}} host
 * @param {{baseUrl: string, layers: string[], visibleLayers: number[],
 *          chunks: {kind: number, layers: number[], vertexCount: number, indexCount: number,
 *                   shortIndices: boolean, min: number[], max: number[]}[]}} descriptor
 *   chunk i is fetched from baseUrl + i + '.bin'.
 */
export function createMapGeometry(host, descriptor) {
    const chunks = descriptor?.chunks ?? [];
    const group = new THREE.Group();
    group.name = 'map-geometry';
    host.scene.add(group);

    let low = Infinity, high = -Infinity;
    for (const c of chunks) { low = Math.min(low, c.min[1]); high = Math.max(high, c.max[1]); }
    if (!(low < high)) { low = 0; high = 1; }
    const materials = [
        createMaterial(LANDSCAPE_COLOR, [low, high], true),
        createMaterial(OBJECT_COLOR, [low, high], false),
    ];

    const layerOn = new Uint8Array(descriptor?.layers?.length ?? 0);
    for (const l of descriptor?.visibleLayers ?? []) if (l >= 0 && l < layerOn.length) layerOn[l] = 1;
    const meshes = [];          // [{ mesh, layers }]
    const abort = new AbortController();
    let loaded = 0, failed = 0, lastProgress = 0, disposed = false;

    const isVisible = (layers) => layers.length === 0 || layers.some((l) => layerOn[l]);

    const report = (force) => {
        const now = performance.now();
        if (!force && now - lastProgress < PROGRESS_INTERVAL_MS) return;
        lastProgress = now;
        host.onProgress(loaded, chunks.length, failed);
    };

    const addChunk = (c, buffer) => {
        const positions = new Float32Array(buffer, 0, c.vertexCount * 3);
        const indexOffset = c.vertexCount * 12;
        const indices = c.shortIndices
            ? new Uint16Array(buffer, indexOffset, c.indexCount)
            : new Uint32Array(buffer, indexOffset, c.indexCount);
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3).onUpload(disposeArray));
        geometry.setIndex(new THREE.BufferAttribute(indices, 1).onUpload(disposeArray));
        geometry.boundingBox = new THREE.Box3(new THREE.Vector3().fromArray(c.min), new THREE.Vector3().fromArray(c.max));
        geometry.boundingSphere = geometry.boundingBox.getBoundingSphere(new THREE.Sphere());
        const mesh = new THREE.Mesh(geometry, materials[c.kind === LANDSCAPE ? 0 : 1]);
        mesh.matrixAutoUpdate = false;
        mesh.visible = isVisible(c.layers);
        group.add(mesh);
        meshes.push({ mesh, layers: c.layers });
        if (mesh.visible) host.requestRender();
    };

    // Nearest chunks first (horizontal distance from the camera when loading starts).
    const view = host.viewPoint();
    const queue = chunks.map((c, i) => i).sort((a, b) => distance(chunks[a], view) - distance(chunks[b], view));

    const worker = async () => {
        while (queue.length > 0 && !disposed) {
            const i = queue.shift();
            try {
                const response = await fetch(`${descriptor.baseUrl}${i}.bin`, { signal: abort.signal });
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                const buffer = await response.arrayBuffer();
                if (disposed) return;
                if (buffer.byteLength < chunks[i].vertexCount * 12 + chunks[i].indexCount * (chunks[i].shortIndices ? 2 : 4)) {
                    throw new Error(`chunk ${i} is ${buffer.byteLength} bytes`);
                }
                addChunk(chunks[i], buffer);
                loaded++;
            } catch (e) {
                if (disposed) return;
                failed++;
                console.warn(`[map-geometry] chunk ${i} failed:`, e);
            }
            report(false);
        }
    };

    if (chunks.length > 0) {
        report(true);
        Promise.all(Array.from({ length: Math.min(CONCURRENT_FETCHES, chunks.length) }, worker))
            .then(() => { if (!disposed) report(true); });
    }

    return {
        /** Whether any geometry will be drawn. */
        get isEmpty() { return chunks.length === 0; },

        /** Shows or hides the chunks of a data layer (index into descriptor.layers). */
        setLayerVisible(layer, visible) {
            if (!(layer >= 0 && layer < layerOn.length)) return;
            layerOn[layer] = visible ? 1 : 0;
            for (const m of meshes) m.mesh.visible = isVisible(m.layers);
            host.requestRender();
        },

        /** Stops loading and frees every GPU resource. */
        dispose() {
            disposed = true;
            abort.abort();
            group.parent?.remove(group);
            for (const m of meshes) m.mesh.geometry.dispose();
            for (const material of materials) material.dispose();
            meshes.length = 0;
        },
    };
}

function distance(c, view) {
    const x = (c.min[0] + c.max[0]) / 2 - view.x;
    const z = (c.min[2] + c.max[2]) / 2 - view.z;
    return x * x + z * z;
}
