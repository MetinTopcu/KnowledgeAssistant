import { useMatches } from 'react-router';

import { type RouteHandle } from '@/app/router/routePaths';

function hasTitle(handle: unknown): handle is RouteHandle {
  return (
    typeof handle === 'object' &&
    handle !== null &&
    'title' in handle &&
    typeof (handle as RouteHandle).title === 'string'
  );
}

/**
 * The title of the deepest matched route that declares one.
 *
 * Reading the title from the route rather than having each screen render its
 * own heading keeps the top bar's height and alignment identical everywhere,
 * which is the whole reason it is part of the shell.
 */
export function useRouteTitle(fallback: string): string {
  const matches = useMatches();

  for (let index = matches.length - 1; index >= 0; index -= 1) {
    const handle = matches[index]?.handle;
    if (hasTitle(handle)) {
      return handle.title;
    }
  }

  return fallback;
}
