namespace KnowledgeAssistant.Tests.Unit.Fakes;

/// <summary>
/// A stream that remembers whether it was disposed.
/// </summary>
/// <remarks>
/// Ingestion downloads a blob and owns the resulting stream. "It is disposed on
/// both the success and the failure path" is a leak test, and a leak is
/// invisible to every other assertion.
/// </remarks>
internal sealed class TrackedStream(byte[] data) : MemoryStream(data)
{
    public bool Disposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Disposed = true;
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        Disposed = true;
        return base.DisposeAsync();
    }
}
