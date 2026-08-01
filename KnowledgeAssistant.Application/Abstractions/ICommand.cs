using KnowledgeAssistant.Domain.Common;
using MediatR;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>
/// A state-changing use case that produces <typeparamref name="TResponse"/> on success.
/// </summary>
/// <typeparam name="TResponse">The value returned when the command succeeds.</typeparam>
/// <remarks>
/// <para>
/// This is the inward-facing contract described in <c>Abstractions/README.md</c>:
/// the Application layer defines it and implements it. It exists rather than
/// using <see cref="IRequest{TResponse}"/> directly for three reasons.
/// </para>
/// <para>
/// First, it bakes <see cref="Result{TValue}"/> into the pipeline — a command
/// cannot accidentally be declared as returning a bare value and bypass the
/// Result pattern.
/// </para>
/// <para>
/// Second, it gives pipeline behaviours a type to target: a transaction
/// behaviour can be constrained to commands and never wrap a query.
/// </para>
/// <para>
/// Third, it is the seam that contains a future move away from MediatR. Handlers
/// reference <c>ICommand</c>; only this file references <c>IRequest</c>.
/// </para>
/// </remarks>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;
