// ARES-Editor page helpers: the Ctrl+S shortcut and copying text.

let saveHandler = null;

function onKeyDown(event) {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        // Commit the input being edited first: Blazor applies a value on its change event, which blur raises.
        if (document.activeElement instanceof HTMLElement) {
            document.activeElement.blur();
        }
        saveHandler?.invokeMethodAsync('OnSaveShortcut');
    }
}

/**
 * Calls OnSaveShortcut() on the DotNetObjectReference when Ctrl+S (Cmd+S) is pressed. One handler at a time.
 * @param {{invokeMethodAsync(method: string): Promise<any>}} dotNetRef
 */
export function registerSaveShortcut(dotNetRef) {
    saveHandler = dotNetRef;
    document.removeEventListener('keydown', onKeyDown);
    document.addEventListener('keydown', onKeyDown);
}

/** Removes the Ctrl+S handler. */
export function unregisterSaveShortcut() {
    saveHandler = null;
    document.removeEventListener('keydown', onKeyDown);
}

/**
 * Copies text to the clipboard.
 * @param {string} text
 * @returns {Promise<boolean>} whether it was copied.
 */
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        // Older WebKit: copy from a temporary selection.
        const area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();
        const copied = document.execCommand('copy');
        area.remove();
        return copied;
    }
}
