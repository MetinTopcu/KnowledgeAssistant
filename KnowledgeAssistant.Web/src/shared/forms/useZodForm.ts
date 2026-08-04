import { standardSchemaResolver } from '@hookform/resolvers/standard-schema';
import { useForm, type FieldValues, type UseFormProps, type UseFormReturn } from 'react-hook-form';
import type { z } from 'zod';

/**
 * Binds React Hook Form to a Zod schema, so a form's rules live in one schema
 * rather than being split between validation attributes and a submit handler.
 *
 * Input and output are separate type parameters because they are separate
 * things: a field holds the string a user typed, while the submit handler
 * should receive the parsed value the schema produced. Collapsing them is what
 * forces a coercion back into the component.
 */
export function useZodForm<TInput extends FieldValues, TOutput extends FieldValues>(
  schema: z.ZodType<TOutput, TInput>,
  options?: Omit<UseFormProps<TInput, unknown, TOutput>, 'resolver'>,
): UseFormReturn<TInput, unknown, TOutput> {
  return useForm<TInput, unknown, TOutput>({
    ...options,
    resolver: standardSchemaResolver(schema),
  });
}
