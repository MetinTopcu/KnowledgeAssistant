export interface ShortcutDocumentation {
  /** Rendered in order; a chord is two entries, not one string. */
  readonly keys: readonly string[];
  readonly description: string;
}

export interface ShortcutGroup {
  readonly name: string;
  readonly shortcuts: readonly ShortcutDocumentation[];
}

/**
 * The keyboard reference shown by `?`.
 *
 * It lists only bindings that are actually wired up. Documenting the full model
 * from docs/DESIGN.md §14 now would be quicker and would also be a lie: a
 * reference that names a key which does nothing teaches the user that the
 * reference cannot be trusted, and they stop opening it. Each slice adds its
 * own bindings here as it binds them.
 */
export const shortcutGroups: readonly ShortcutGroup[] = [
  {
    name: 'Navigation',
    shortcuts: [
      { keys: ['g', 'a'], description: 'Go to Ask' },
      { keys: ['g', 'r'], description: 'Go to Runs' },
      { keys: ['g', 'd'], description: 'Go to Documents' },
      { keys: ['g', 'h'], description: 'Go to Health' },
      { keys: ['g', 's'], description: 'Go to Settings' },
    ],
  },
  {
    name: 'View',
    shortcuts: [
      { keys: ['Ctrl', 'B'], description: 'Collapse or expand the navigation rail' },
      { keys: ['Ctrl', 'J'], description: 'Cycle the theme: system, light, dark' },
    ],
  },
  {
    name: 'Help',
    shortcuts: [
      { keys: ['?'], description: 'Show this reference' },
      { keys: ['Esc'], description: 'Close the open dialog' },
    ],
  },
];
