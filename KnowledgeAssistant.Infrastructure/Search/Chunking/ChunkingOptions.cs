using System.ComponentModel.DataAnnotations;

namespace KnowledgeAssistant.Infrastructure.Search.Chunking;

/// <summary>
/// Configuration for the chunking algorithm, bound from the <c>Chunking</c>
/// section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these are settings and not constants.</b> Chunk size is not an
/// arbitrary preference — it is chosen against the token limit of whatever
/// consumes the chunks, and against how retrieval performs on the corpus in
/// question. Both change without the code changing. <c>Search/README.md</c>
/// records that chunking strategy is a versioned decision, which is precisely
/// the kind of decision that should be visible in configuration rather than
/// buried in a literal.
/// </para>
/// <para>
/// The defaults are the specified 800 and 200, so an environment that sets
/// nothing behaves exactly as required.
/// </para>
/// <para>
/// <b>Changing these invalidates existing chunks.</b> Chunk ids derive from
/// position, not content, so re-chunking with a different size maps old ids onto
/// different text. Treat a change here as a corpus rebuild, not a tuning knob to
/// nudge in production.
/// </para>
/// </remarks>
public sealed class ChunkingOptions : IValidatableObject
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Chunking";

    /// <summary>The maximum number of characters in a chunk.</summary>
    /// <remarks>
    /// An upper bound, not a target. A chunk ends early whenever doing so
    /// preserves a paragraph or avoids splitting a word.
    /// </remarks>
    [Range(1, 100_000, ErrorMessage = "Chunking:MaxChunkSize must be between 1 and 100000.")]
    public int MaxChunkSize { get; init; } = 800;

    /// <summary>
    /// The number of characters each chunk repeats from the end of the previous
    /// one.
    /// </summary>
    /// <remarks>
    /// Overlap exists so that a passage spanning a chunk boundary is still fully
    /// present in at least one chunk. Without it, the sentence that happens to
    /// straddle a boundary is retrievable from neither side.
    /// </remarks>
    [Range(0, 100_000, ErrorMessage = "Chunking:OverlapSize must be between 0 and 100000.")]
    public int OverlapSize { get; init; } = 200;

    /// <summary>
    /// Rejects an overlap that is not smaller than the chunk size.
    /// </summary>
    /// <remarks>
    /// A cross-property rule, so it cannot be expressed as an attribute on
    /// either property alone. It is not a stylistic check: an overlap equal to or
    /// larger than the chunk size means each chunk begins at or before the
    /// previous one started, which is a non-terminating split. The algorithm
    /// guards against that too, but catching it here fails the deployment with a
    /// message naming the settings, instead of silently degrading to a
    /// pathological chunking at runtime.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OverlapSize >= MaxChunkSize)
        {
            yield return new ValidationResult(
                "Chunking:OverlapSize must be strictly less than Chunking:MaxChunkSize.",
                [nameof(OverlapSize), nameof(MaxChunkSize)]);
        }
    }
}
