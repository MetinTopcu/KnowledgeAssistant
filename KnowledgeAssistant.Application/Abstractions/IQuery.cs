using KnowledgeAssistant.Domain.Common;
using MediatR;

namespace KnowledgeAssistant.Application.Abstractions;

/// <summary>
/// A request that reads without changing anything.
/// </summary>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
/// <remarks>
/// <para>
/// The read half of the split <c>ICommand</c> declares. It exists as a separate
/// marker rather than sharing one request type because the pipeline treats the
/// two differently: <c>ARCHITECTURE.md</c> reserves transaction behaviour for
/// commands and caching behaviour for queries, and a behaviour cannot select on
/// a distinction the type system does not make.
/// </para>
/// <para>
/// Naming a request a query is also a claim, and a useful one: it asserts that
/// running it twice changes nothing, which is what makes it safe to cache, retry,
/// or fan out. A request that writes must not wear this marker.
/// </para>
/// </remarks>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
