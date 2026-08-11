import { Search } from 'lucide-react';
import { type FormEvent } from 'react';

import {
  DEFAULT_TOP_K,
  MAX_QUESTION_LENGTH,
  MAX_TOP_K,
  MIN_TOP_K,
  QUESTION_LENGTH_WARNING,
  askQuestionFormSchema,
  type AskQuestionFormValues,
} from '@/features/ask/model/askQuestionForm';
import { Button } from '@/shared/components/Button';
import { NumberStepper } from '@/shared/components/NumberStepper';
import { useZodForm } from '@/shared/forms/useZodForm';
import { useKeyboardShortcut } from '@/shared/hooks/useKeyboardShortcut';
import { cn } from '@/shared/lib/cn';

interface QuestionComposerProps {
  readonly onSubmit: (values: AskQuestionFormValues) => void;
  readonly isRunning: boolean;
}

/**
 * The composer sits at the top of the screen, not the bottom.
 *
 * There is no scrollback to anchor to: the API retains no conversation, so each
 * question produces one record rather than a further turn. A bottom-anchored
 * input would promise a thread that does not exist.
 */
export function QuestionComposer({ onSubmit, isRunning }: QuestionComposerProps) {
  const form = useZodForm(askQuestionFormSchema, {
    defaultValues: { question: '', topK: DEFAULT_TOP_K },
    mode: 'onSubmit',
  });

  const question = form.watch('question');
  const topK = form.watch('topK');
  const length = question.length;

  const submit = form.handleSubmit((values) => {
    onSubmit(values);
  });

  // Meta shortcuts deliberately fire from inside text fields — this is the one
  // place ⌘⏎ is ever pressed.
  useKeyboardShortcut({ key: 'Enter', meta: true }, () => {
    if (!isRunning) {
      void submit();
    }
  });

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    void submit();
  };

  const questionError = form.formState.errors.question?.message;

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-2">
      <textarea
        {...form.register('question')}
        // The composer is the only reason this screen exists, and it is the
        // first thing in the content column — so focusing it costs a keyboard
        // user nothing and saves everyone else a click. AppShell's own
        // focus-on-navigation defers to this.
        autoFocus
        rows={3}
        placeholder="Ask a question about the indexed documents…"
        aria-label="Question"
        aria-invalid={questionError !== undefined}
        className={cn(
          'w-full resize-y rounded-sm border bg-canvas-base px-3 py-2 text-body text-fg-default placeholder:text-fg-subtle',
          'transition-colors duration-instant ease-standard',
          questionError !== undefined ? 'border-danger-fg' : 'hover:border-border-strong',
        )}
      />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-4">
          <NumberStepper
            label="topK"
            value={topK}
            min={MIN_TOP_K}
            max={MAX_TOP_K}
            disabled={isRunning}
            onChange={(next) => {
              form.setValue('topK', next);
            }}
          />
          <span className="text-caption text-fg-subtle">passages retrieved</span>
        </div>

        <div className="flex items-center gap-3">
          <span
            className={cn(
              'font-mono text-mono-ui tabular-nums',
              length > MAX_QUESTION_LENGTH
                ? 'text-danger-fg'
                : length >= QUESTION_LENGTH_WARNING
                  ? 'text-attention-fg'
                  : 'text-fg-subtle',
            )}
          >
            {length} / {MAX_QUESTION_LENGTH}
          </span>

          <Button type="submit" variant="primary" size="md" disabled={isRunning}>
            <Search size={16} strokeWidth={1.5} aria-hidden />
            <span>{isRunning ? 'Asking…' : 'Ask'}</span>
          </Button>
        </div>
      </div>

      {questionError !== undefined && (
        <p role="alert" className="text-caption text-danger-fg">
          {questionError}
        </p>
      )}
    </form>
  );
}
