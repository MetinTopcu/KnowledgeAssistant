import { createElement } from 'react';
import { createBrowserRouter } from 'react-router';

import { AppShell } from '@/app/layout/AppShell';
import { NotFoundRoute } from '@/app/routes/NotFoundRoute';
import { RouteErrorBoundary } from '@/app/routes/RouteErrorBoundary';
import { type RouteHandle } from '@/app/router/routePaths';

const notFoundHandle: RouteHandle = { title: 'Not found' };

/**
 * The route tree.
 *
 * It currently resolves the shell, the error boundary, and the catch-all. The
 * screens named in docs/DESIGN.md §6 are added by the slices that build them —
 * registering a route now that rendered a stub would put a page in the product
 * that looks finished and does nothing, which is the thing the slice plan is
 * arranged to avoid. Until Ask lands, `/` genuinely is not a route, and the
 * catch-all reporting that is the accurate answer.
 */
export const appRouter = createBrowserRouter([
  {
    element: createElement(AppShell),
    errorElement: createElement(RouteErrorBoundary),
    children: [
      {
        path: '*',
        element: createElement(NotFoundRoute),
        handle: notFoundHandle,
      },
    ],
  },
]);
