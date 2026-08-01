using KnowledgeAssistant.Api.Extensions;
using KnowledgeAssistant.Application.Queries.Documents.Ask;
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
}
