import { useMemo } from 'react';
import { useNavigate } from 'react-router';

import { routePaths } from '@/app/router/routePaths';
import { useChordSequence } from '@/shared/hooks/useChordSequence';

/** The leader key, following the convention these users already have from GitHub. */
const LEADER = 'g';

/**
 * Binds `g` followed by a destination key.
 *
 * The destinations match the rail, so the two navigation surfaces cannot drift
 * apart — a chord that goes somewhere the rail does not is a shortcut nobody
 * can discover.
 */
export function useNavigationChords(): void {
  const navigate = useNavigate();

  const bindings = useMemo(
    () => ({
      a: () => void navigate(routePaths.ask),
      g: () => void navigate(routePaths.agent),
      d: () => void navigate(routePaths.documents),
      h: () => void navigate(routePaths.status),
      s: () => void navigate(routePaths.settings),
    }),
    [navigate],
  );

  useChordSequence(LEADER, bindings);
}
