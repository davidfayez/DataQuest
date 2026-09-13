import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

/** How long to keep correcting for sections that are still filling in. */
const SETTLE_MS = 2_000;

/**
 * Scrolls to the section a URL's hash names, including one that is not in the page yet.
 *
 * Two things get in the way, and both come from the same place: nearly every section of the
 * landing page is admin-managed, so it renders only once its content has been fetched, and several
 * remove themselves entirely when nothing is published.
 *
 * The first is that the browser honours a hash exactly once, the moment it parses the document. By
 * the time "#coverage" exists there is no longer anything trying to reach it, and the header's own
 * links leave the reader at the top of the page.
 *
 * The second only shows up once the first is fixed: landing on the right section is not enough,
 * because the sections *above* it are still arriving, and each one pushes the target further down
 * after the jump. So the position is held — re-corrected as the page grows — until the page stops
 * changing, and abandoned the moment the reader does anything themselves.
 *
 * "Anything" includes a click, not just a scroll. The header's own mark scrolls the page back to
 * the top, and while this was watching only for wheel, touch and key events it dragged the reader
 * straight back down to the section they had just left.
 */
export function useHashScroll(): void {
  const { hash } = useLocation();

  useEffect(() => {
    const id = decodeURIComponent(hash.replace(/^#/, ''));
    if (!id) return undefined;

    const smooth = !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    let arrived = false;
    let stopped = false;

    function scrollToTarget(): void {
      if (stopped) return;

      const target = document.getElementById(id);
      if (!target) return;

      // Smooth for the first jump, instant for the corrections after it: re-animating every time
      // a section above finishes loading reads as the page sliding around on its own.
      target.scrollIntoView({ behavior: arrived || !smooth ? 'auto' : 'smooth', block: 'start' });
      arrived = true;
    }

    function stop() {
      stopped = true;
      observer.disconnect();
      window.clearTimeout(timer);

      for (const event of ['wheel', 'touchstart', 'keydown', 'pointerdown'] as const) {
        window.removeEventListener(event, stop);
      }
    }

    const observer = new MutationObserver(scrollToTarget);
    const timer = window.setTimeout(stop, SETTLE_MS);

    // Passive: these only ever stop the correction, so they must not delay the reader's own scroll.
    for (const event of ['wheel', 'touchstart', 'keydown'] as const) {
      window.addEventListener(event, stop, { passive: true, once: true });
    }

    scrollToTarget();
    observer.observe(document.body, { childList: true, subtree: true });

    return stop;
  }, [hash]);
}
