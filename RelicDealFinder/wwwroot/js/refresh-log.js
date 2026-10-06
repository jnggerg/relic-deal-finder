// Keeps a scroll container pinned to the bottom as lines are added,
// unless the user has scrolled up to read older lines.
// Used in /refresh log viewer
const PIN_THRESHOLD_PX = 8;

export function attach(el) {
    if (!el || el._autoScroll) return;

    let pinned = true;
    const onScroll = () => {
        pinned = el.scrollTop + el.clientHeight >= el.scrollHeight - PIN_THRESHOLD_PX;
    };
    const observer = new MutationObserver(() => {
        if (pinned) el.scrollTop = el.scrollHeight;
    });

    el.addEventListener("scroll", onScroll, { passive: true });
    observer.observe(el, { childList: true, subtree: true, characterData: true });
    el.scrollTop = el.scrollHeight;
    el._autoScroll = { observer, onScroll };
}

export function detach(el) {
    if (!el?._autoScroll) return;
    el._autoScroll.observer.disconnect();
    el.removeEventListener("scroll", el._autoScroll.onScroll);
    delete el._autoScroll;
}
