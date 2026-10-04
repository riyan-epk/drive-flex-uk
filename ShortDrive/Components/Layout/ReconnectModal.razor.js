// Set up event handlers
const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", retry);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", resume);

// Switching tabs or apps on a phone drops the live connection, and it is normally back within a
// second of returning. Showing "Rejoining the server..." for that is just noise, so the dialog is
// held back: it only appears if the connection is still down a few seconds after the visitor is
// actually looking at the page.
const SHOW_DELAY_MS = 4000;
let wantModal = false;
let showTimer = 0;

function openModal() {
    if (!reconnectModal.open) {
        reconnectModal.showModal();
    }
}

function closeModal() {
    wantModal = false;
    clearTimeout(showTimer);
    if (reconnectModal.open) {
        reconnectModal.close();
    }
}

function scheduleModal() {
    clearTimeout(showTimer);
    if (document.visibilityState !== "visible") {
        return; // re-armed by the visibilitychange listener below when the visitor comes back
    }
    showTimer = setTimeout(() => {
        if (wantModal && document.visibilityState === "visible") {
            openModal();
        }
    }, SHOW_DELAY_MS);
}

document.addEventListener("visibilitychange", () => {
    if (wantModal && document.visibilityState === "visible") {
        scheduleModal();
    }
});

function handleReconnectStateChanged(event) {
    if (event.detail.state === "show") {
        wantModal = true;
        scheduleModal();
    } else if (event.detail.state === "hide") {
        closeModal();
    } else if (event.detail.state === "failed") {
        // Automatic retries are used up. Try once more straight away if the visitor is looking at
        // the page (otherwise as soon as they return); the dialog only appears if that fails too.
        if (document.visibilityState === "visible") {
            retry();
        } else {
            document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
        }
    } else if (event.detail.state === "rejected") {
        location.reload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        // Reconnect will asynchronously return:
        // - true to mean success
        // - false to mean we reached the server, but it rejected the connection (e.g., unknown circuit ID)
        // - exception to mean we didn't reach the server (this can be sync or async)
        const successful = await Blazor.reconnect();
        if (!successful) {
            // We have been able to reach the server, but the circuit is no longer available.
            // We'll reload the page so the user can continue using the app as quickly as possible.
            // (Blazor.resumeCircuit only exists from .NET 10; calling it blindly on .NET 9 threw
            // and left the visitor stuck on "Failed to rejoin".)
            const resumeSuccessful = typeof Blazor.resumeCircuit === "function"
                ? await Blazor.resumeCircuit()
                : false;
            if (!resumeSuccessful) {
                location.reload();
            } else {
                closeModal();
            }
        }
    } catch (err) {
        // We got an exception, server is currently unavailable: show the dialog (with its Retry
        // button) and try again when the visitor next returns to the page.
        openModal();
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        const successful = typeof Blazor.resumeCircuit === "function"
            ? await Blazor.resumeCircuit()
            : false;
        if (!successful) {
            location.reload();
        }
    } catch {
        reconnectModal.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}
