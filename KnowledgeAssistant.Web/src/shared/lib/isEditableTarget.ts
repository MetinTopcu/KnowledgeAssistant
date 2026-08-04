const EDITABLE_TAGS = new Set(['INPUT', 'TEXTAREA', 'SELECT']);

/**
 * True when a keyboard event came from somewhere the user is typing.
 *
 * Unmodified shortcuts have to defer to text entry: `g` is a navigation chord
 * everywhere except inside the composer, where it is the letter g. Getting this
 * wrong does not produce a subtle bug — it makes the question box unusable.
 */
export function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  return target.isContentEditable || EDITABLE_TAGS.has(target.tagName);
}
