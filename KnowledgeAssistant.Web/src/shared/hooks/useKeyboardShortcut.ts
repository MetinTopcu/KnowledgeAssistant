import { useEffect, useRef } from 'react';

import { isEditableTarget } from '@/shared/lib/isEditableTarget';

interface ShortcutDefinition {
  /** The `KeyboardEvent.key` to match, compared case-insensitively. */
  readonly key: string;
  /** Requires Cmd on macOS and Ctrl elsewhere. */
  readonly meta?: boolean;
  readonly shift?: boolean;
}

function matchesModifiers(event: KeyboardEvent, shortcut: ShortcutDefinition): boolean {
  // Cmd on macOS, Ctrl everywhere else: matching both would make Ctrl+B fire a
  // second time on Mac, where it is already a text-navigation binding.
  const metaPressed = event.metaKey || event.ctrlKey;

  return metaPressed === (shortcut.meta ?? false) && event.shiftKey === (shortcut.shift ?? false);
}

/**
 * Binds a global keyboard shortcut for as long as the component is mounted.
 *
 * Unmodified shortcuts do not fire while the user is typing; modified ones do.
 * That asymmetry is deliberate rather than configurable: `?` must not open help
 * mid-question, while ⌘⏎ has to submit *from* the composer, which is the only
 * place it is ever pressed. Leaving it to each caller would mean getting it
 * wrong once and making a text field unusable.
 *
 * The handler is held in a ref so that a caller passing an inline arrow
 * function does not detach and reattach the listener on every render.
 */
export function useKeyboardShortcut(shortcut: ShortcutDefinition, handler: () => void): void {
  const handlerRef = useRef(handler);

  // Updated in an effect rather than during render: a ref written while
  // rendering is not a value React has committed yet, and in a concurrent
  // render that is discarded the listener would be left calling a handler that
  // closed over state the user never saw.
  useEffect(() => {
    handlerRef.current = handler;
  });

  const { key, meta, shift } = shortcut;

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() !== key.toLowerCase()) {
        return;
      }

      if (!matchesModifiers(event, { key, meta, shift })) {
        return;
      }

      if (meta !== true && isEditableTarget(event.target)) {
        return;
      }

      event.preventDefault();
      handlerRef.current();
    };

    window.addEventListener('keydown', onKeyDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
    };
  }, [key, meta, shift]);
}
