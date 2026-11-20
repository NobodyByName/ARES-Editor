// ARES-Editor fill groups: the groups of sockets a loot rule's group fill decides
// together (Ares.Editor.Core LootFillGroups), drawn so a rack reads as one unit.
//
// Used by scene.js (setFillGroups, setFillGroup); it owns three objects in the
// scene and is told the containers' positions (three.js space, metres) by index.
//   - The selected rule's groups: one thin outline box around each group's
//     members, all groups in one LineSegments buffer, rebuilt only when sent.
//   - The selected container's group: a ring on each other member (like the
//     selection ring, in the group colour) and spokes from the group's centre to
//     every member.
// Everything here is drawn on top (no depth test), like the selection ring, so a
// group stays readable through the rack's own mesh and walls whatever the X-ray
// setting. Nothing is pickable. Rendering stays on demand: changes only call
// requestRender.

import * as THREE from 'three';

const GROUP_COLOR = 0x5ad1ff;
const OUTLINE_OPACITY = 0.55;
const SPOKE_OPACITY = 0.9;
const SIBLING_RING_PX = 22;      // CSS px; the selection ring is 30
const PAD = 0.3;                 // metres around a group's members in its outline box
const ORDER = 9;                 // after the X-ray containers, before the selection ring (10)

/**
 * Creates the fill group layer.
 * @param {{scene: THREE.Scene, ringTexture: THREE.Texture, requestRender: () => void}} options
 *   ringTexture: the selection ring's texture (owned by the caller).
 */
export function createFillGroups({ scene, ringTexture, requestRender }) {
    return new FillGroups(scene, ringTexture, requestRender);
}

class FillGroups {
    constructor(scene, ringTexture, requestRender) {
        this.scene = scene;
        this.requestRender = requestRender;
        this.outlines = lines(OUTLINE_OPACITY);
        this.spokes = lines(SPOKE_OPACITY);
        this.rings = new THREE.Points(new THREE.BufferGeometry(), new THREE.PointsMaterial({
            map: ringTexture, color: GROUP_COLOR, size: SIBLING_RING_PX, sizeAttenuation: false,
            transparent: true, depthTest: false, depthWrite: false, fog: false,
        }));
        this.rings.renderOrder = ORDER;
        this.rings.frustumCulled = false;
        this.rings.visible = false;
        scene.add(this.outlines, this.spokes, this.rings);
    }

    /**
     * Replaces the outlined groups (the selected rule's).
     * @param {number[][]} groups container indices of each group; empty = none.
     * @param {Float32Array} pos container positions, flat [x, y, z, ...] in three.js space.
     * @param {number} count containers in pos.
     */
    setGroups(groups, pos, count) {
        const points = [];
        const min = new THREE.Vector3(), max = new THREE.Vector3(), p = new THREE.Vector3();
        for (const members of groups ?? []) {
            let any = false;
            min.setScalar(Infinity);
            max.setScalar(-Infinity);
            for (const i of members) {
                if (!(i >= 0 && i < count)) continue;
                p.fromArray(pos, i * 3);
                min.min(p);
                max.max(p);
                any = true;
            }
            if (any) boxEdges(points, min.subScalar(PAD), max.addScalar(PAD));
        }
        setLines(this.outlines, points);
        this.requestRender();
    }

    /**
     * Shows the selected container's group: rings on the other members and spokes
     * from the group's centre to every member.
     * @param {number[]} members container indices of the group; empty = none.
     * @param {number} selected the selected container (gets no group ring).
     * @param {Float32Array} pos container positions, as in setGroups.
     * @param {number} count containers in pos.
     */
    setSelectedGroup(members, selected, pos, count) {
        const valid = (members ?? []).filter((i) => i >= 0 && i < count);
        const center = new THREE.Vector3();
        for (const i of valid) center.x += pos[i * 3], center.y += pos[i * 3 + 1], center.z += pos[i * 3 + 2];
        if (valid.length > 0) center.divideScalar(valid.length);
        const spokes = [];
        const rings = [];
        for (const i of valid) {
            spokes.push(center.x, center.y, center.z, pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
            if (i !== selected) rings.push(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
        }
        setLines(this.spokes, valid.length > 1 ? spokes : []);
        this.rings.geometry.dispose();
        this.rings.geometry = new THREE.BufferGeometry();
        this.rings.geometry.setAttribute('position', new THREE.Float32BufferAttribute(rings, 3));
        this.rings.visible = rings.length > 0;
        this.requestRender();
    }

    /** Removes everything (the container indices it was given are no longer valid). */
    clear() {
        setLines(this.outlines, []);
        setLines(this.spokes, []);
        this.rings.visible = false;
        this.requestRender();
    }

    dispose() {
        for (const object of [this.outlines, this.spokes, this.rings]) {
            this.scene.remove(object);
            object.geometry.dispose();
            object.material.dispose();      // the ring texture belongs to scene.js
        }
    }
}

function lines(opacity) {
    const object = new THREE.LineSegments(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({
        color: GROUP_COLOR, transparent: true, opacity, depthTest: false, depthWrite: false, fog: false,
    }));
    object.renderOrder = ORDER;
    object.frustumCulled = false;
    object.visible = false;
    return object;
}

function setLines(object, points) {
    object.geometry.dispose();
    object.geometry = new THREE.BufferGeometry();
    object.geometry.setAttribute('position', new THREE.Float32BufferAttribute(points, 3));
    object.visible = points.length > 0;
}

// The 12 edges of an axis-aligned box, as pairs of points.
function boxEdges(points, min, max) {
    const x = [min.x, max.x], y = [min.y, max.y], z = [min.z, max.z];
    for (let a = 0; a < 2; a++) {
        for (let b = 0; b < 2; b++) {
            points.push(x[0], y[a], z[b], x[1], y[a], z[b]);
            points.push(x[a], y[0], z[b], x[a], y[1], z[b]);
            points.push(x[a], y[b], z[0], x[a], y[b], z[1]);
        }
    }
}
