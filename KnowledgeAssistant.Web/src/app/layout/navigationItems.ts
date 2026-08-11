import { Activity, Bot, FileText, History, MessageSquareText, Settings } from 'lucide-react';
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
 * The primary destinations.
 *
 * Agent is listed beside Ask rather than folded into it as a mode, because the
 * two endpoints make different promises: retrieval performs exactly one search
 * and costs a predictable amount, while the agent decides how many times to
 * look. Choosing between them is a decision about cost, and a decision about
 * cost deserves to be visible rather than buried in a control on one screen.
 *
 * That makes six entries, past the point where this list should keep growing.
 * Runs is the one to reconsider first: it has no screen and no endpoint behind
 * it, and run history is a client-local idea until the API can persist one.
 */
export const navigationItems: readonly NavigationItem[] = [
  { label: 'Ask', path: routePaths.ask, icon: MessageSquareText, end: true },
  { label: 'Agent', path: routePaths.agent, icon: Bot },
  { label: 'Runs', path: routePaths.runs, icon: History },
  { label: 'Documents', path: routePaths.documents, icon: FileText },
  { label: 'Health', path: routePaths.status, icon: Activity },
  { label: 'Settings', path: routePaths.settings, icon: Settings },
];
