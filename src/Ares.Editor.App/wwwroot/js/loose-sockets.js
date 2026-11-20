// ARES-Editor loose item sockets: every loose item socket of a map is a small
// diamond, drawn like the containers (instanced, minimum on-screen size, the same
// X-ray passes) but with its own geometry and colours. Sockets on nature
// (trees, rocks) are a separate group that is shown or hidden on its own.
//
// Owned by scene.js, which picks, hovers, selects and focuses through the
// accessors below; positions are converted like scene.js containers
// (Unreal cm Z-up -> three metres Y-up).

import * as THREE from 'three';

const UE_TO_M = 0.01;
const DIM_FACTOR = 0.25;          // brightness of non-highlighted sockets
const SELECTED_SCALE = 1.8;
const DEFAULT_COLOR = [0.7, 0.7, 0.7];
const INDOOR = 0, NATURE = 1;      // group of a socket

/**
 * @param {object} opts
 * @param {THREE.Scene} opts.scene
 * @param {THREE.BufferGeometry} opts.geometry marker shape (owned: disposed here).
 * @param {THREE.Material} opts.material normal pass (owned).
 * @param {THREE.Material} opts.xrayMaterial X-ray pass (owned).
 * @param {number} opts.xrayOrder render order of the X-ray pass.
 * @param {() => void} opts.requestRender
 */
export function createLooseSockets({ scene, geometry, material, xrayMaterial, xrayOrder, requestRender }) {
    const tmpColor = new THREE.Color();
    const tmpMatrix = new THREE.Matrix4();
    let count = 0;
    let pos = new Float32Array(0);
    let baseColors = new Float32Array(0);
    let group = new Uint8Array(0);     // INDOOR or NATURE per socket
    let slot = new Int32Array(0);      // instance index within its group's meshes
    let highlight = null;              // Uint8Array mask, or null = no highlighting
    let selected = -1;
    const visible = [true, false];     // by group
    let normalPass = true, xrayPass = true;
    /** @type {{members: number[], mesh: THREE.InstancedMesh|null, xrayMesh: THREE.InstancedMesh|null}[]} */
    const groups = [{ members: [], mesh: null, xrayMesh: null }, { members: [], mesh: null, xrayMesh: null }];

    function removeMeshes() {
        for (const g of groups) {
            for (const mesh of [g.mesh, g.xrayMesh]) {
                if (mesh) { scene.remove(mesh); mesh.dispose(); }
            }
            g.members = [];
            g.mesh = g.xrayMesh = null;
        }
    }

    function applyVisibility() {
        groups.forEach((g, index) => {
            if (g.mesh) g.mesh.visible = visible[index] && normalPass;
            if (g.xrayMesh) g.xrayMesh.visible = visible[index] && xrayPass;
        });
    }

    function setInstanceScale(i, scale) {
        const g = groups[group[i]];
        if (!g.mesh) return;
        tmpMatrix.makeScale(scale, scale, scale);
        tmpMatrix.setPosition(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
        g.mesh.setMatrixAt(slot[i], tmpMatrix);
        g.mesh.instanceMatrix.needsUpdate = true;
    }

    /** Base colour x highlight dimming x selection -> instanceColor (linear). */
    function applyColors() {
        for (let i = 0; i < count; i++) {
            const g = groups[group[i]];
            if (!g.mesh) continue;
            if (i === selected) {
                tmpColor.setRGB(1, 1, 1);
            } else {
                const f = highlight && !highlight[i] ? DIM_FACTOR : 1;
                tmpColor.setRGB(baseColors[i * 3] * f, baseColors[i * 3 + 1] * f, baseColors[i * 3 + 2] * f, THREE.SRGBColorSpace);
            }
            const out = g.mesh.instanceColor.array;
            out[slot[i] * 3] = tmpColor.r;
            out[slot[i] * 3 + 1] = tmpColor.g;
            out[slot[i] * 3 + 2] = tmpColor.b;
        }
        for (const g of groups) if (g.mesh) g.mesh.instanceColor.needsUpdate = true;
    }

    return {
        get count() { return count; },
        get pos() { return pos; },
        get selected() { return selected; },

        /** Whether socket i is drawn (its group is shown). */
        isShown(i) { return i >= 0 && i < count && visible[group[i]]; },

        /**
         * Replaces all sockets; resets highlight and selection.
         * @param {number[]} positions flat [x,y,z,...] in Unreal units.
         * @param {number[]} colors flat [r,g,b,...] sRGB 0..1, one triple per socket.
         * @param {boolean[]} nature per socket: whether it is on nature.
         */
        set(positions, colors, nature) {
            removeMeshes();
            count = Math.floor((positions?.length ?? 0) / 3);
            pos = new Float32Array(count * 3);
            baseColors = new Float32Array(count * 3);
            group = new Uint8Array(count);
            slot = new Int32Array(count);
            highlight = null;
            selected = -1;
            const m = Math.min(count, Math.floor((colors?.length ?? 0) / 3));
            for (let i = 0; i < count; i++) {
                pos[i * 3] = positions[i * 3] * UE_TO_M;           // Unreal (x, y, z) -> three (x, z, y)
                pos[i * 3 + 1] = positions[i * 3 + 2] * UE_TO_M;
                pos[i * 3 + 2] = positions[i * 3 + 1] * UE_TO_M;
                for (let c = 0; c < 3; c++) baseColors[i * 3 + c] = i < m ? colors[i * 3 + c] : DEFAULT_COLOR[c];
                group[i] = nature?.[i] ? NATURE : INDOOR;
                const g = groups[group[i]];
                slot[i] = g.members.length;
                g.members.push(i);
            }
            for (const g of groups) {
                const n = g.members.length;
                if (n === 0) continue;
                const mesh = new THREE.InstancedMesh(geometry, material, n);
                mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(n * 3), 3);
                mesh.frustumCulled = false;   // min-size growth can exceed the computed bounds
                g.members.forEach((i, k) => mesh.setMatrixAt(k, tmpMatrix.makeTranslation(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2])));
                mesh.instanceMatrix.needsUpdate = true;
                // The X-ray pass draws the same instances after the containers' X-ray pass, which cleared the depth.
                const xrayMesh = new THREE.InstancedMesh(geometry, xrayMaterial, n);
                xrayMesh.instanceMatrix = mesh.instanceMatrix;
                xrayMesh.instanceColor = mesh.instanceColor;
                xrayMesh.frustumCulled = false;
                xrayMesh.renderOrder = xrayOrder;
                g.mesh = mesh;
                g.xrayMesh = xrayMesh;
                scene.add(mesh, xrayMesh);
            }
            applyVisibility();
            applyColors();
            requestRender();
        },

        /** Shows or hides the indoor (non-nature) and the nature sockets. */
        setVisible(indoor, natureShown) {
            visible[INDOOR] = !!indoor;
            visible[NATURE] = !!natureShown;
            applyVisibility();
            requestRender();
        },

        /** Which X-ray passes draw (scene.js applyXray). */
        setPasses(normal, xray) {
            normalPass = normal;
            xrayPass = xray;
            applyVisibility();
        },

        /** Keeps the given sockets bright and dims the others; null turns highlighting off. */
        setHighlight(indices) {
            if (!indices) {
                highlight = null;
            } else {
                highlight = new Uint8Array(count);
                for (const i of indices) if (i >= 0 && i < count) highlight[i] = 1;
            }
            applyColors();
            requestRender();
        },

        /** Marks one socket as selected (white, enlarged); -1 clears. */
        setSelected(index) {
            if (!(index >= 0 && index < count)) index = -1;
            if (selected >= 0) setInstanceScale(selected, 1);
            selected = index;
            if (index >= 0) setInstanceScale(index, SELECTED_SCALE);
            applyColors();
            requestRender();
        },

        dispose() {
            removeMeshes();
            geometry.dispose();
            material.dispose();
            xrayMaterial.dispose();
        },
    };
}
