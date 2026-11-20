// ARES-Editor loot value areas: the map's points of interest with a loot value
// (Ares.Editor.Core LootValueArea / ConvexShape), drawn as translucent volumes
// with outlined edges and an HTML label over each. scene.js makes a second layer
// of the same kind for the loot placement limiters (Ares.Editor.Core LootLimiter),
// whose labels sit under the anchor so they do not cover the area labels of the
// volumes they share.
//
// Used by scene.js (setAreas, setAreaLabels, setAreasVisible, setAreaHighlight,
// frameArea, and the limiter functions); each layer owns one THREE.Group in the
// scene and its labels among the children of the label layer element. The volumes are never picked: container picking in scene.js is
// screen-space and ignores them.
//
// Drawing: faces and edges are transparent with depth writes off, so the map and
// the containers behind stay readable; they are drawn after the opaque map and
// before the X-ray containers (renderOrder AREA_ORDER < the X-ray pass). The edges
// are drawn twice: faintly through the map (areas are often buried in terrain or
// concrete) and stronger where nothing hides them.
// Labels are placed after each rendered frame (updateLabels), so they follow the
// camera without extra frames. Labels that would overlap are culled greedily in
// screen space: higher loot value first, then nearer to the camera; a label that
// overlaps one already placed is hidden for that frame.

import * as THREE from 'three';

const UE_TO_M = 0.01;
const AREA_ORDER = 1;
const FACE_OPACITY = 0.09;
const FACE_OPACITY_HIGHLIGHT = 0.28;
const EDGE_OPACITY = 0.75;
const EDGE_OPACITY_HIGHLIGHT = 1;
const HIDDEN_EDGE_OPACITY = 0.22;      // the edges' pass through the map
const HIDDEN_EDGE_OPACITY_HIGHLIGHT = 0.75;
const EDGE_ANGLE = 2;              // degrees: edges between faces flatter than this are not outlined
const LABEL_MARGIN_PX = 80;        // labels this far outside the view are hidden
const LABEL_GAP_PX = 4;            // free space kept between two shown labels

const tmpVec = new THREE.Vector3();

/**
 * Creates an area layer.
 * @param {{scene: THREE.Scene, labelLayer: HTMLElement, requestRender: () => void,
 *          name?: string, labelsBelow?: boolean}} options
 *   labelLayer: element the labels are added to (positioned over the canvas, same size).
 *   name: the layer's group name; labelsBelow: hang the labels under their anchor
 *   instead of standing them on it.
 */
export function createLootAreas({ scene, labelLayer, requestRender, name = 'loot-areas', labelsBelow = false }) {
    return new LootAreas(scene, labelLayer, requestRender, name, labelsBelow);
}

class LootAreas {
    constructor(scene, labelLayer, requestRender, name, labelsBelow) {
        this.group = new THREE.Group();
        this.group.name = name;
        scene.add(this.group);
        this.labelLayer = labelLayer;
        this.requestRender = requestRender;
        this.labelsBelow = labelsBelow;
        this.areas = [];
        this.visible = true;
        this.highlight = 0;
        this.highlightIndex = -1;
    }

    /**
     * Replaces the areas.
     * @param {{label: string, color: string, value: number,
     *          hulls: {vertices: number[], triangles: number[]}[]}[]} areas
     *   vertices: flat [x, y, z, ...] in Unreal units (cm, Z up); triangles: index
     *   triples wound outward in Unreal's coordinates.
     */
    set(areas) {
        this.clear();
        for (const area of areas ?? []) {
            const geometry = buildGeometry(area.hulls ?? []);
            if (!geometry) continue;
            const color = new THREE.Color(area.color);
            const faces = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({
                color, transparent: true, opacity: FACE_OPACITY, depthWrite: false, side: THREE.DoubleSide,
            }));
            const edgeGeometry = new THREE.EdgesGeometry(geometry, EDGE_ANGLE);
            const edges = new THREE.LineSegments(edgeGeometry, new THREE.LineBasicMaterial({
                color, transparent: true, opacity: EDGE_OPACITY, depthWrite: false,
            }));
            const hiddenEdges = new THREE.LineSegments(edgeGeometry, new THREE.LineBasicMaterial({
                color, transparent: true, opacity: HIDDEN_EDGE_OPACITY, depthWrite: false, depthTest: false,
            }));
            faces.renderOrder = edges.renderOrder = hiddenEdges.renderOrder = AREA_ORDER;
            this.group.add(faces, hiddenEdges, edges);

            const box = geometry.boundingBox;
            const label = document.createElement('div');
            label.className = 'scene-area-label';
            label.style.cssText = 'position:absolute;left:0;top:0;white-space:nowrap;pointer-events:none;';
            label.style.setProperty('--area-color', area.color);
            label.textContent = area.label ?? '';
            label.hidden = true;
            this.labelLayer.appendChild(label);

            this.areas.push({
                value: area.value, faces, edges, hiddenEdges, label,
                anchor: new THREE.Vector3((box.min.x + box.max.x) / 2, box.max.y, (box.min.z + box.max.z) / 2),
                sphere: geometry.boundingSphere.clone(),
            });
        }
        this.applyHighlight();
        this.requestRender();
    }

    /** Replaces the labels' text, one per area in set() order. */
    setLabels(labels) {
        this.areas.forEach((area, i) => {
            area.label.textContent = labels?.[i] ?? '';
            area.size = null;
        });
    }

    /** Shows or hides every volume and label. */
    setVisible(on) {
        this.visible = !!on;
        this.group.visible = this.visible;
        if (!this.visible) for (const area of this.areas) area.label.hidden = true;
        this.requestRender();
    }

    /** Draws the areas of one loot value brighter (0 = none). */
    setHighlight(value) {
        this.highlight = value | 0;
        this.applyHighlight();
        this.requestRender();
    }

    /** Draws one area brighter (index in set() order; -1 = none). */
    setHighlightIndex(index) {
        this.highlightIndex = index ?? -1;
        this.applyHighlight();
        this.requestRender();
    }

    /** Bounding sphere of an area (three.js space), or null. */
    bounds(index) {
        return this.areas[index]?.sphere ?? null;
    }

    /** Places the labels over their areas' tops; called after each rendered frame. */
    updateLabels(camera, width, height) {
        if (!this.visible) return;
        const candidates = [];
        for (const area of this.areas) {
            tmpVec.copy(area.anchor).applyMatrix4(camera.matrixWorldInverse);
            const depth = -tmpVec.z;
            if (depth < camera.near) {          // behind the camera
                area.label.hidden = true;
                continue;
            }
            tmpVec.copy(area.anchor).project(camera);
            const x = (tmpVec.x * 0.5 + 0.5) * width;
            const y = (0.5 - tmpVec.y * 0.5) * height;
            if (x < -LABEL_MARGIN_PX || x > width + LABEL_MARGIN_PX || y < -LABEL_MARGIN_PX || y > height + LABEL_MARGIN_PX) {
                area.label.hidden = true;
                continue;
            }
            candidates.push({ area, x, y, depth });
        }

        candidates.sort((a, b) => b.area.on - a.area.on || b.area.value - a.area.value || a.depth - b.depth);
        const placed = [];
        for (const { area, x, y } of candidates) {
            const { width: w, height: h } = labelSize(area);
            const top = this.labelsBelow ? y : y - h;
            const box = { left: x - w / 2, right: x + w / 2, top, bottom: top + h };
            const overlaps = placed.some((other) => box.left < other.right + LABEL_GAP_PX && other.left < box.right + LABEL_GAP_PX
                && box.top < other.bottom + LABEL_GAP_PX && other.top < box.bottom + LABEL_GAP_PX);
            area.label.hidden = overlaps;
            if (overlaps) continue;
            placed.push(box);
            area.label.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px) translate(-50%, ${this.labelsBelow ? '0' : '-100%'})`;
        }
    }

    applyHighlight() {
        this.areas.forEach((area, index) => {
            const on = (this.highlight !== 0 && area.value === this.highlight) || index === this.highlightIndex;
            area.on = on ? 1 : 0;
            area.faces.material.opacity = on ? FACE_OPACITY_HIGHLIGHT : FACE_OPACITY;
            area.edges.material.opacity = on ? EDGE_OPACITY_HIGHLIGHT : EDGE_OPACITY;
            area.hiddenEdges.material.opacity = on ? HIDDEN_EDGE_OPACITY_HIGHLIGHT : HIDDEN_EDGE_OPACITY;
            area.label.classList.toggle('highlighted', on);
        });
    }

    clear() {
        for (const area of this.areas) {
            for (const object of [area.faces, area.edges, area.hiddenEdges]) {
                this.group.remove(object);
                object.material.dispose();
            }
            area.faces.geometry.dispose();
            area.edges.geometry.dispose();     // shared with hiddenEdges
            area.label.remove();
        }
        this.areas = [];
    }

    dispose() {
        this.clear();
        this.group.parent?.remove(this.group);
    }
}

// A label's size in CSS px, measured once while shown (setLabels clears it).
function labelSize(area) {
    if (area.size) return area.size;
    area.label.hidden = false;
    const size = { width: area.label.offsetWidth, height: area.label.offsetHeight };
    if (size.width > 0) area.size = size;     // not laid out yet (hidden view): measure again later
    return size;
}

// One indexed geometry for all hulls of an area, in three.js space (metres, Y up:
// three = (ue.x, ue.z, ue.y) * 0.01). Swapping Y and Z flips handedness, so each
// triangle's last two indices are swapped to keep it wound outward. Null when empty.
function buildGeometry(hulls) {
    const positions = [];
    const indices = [];
    for (const hull of hulls) {
        const base = positions.length / 3;
        const v = hull.vertices ?? [];
        for (let i = 0; i + 2 < v.length; i += 3) positions.push(v[i] * UE_TO_M, v[i + 2] * UE_TO_M, v[i + 1] * UE_TO_M);
        const t = hull.triangles ?? [];
        for (let i = 0; i + 2 < t.length; i += 3) indices.push(base + t[i], base + t[i + 2], base + t[i + 1]);
    }
    if (indices.length === 0) return null;
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geometry.setIndex(indices);
    geometry.computeBoundingBox();
    geometry.computeBoundingSphere();
    return geometry;
}
