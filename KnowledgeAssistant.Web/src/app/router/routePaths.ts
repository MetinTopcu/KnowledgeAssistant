/**
 * Every route the client owns, named once.
 *
 * `status` rather than `/health`: the API serves its own probes at `/health`,
 * and in a co-hosted deploy — or behind the dev proxy — a client route on that
 * path would shadow them. Choosing a different path here costs a word and
 * removes a class of "works locally, 404s in Azure" bug.
 */
export const routePaths = {
  ask: '/',
  runs: '/runs',
  documents: '/documents',
  status: '/status',
  settings: '/settings',
} as const;

/** Per-route metadata read back out of the matched route by the top bar. */
export interface RouteHandle {
  readonly title: string;
}
