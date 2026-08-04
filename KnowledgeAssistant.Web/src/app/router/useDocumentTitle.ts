import { useEffect } from 'react';

const PRODUCT_NAME = 'KnowledgeAssistant';

/**
 * Keeps the browser tab in step with the current screen.
 *
 * Screen first, product second: a user with several tabs open is choosing
 * between screens, and a title that leads with the product name makes every tab
 * look identical until it is truncated to uselessness.
 */
export function useDocumentTitle(title: string): void {
  useEffect(() => {
    document.title = title === PRODUCT_NAME ? PRODUCT_NAME : `${title} · ${PRODUCT_NAME}`;
  }, [title]);
}
