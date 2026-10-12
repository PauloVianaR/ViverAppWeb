let observingFatal = false;
let fatalTimer;
let lastToastId;

export function sync(visible, newestToastId) {
    if (!observingFatal) {
        const fatal = document.getElementById("blazor-error-ui");
        if (fatal) {
            observingFatal = true;
            new MutationObserver(() => {
                clearTimeout(fatalTimer);
                if (fatal.style.display === "block")
                    fatalTimer = setTimeout(() => { fatal.style.display = "none"; }, 10000);
            }).observe(fatal, { attributes: true, attributeFilter: ["style"] });
        }
    }
    const host = document.getElementById("ui-toast-host");
    if (!host) return;
    if (visible) {
        // A camada superior mantém o aviso visível inclusive sobre formulários dialog.
        if (newestToastId !== lastToastId && host.matches(":popover-open")) host.hidePopover();
        if (!host.matches(":popover-open")) host.showPopover();
    } else if (host.matches(":popover-open")) host.hidePopover();
    lastToastId = newestToastId;
}
