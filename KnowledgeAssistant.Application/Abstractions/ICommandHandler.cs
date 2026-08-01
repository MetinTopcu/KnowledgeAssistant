using KnowledgeAssistant.Domain.Common;
using MediatR;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>
/// Handles a <see cref="ICommand{TResponse}"/>.
/// </summary>
/// <typeparam name="TCommand">The command being handled.</typeparam>
/// <typeparam name="TResponse">The value returned when the command succeeds.</typeparam>
/// <remarks>
/// The <c>where TCommand : ICommand&lt;TResponse&gt;</c> constraint is what makes
/// the pairing compile-time safe: a handler cannot be wired to a command whose
/// response type differs, which is otherwise a mistake that surfaces only as a
/// runtime resolution failure on the first request.
/// </remarks>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;
