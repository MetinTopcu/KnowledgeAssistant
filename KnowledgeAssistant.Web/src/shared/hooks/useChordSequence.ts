import { useEffect, useRef } from 'react';

import { isEditableTarget } from '@/shared/lib/isEditableTarget';

/** How long the leader key stays armed before the chord lapses. */
const DEFAULT_TIMEOUT_MS = 1200;

/**
 * Binds a two-key chord: a leader key, then one of several follow-up keys.
 *
 * This is the GitHub model — `g` then `d` — and it is used here because it is
 * already muscle memory for the people this product is for. Matching their
 * existing bindings matters more than internal consistency.
 *
 * The chord disarms on any modifier, any unrecognised key, and after a timeout,
 * so a stray `g` never leaves the window silently waiting to swallow the next
 * keystroke.
 */
export function useChordSequence(
  leader: string,
  bindings: Record<string, () => void>,
  timeoutMs: number = DEFAULT_TIMEOUT_MS,
): void {
  const bindingsRef = useRef(bindings);

  useEffect(() => {
    bindingsRef.current = bindings;
  });

  useEffect(() => {
    let armed = false;
    let timer: number | undefined;

    const disarm = () => {
      armed = false;
      if (timer !== undefined) {
        window.clearTimeout(timer);
        timer = undefined;
      }
    };

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.metaKey || event.ctrlKey || event.altKey || isEditableTarget(event.target)) {
        disarm();
        return;
      }

      const key = event.key.toLowerCase();

      if (armed) {
        const handler = bindingsRef.current[key];
        disarm();

        if (handler) {
          event.preventDefault();
          handler();
        }

        return;
      }

      if (key === leader.toLowerCase()) {
        armed = true;
        timer = window.setTimeout(disarm, timeoutMs);
      }
    };

    window.addEventListener('keydown', onKeyDown);

    return () => {
      window.removeEventListener('keydown', onKeyDown);
      disarm();
    };
  }, [leader, timeoutMs]);
}
