using KnowledgeAssistant.Application.Abstractions;

namespace KnowledgeAssistant.Application.Queries.Documents.List;

/// <summary>
/// Lists the documents that have been ingested.
/// </summary>
/// <param name="MaxResults">
/// The largest number of documents to return, newest upload first.
/// </param>
/// <remarks>
/// <para>
/// The cheapest request in this service: one search call, no embedding, no
/// completion, nothing billed by the token. It exists so the corpus can be
/// inspected — "did my upload land?" is the first question anyone asks after
/// ingesting, and until now the only way to answer it was to ask a question and
/// see whether the document was cited.
/// </para>
/// <para>
/// <b>Why a limit is on the request rather than fixed.</b> A browser wants a
/// screenful; a reconciliation script wants everything it can get in one call.
/// The validator bounds it so neither can turn this into an unbounded scan.
/// </para>
/// </remarks>
public sealed record ListDocumentsQuery(int MaxResults) : IQuery<ListDocumentsResponse>;
