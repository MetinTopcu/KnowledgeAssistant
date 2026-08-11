import { createElement, type ComponentType } from 'react';
import { Navigate, createBrowserRouter } from 'react-router';

import { AppShell } from '@/app/layout/AppShell';
import { NotFoundRoute } from '@/app/routes/NotFoundRoute';
import { RouteErrorBoundary } from '@/app/routes/RouteErrorBoundary';
import { routePaths, type RouteHandle } from '@/app/router/routePaths';
import { AgentRoute } from '@/features/agent/components/AgentRoute';
import { AskRoute } from '@/features/ask/components/AskRoute';
import { DocumentsRoute } from '@/features/documents/components/DocumentsRoute';
import { AccessibilitySection } from '@/features/settings/components/AccessibilitySection';
import { AppearanceSection } from '@/features/settings/components/AppearanceSection';
import { EnvironmentSection } from '@/features/settings/components/EnvironmentSection';
import { KeyboardSection } from '@/features/settings/components/KeyboardSection';
import { SettingsRoute } from '@/features/settings/components/SettingsRoute';
import {
  DEFAULT_SETTINGS_SECTION,
  settingsSections,
  type SettingsSectionSlug,
} from '@/features/settings/model/settingsSections';
import { StatusRoute } from '@/features/status/components/StatusRoute';

const askHandle: RouteHandle = { title: 'Ask' };
const agentHandle: RouteHandle = { title: 'Agent' };
const documentsHandle: RouteHandle = { title: 'Documents' };
const statusHandle: RouteHandle = { title: 'Health' };
const settingsHandle: RouteHandle = { title: 'Settings' };
const notFoundHandle: RouteHandle = { title: 'Not found' };

/**
 * A screen per settings section, keyed by slug.
 *
 * A `Record` over the slug union rather than a list: adding a section without a
 * screen, or a screen without a section, is then a type error rather than a
 * link into a 404.
 */
const settingsSectionScreens: Record<SettingsSectionSlug, ComponentType> = {
  appearance: AppearanceSection,
  keyboard: KeyboardSection,
  accessibility: AccessibilitySection,
  environment: EnvironmentSection,
};

/**
 * The route tree.
 *
 * It currently resolves the shell, Ask, Agent, Documents, Health, Settings and
 * its sections, the error boundary, and the catch-all. The remaining
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
        index: true,
        element: createElement(AskRoute),
        handle: askHandle,
      },
      {
        path: routePaths.agent,
        element: createElement(AgentRoute),
        handle: agentHandle,
      },
      {
        path: routePaths.documents,
        element: createElement(DocumentsRoute),
        handle: documentsHandle,
      },
      {
        path: routePaths.status,
        element: createElement(StatusRoute),
        handle: statusHandle,
      },
      {
        path: routePaths.settings,
        element: createElement(SettingsRoute),
        handle: settingsHandle,
        // Sections are routes rather than local state so that one can be
        // linked to and returned to. `/settings` itself is not a screen — it
        // redirects, and replaces the history entry so the back button leaves
        // Settings instead of bouncing off the redirect.
        children: [
          {
            index: true,
            element: createElement(Navigate, { to: DEFAULT_SETTINGS_SECTION, replace: true }),
          },
          ...settingsSections.map((section) => ({
            path: section.slug,
            element: createElement(settingsSectionScreens[section.slug]),
          })),
        ],
      },
      {
        path: '*',
        element: createElement(NotFoundRoute),
        handle: notFoundHandle,
      },
    ],
  },
]);
