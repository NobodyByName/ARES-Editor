// ARES-Editor side panel splitters: dragging a handle between a side panel and the map view resizes the panel, and a
// double-click restores its default width. Widths are custom properties on the editor grid, so a drag never waits for
// Blazor; .NET hears about the new widths once, when the drag ends.

const MIN_PANEL_PX = 200;
const MAX_PANEL_FRACTION = 0.5;
const MIN_VIEW_PX = 320;

let state = null;

const clamp = (value, min, max) => Math.min(Math.max(value, min), max);
const otherSide = (side) => (side === 'left' ? 'right' : 'left');

function defaultWidth(side) {
    const value = parseFloat(getComputedStyle(state.grid).getPropertyValue(`--${side}-default`));
    return Number.isFinite(value) ? value : MIN_PANEL_PX;
}

function maxPanelWidth(total) {
    return Math.max(MIN_PANEL_PX, Math.floor(total * MAX_PANEL_FRACTION));
}

// Effective widths: each preferred (or default) width within its bounds; when both panels together leave the view less
// than its minimum, they give up the difference in proportion to how far each is above the panel minimum.
function apply() {
    const total = state.grid.clientWidth;
    if (total <= 0) {
        return;
    }

    const max = maxPanelWidth(total);
    let left = Math.round(clamp(state.preferred.left ?? defaultWidth('left'), MIN_PANEL_PX, max));
    let right = Math.round(clamp(state.preferred.right ?? defaultWidth('right'), MIN_PANEL_PX, max));
    const excess = left + right + MIN_VIEW_PX - total;
    const slack = left + right - 2 * MIN_PANEL_PX;
    if (excess > 0 && slack > 0) {
        const cut = Math.min(excess, slack);
        const leftCut = Math.round(cut * (left - MIN_PANEL_PX) / slack);
        left -= leftCut;
        right = Math.max(MIN_PANEL_PX, right - (cut - leftCut));
    }

    state.effective = { left, right };
    state.grid.style.setProperty('--left-width', `${left}px`);
    state.grid.style.setProperty('--right-width', `${right}px`);
}

function persist() {
    state.dotNetRef.invokeMethodAsync('OnPanelWidthsChanged', state.preferred.left, state.preferred.right)
        .catch(() => { /* The window is closing. */ });
}

function finishDrag(drag) {
    state.drag = null;
    drag.handle.classList.remove('dragging');
    state.grid.classList.remove('resizing');
    if (drag.handle.hasPointerCapture(drag.pointerId)) {
        drag.handle.releasePointerCapture(drag.pointerId);
    }

    if (drag.moved) {
        persist();
    }
}

function endDrag(event) {
    const drag = state?.drag;
    if (drag && event.pointerId === drag.pointerId) {
        finishDrag(drag);
    }
}

function onPointerDown(event) {
    const handle = event.target.closest?.('.splitter');
    if (!handle || event.button !== 0 || !state.grid.contains(handle)) {
        return;
    }

    // A drag whose release never arrived ends here. Capturing the pointer keeps every move and the release on the handle,
    // away from the view's camera and picking.
    if (state.drag) {
        finishDrag(state.drag);
    }

    event.preventDefault();
    handle.setPointerCapture(event.pointerId);
    const side = handle.dataset.side;
    state.drag = { handle, side, pointerId: event.pointerId, startX: event.clientX, startWidth: state.effective[side], moved: false };
    handle.classList.add('dragging');
    state.grid.classList.add('resizing');
}

function onPointerMove(event) {
    const drag = state.drag;
    if (!drag || event.pointerId !== drag.pointerId) {
        return;
    }

    const dx = event.clientX - drag.startX;
    const total = state.grid.clientWidth;
    // The other panel keeps its width; the dragged one stops where the view would get narrower than its minimum.
    const max = Math.min(maxPanelWidth(total), total - state.effective[otherSide(drag.side)] - MIN_VIEW_PX);
    const width = Math.round(clamp(drag.startWidth + (drag.side === 'left' ? dx : -dx), MIN_PANEL_PX, Math.max(MIN_PANEL_PX, max)));
    if (width !== state.effective[drag.side]) {
        state.preferred[drag.side] = width;
        drag.moved = true;
        apply();
    }
}

function onDoubleClick(event) {
    const handle = event.target.closest?.('.splitter');
    if (!handle || !state.grid.contains(handle)) {
        return;
    }

    state.preferred[handle.dataset.side] = null;
    apply();
    persist();
}

/**
 * Makes the splitters of the editor grid draggable. The grid's --left-default and --right-default custom properties
 * give the default widths; the widths in effect are set on it as --left-width and --right-width, and again whenever the
 * grid's size changes. Splitters are the grid's .splitter children with data-side="left" or "right", and may come and go.
 * One grid at a time.
 * @param {HTMLElement} grid
 * @param {{invokeMethodAsync(method: string, ...args: any[]): Promise<any>}} dotNetRef  Gets
 *     OnPanelWidthsChanged(left, right) after a drag or a reset, with null for a default width.
 * @param {number|null} left  The remembered left panel width in pixels, or null for the default.
 * @param {number|null} right  The remembered right panel width in pixels, or null for the default.
 */
export function initSplitters(grid, dotNetRef, left, right) {
    disposeSplitters();
    state = {
        grid,
        dotNetRef,
        preferred: { left: left ?? null, right: right ?? null },
        effective: { left: MIN_PANEL_PX, right: MIN_PANEL_PX },
        drag: null,
        observer: new ResizeObserver(() => apply()),
        listeners: [
            ['pointerdown', onPointerDown],
            ['pointermove', onPointerMove],
            ['pointerup', endDrag],
            ['pointercancel', endDrag],
            ['lostpointercapture', endDrag],
            ['dblclick', onDoubleClick],
        ],
    };
    for (const [type, fn] of state.listeners) {
        grid.addEventListener(type, fn);
    }

    state.observer.observe(grid);
    apply();
}

/** Stops handling the splitters. */
export function disposeSplitters() {
    if (!state) {
        return;
    }

    for (const [type, fn] of state.listeners) {
        state.grid.removeEventListener(type, fn);
    }

    state.observer.disconnect();
    state.grid.classList.remove('resizing');
    state = null;
}
