import { Bot } from 'lucide-react';
import { type FormEvent } from 'react';

import {
  DEFAULT_MAX_SOURCES,
  MAX_MAX_SOURCES,
  MAX_QUESTION_LENGTH,
  MIN_MAX_SOURCES,
  QUESTION_LENGTH_WARNING,
  askAgentFormSchema,
  type AskAgentFormValues,
} from '@/features/agent/model/askAgentForm';
import { Button } from '@/shared/components/Button';
import { NumberStepper } from '@/shared/components/NumberStepper';
import { useZodForm } from '@/shared/forms/useZodForm';
import { useKeyboardShortcut } from '@/shared/hooks/useKeyboardShortcut';
import { cn } from '@/shared/lib/cn';

interface AgentComposerProps {
  readonly onSubmit: (values: AskAgentFormValues) => void;
  readonly isRunning: boolean;
}

/**
 * The agent composer.
 *
 * Structurally the same as retrieval's — composer at the top, one question, one
 * record, no thread — because the endpoint retains no conversation here either.
 * What differs is the parameter it exposes: `maxSources` bounds a *single*
 * search, and the agent decides how many searches to run, so the number is a
 * cap per lookup rather than a total.
 */
export function AgentComposer({ onSubmit, isRunning }: AgentComposerProps) {
  const form = useZodForm(askAgentFormSchema, {
    defaultValues: { question: '', maxSources: DEFAULT_MAX_SOURCES },
    mode: 'onSubmit',
  });

  const question = form.watch('question');
  const maxSources = form.watch('maxSources');
  const length = question.length;

  const submit = form.handleSubmit((values) => {
    onSubmit(values);
  });

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
        autoFocus
        rows={3}
        placeholder="Ask something that may need more than one search…"
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
            label="maxSources"
            value={maxSources}
            min={MIN_MAX_SOURCES}
            max={MAX_MAX_SOURCES}
            disabled={isRunning}
            onChange={(next) => {
              form.setValue('maxSources', next);
            }}
          />
          <span className="text-caption text-fg-subtle">passages per search</span>
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
            <Bot size={16} strokeWidth={1.5} aria-hidden />
            <span>{isRunning ? 'Running…' : 'Run agent'}</span>
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
