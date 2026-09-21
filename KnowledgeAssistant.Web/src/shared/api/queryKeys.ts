/**
 * Query keys that more than one feature touches.
 *
 * A key is a contract between whoever reads a resource and whoever invalidates
 * it, so it cannot live inside either feature: uploading a document must
 * refresh the corpus listing, and `features/document-upload` importing from
 * `features/documents` to say so would be exactly the cross-feature dependency
 * the folder layout exists to prevent. The read side moves here instead — the
 * same move `shared/theme` and `shared/lib/keyboardShortcuts` made.
 *
 * Keys used by one feature alone stay in that feature.
 */

/** The corpus listing. Read by Documents, invalidated by an ingested upload. */
export const documentsQueryKey = ['documents'] as const;
