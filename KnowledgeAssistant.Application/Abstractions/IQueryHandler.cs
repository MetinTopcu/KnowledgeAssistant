using KnowledgeAssistant.Domain.Common;
using MediatR;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>
/// Handles an <see cref="IQuery{TResponse}"/>.
/// </summary>
/// <typeparam name="TQuery">The query type handled.</typeparam>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
/// <remarks>
/// Mirrors <c>ICommandHandler</c>: a thin alias over MediatR's
/// <c>IRequestHandler</c> that fixes the response to
/// <see cref="Result{TValue}"/>. Handlers therefore cannot accidentally return a
/// bare value and bypass the failure channel, and the constraint keeps a query
/// and its handler from drifting apart.
/// <para>
/// Depending on this rather than on MediatR directly is what keeps an eventual
/// migration away from MediatR contained to these two files.
/// </para>
/// </remarks>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
