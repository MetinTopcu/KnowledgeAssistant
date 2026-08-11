import { Alert } from '@/shared/components/Alert';

/**
 * Shown when `searchCount` is zero.
 *
 * This is the most important state in the product. Unlike retrieval — where an
 * empty result forces an honest "I found nothing" — an agent that never
 * searched still produces a confident, well-written answer, from its training
 * data, about documents it did not open. `searchCount` is the only field in the
 * response that distinguishes that case, which is why it is reported rather
 * than merely logged, and why this sits *above* the answer instead of below it.
 *
 * It is a warning, not an error: the agent is permitted to decline a question
 * that is not about the corpus at all, and treating a correct refusal as a
 * failure would be its own kind of lie.
 */
export function UngroundedBanner() {
  return (
    <Alert variant="warning" title="The agent answered without searching the corpus">
      This answer comes from the model&apos;s training data, not from your documents. It has no
      citations and cannot be verified against a source.
    </Alert>
  );
}
