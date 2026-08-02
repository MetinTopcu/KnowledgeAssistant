using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace KnowledgeAssistant.Tests.Unit.Fakes;

/// <summary>
/// Captures the activities started by a named source for the duration of a test.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="ActivitySource"/> only produces an <see cref="Activity"/> when
/// something is listening — that is the design that makes instrumentation free
/// when telemetry is off, and it means a test that simply calls instrumented code
/// and inspects <see cref="Activity.Current"/> sees nothing at all. Subscribing a
/// listener is what makes spans observable, and it is also the closest available
/// stand-in for what the OpenTelemetry SDK does in production.
/// </para>
/// <para>
/// Hand-written rather than taken from a testing package: this is the whole of
/// what such a package would provide here, and the repo's fakes are already its
/// own.
/// </para>
/// </remarks>
internal sealed class ActivityCollector : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public ActivityCollector(string sourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,

            // AllDataAndRecorded, or StartActivity returns null and the code under
            // test takes its "nobody is listening" path — which would make every
            // assertion below vacuously pass.
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,

            ActivityStopped = activity =>
            {
                lock (_activities)
                {
                    _activities.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>The activities that have completed, in completion order.</summary>
    public IReadOnlyList<Activity> Activities
    {
        get
        {
            lock (_activities)
            {
                return [.. _activities];
            }
        }
    }

    /// <summary>The single completed activity, failing the test if there is not exactly one.</summary>
    public Activity Single() => Activities.Should().ContainSingle().Subject;

    public void Dispose() => _listener.Dispose();
}

/// <summary>Captures measurements recorded on a named meter.</summary>
/// <remarks>
/// Records the instrument name and the tags rather than only the value, because
/// the tags are most of what a metric is worth: a duration histogram nobody can
/// split by outcome answers almost no question worth asking.
/// </remarks>
internal sealed class MetricCollector : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<Measurement> _measurements = [];

    public MetricCollector(string meterName)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        _listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => Record(instrument.Name, value, tags));
        _listener.SetMeasurementEventCallback<int>(
            (instrument, value, tags, _) => Record(instrument.Name, value, tags));
        _listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => Record(instrument.Name, value, tags));

        _listener.Start();
    }

    public IReadOnlyList<Measurement> Measurements
    {
        get
        {
            lock (_measurements)
            {
                return [.. _measurements];
            }
        }
    }

    /// <summary>The measurements recorded against one instrument.</summary>
    public IReadOnlyList<Measurement> For(string instrumentName) =>
        [.. Measurements.Where(measurement => measurement.InstrumentName == instrumentName)];

    public void Dispose() => _listener.Dispose();

    private void Record(string instrumentName, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var captured = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (KeyValuePair<string, object?> tag in tags)
        {
            captured[tag.Key] = tag.Value;
        }

        lock (_measurements)
        {
            _measurements.Add(new Measurement(instrumentName, value, captured));
        }
    }

    internal sealed record Measurement(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> over a value a test can change.</summary>
/// <remarks>
/// The health checks read <c>CurrentValue</c> on every probe so a configuration
/// reload takes effect without a restart. Testing that behaviour needs a monitor
/// whose value moves, which <c>Options.Create</c> does not provide.
/// </remarks>
internal sealed class MutableOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue { get; set; } = value;

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}

/// <summary>A clock a test moves by hand.</summary>
/// <remarks>
/// Cache expiry is a statement about elapsed time. Asserting it against the real
/// clock would mean sleeping, which makes a suite slow and, worse, intermittently
/// wrong on a loaded build agent.
/// </remarks>
internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
