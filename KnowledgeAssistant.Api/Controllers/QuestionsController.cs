using KnowledgeAssistant.Api.Extensions;
using KnowledgeAssistant.Application.Queries.Documents.Ask;
using KnowledgeAssistant.Application.Queries.Documents.AskAgent;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeAssistant.Api.Controllers;

/// <summary>
/// Question answering over the ingested corpus.
/// </summary>
/// <remarks>
/// Named for questions rather than chat because that is what it does: one
/// question, one grounded answer, no conversation retained between calls.
/// Calling it a chat endpoint would promise a continuity it does not offer.
/// </remarks>
// See DocumentsController for why there is no class-level [Produces].
[ApiController]
[Route("api/questions")]
public sealed class QuestionsController : ControllerBase
{
    /// <summary>The number of chunks retrieved when the caller does not say.</summary>
    /// <remarks>
    /// Five is enough evidence for most questions without crowding the prompt.
    /// It lives here rather than in the query because it is a transport
    /// convenience — the query itself always carries an explicit value, so no
    /// inner layer has to know what "unspecified" means.
    /// </remarks>
    private const int DefaultTopK = 5;

    /// <summary>
    /// The passages one of the agent's searches returns when the caller does not
    /// say.
    /// </summary>
    /// <remarks>
    /// Matches <see cref="DefaultTopK"/> so the two endpoints retrieve comparably
    /// per search, and lives here for the same reason: it is a transport
    /// convenience, and the query itself always carries an explicit value.
    /// </remarks>
    private const int DefaultMaxSources = 5;

    private readonly ISender _sender;

    /// <summary>Initialises the controller.</summary>
    public QuestionsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Answers a question using the indexed documents.
    /// </summary>
    /// <param name="request">The question and optional retrieval depth.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>The answer with its citations, or a problem response.</returns>
    /// <remarks>
    /// <para>
    /// As mechanical as the upload action: map transport to query, send,
    /// translate the outcome. It holds no rule about what a valid question is and
    /// no knowledge of retrieval — asking "is this answerable?" here would put a
    /// product rule where no non-HTTP caller could reach it.
    /// </para>
    /// <para>
    /// <c>POST</c> rather than <c>GET</c> despite being a read. A question is
    /// free text of up to two thousand characters, which does not belong in a URL
    /// that gets logged by every proxy in the path — and the response is neither
    /// cacheable nor idempotent in cost, so the semantics <c>GET</c> would
    /// promise are ones this endpoint cannot keep.
    /// </para>
    /// </remarks>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AskQuestionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AskAsync(
        [FromBody] AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var query = new AskQuestionQuery(
            Question: request.Question ?? string.Empty,
            TopK: request.TopK ?? DefaultTopK);

        var result = await _sender.Send(query, cancellationToken).ConfigureAwait(false);

        return result.ToActionResult();
    }

    /// <summary>
    /// Answers a question with the agent, which searches the documents itself.
    /// </summary>
    /// <param name="request">The question and optional per-search retrieval depth.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <returns>The answer with its citations, or a problem response.</returns>
    /// <remarks>
    /// <para>
    /// A sibling route rather than a flag on the action above, because the two make
    /// different promises. That one performs exactly one search and one completion,
    /// so its latency and its cost are predictable. This one lets the agent decide
    /// how many times to look — which answers questions the other cannot, and costs
    /// what it costs. A caller should choose that deliberately, and a boolean on a
    /// shared endpoint is not how anyone chooses anything deliberately.
    /// </para>
    /// <para>
    /// As mechanical as its neighbour: map transport to query, send, translate the
    /// outcome. It holds no rule about what a valid question is and no knowledge of
    /// how the agent works.
    /// </para>
    /// <para>
    /// <c>POST</c> rather than <c>GET</c> for the same reasons: a question is free
    /// text that does not belong in a URL every proxy in the path logs, and the
    /// response is neither cacheable nor idempotent in cost.
    /// </para>
    /// </remarks>
    [HttpPost("agent")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AskAgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AskAgentAsync(
        [FromBody] AskAgentRequest request,
        CancellationToken cancellationToken)
    {
        var query = new AskAgentQuery(
            Question: request.Question ?? string.Empty,
            MaxSources: request.MaxSources ?? DefaultMaxSources);

        var result = await _sender.Send(query, cancellationToken).ConfigureAwait(false);

        return result.ToActionResult();
    }
}
