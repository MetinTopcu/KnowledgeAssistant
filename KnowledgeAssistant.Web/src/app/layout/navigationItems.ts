import { Activity, FileText, History, MessageSquareText, Settings } from 'lucide-react';
import { type LucideIcon } from 'lucide-react';

import { routePaths } from '@/app/router/routePaths';

export interface NavigationItem {
  readonly label: string;
  readonly path: string;
  readonly icon: LucideIcon;
  /** True when only the exact path should mark the item active. */
  readonly end?: boolean;
}

/**
 * The four primary destinations, plus Settings.
 *
 * Deliberately short. If a sixth ever earns a place here, something else has to
 * leave — a rail that grows without limit is a menu, and a menu is what this
 * navigation model exists to avoid.
 */
export const navigationItems: readonly NavigationItem[] = [
  { label: 'Ask', path: routePaths.ask, icon: MessageSquareText, end: true },
  { label: 'Runs', path: routePaths.runs, icon: History },
  { label: 'Documents', path: routePaths.documents, icon: FileText },
  { label: 'Health', path: routePaths.status, icon: Activity },
  { label: 'Settings', path: routePaths.settings, icon: Settings },
];
