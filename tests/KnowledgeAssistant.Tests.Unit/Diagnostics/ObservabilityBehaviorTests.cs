using System.Diagnostics;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Application.Behaviors;
using KnowledgeAssistant.Application.Diagnostics;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Unit.Fakes;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAssistant.Tests.Unit.Diagnostics;

/// <summary>
/// The pipeline behaviour that traces and times every use case.
/// </summary>
/// <remarks>
/// <para>
/// The assertion worth reading first is
/// <see cref="Handle_WhenTheResultIsAFailure_RecordsItAsAFailure"/>. This
/// solution returns <see cref="Result"/> for expected failures rather than
/// throwing, so a behaviour that watched only for exceptions would report a
/// throttled embedding call as a success — and the service would show a flawless
/// error rate while answering nothing. That is the single most consequential
/// thing this type gets right.
/// </para>
/// </remarks>
public sealed class ObservabilityBehaviorTests
{
    private sealed record TestCommand : ICommand<string>;

    private sealed record TestQuery : IQuery<string>;

    private sealed class RecordingSliceMetrics : IResponseMetricsRecorder<Result<string>>
    {
        public List<Result<string>> Recorded { get; } = [];

        public void Record(Result<string> response) => Recorded.Add(response);
    }

    private static ApplicationMetrics CreateMetrics()
    {
        // A real IMeterFactory, so the instruments are created exactly as they are
        // in a running host and the MeterListener below sees them.
        ServiceProvider provider = new ServiceCollection().AddMetrics().BuildServiceProvider();

        return new ApplicationMetrics(provider.GetRequiredService<IMeterFactory>());
    }

    private static ObservabilityBehavior<TRequest, Result<string>> Behavior<TRequest>(
        ApplicationMetrics metrics,
        IResponseMetricsRecorder<Result<string>>? sliceMetrics = null)
        where TRequest : notnull =>
        new(metrics, sliceMetrics);

    [Fact]
    public async Task Handle_StartsAnActivityNamedAfterTheRequest()
    {
        using var activities = new ActivityCollector(ApplicationDiagnostics.ActivitySourceName);

        await Behavior<TestCommand>(CreateMetrics()).Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        Activity activity = activities.Single();

        activity.OperationName.Should().Be(nameof(TestCommand));
        activity.Kind.Should().Be(ActivityKind.Internal,
            "the ASP.NET Core instrumentation already produced the Server span this nests under");
        activity.GetTagItem(TelemetryTags.RequestName).Should().Be(nameof(TestCommand));
    }

    [Fact]
    public async Task Handle_TagsTheActivityWithSuccessAndSetsOkStatus()
    {
        using var activities = new ActivityCollector(ApplicationDiagnostics.ActivitySourceName);

        await Behavior<TestCommand>(CreateMetrics()).Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        Activity activity = activities.Single();

        activity.GetTagItem(TelemetryTags.Outcome).Should().Be("success");
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task Handle_WhenTheResultIsAFailure_RecordsItAsAFailure()
    {
        // A failed Result is an ordinary return value, not an exception. If this
        // is ever reported as a success, the error rate silently becomes fiction.
        using var activities = new ActivityCollector(ApplicationDiagnostics.ActivitySourceName);
        using var metrics = new MetricCollector(ApplicationDiagnostics.MeterName);

        await Behavior<TestCommand>(CreateMetrics()).Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Failure<string>(Error.Failure("Chat.RateLimited", "throttled"))),
            CancellationToken.None);

        Activity activity = activities.Single();

        activity.GetTagItem(TelemetryTags.Outcome).Should().Be("failure");
        activity.GetTagItem(TelemetryTags.ErrorCode).Should().Be("Chat.RateLimited");
        activity.Status.Should().Be(ActivityStatusCode.Error);

        metrics.For("knowledgeassistant.request.count").Should().ContainSingle()
            .Which.Tags[TelemetryTags.Outcome].Should().Be("failure");
    }

    [Fact]
    public async Task Handle_RecordsTheErrorCodeButNeverTheDescription()
    {
        // Descriptions can embed a file name or a question. Putting them on a span
        // both explodes tag cardinality and copies user-supplied content into
        // telemetry.
        using var activities = new ActivityCollector(ApplicationDiagnostics.ActivitySourceName);

        var error = Error.Validation("Upload.NotPdf", "The file 'salary-review-alice.docx' is not a PDF.");

        await Behavior<TestCommand>(CreateMetrics()).Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Failure<string>(error)),
            CancellationToken.None);

        Activity activity = activities.Single();

        activity.Tags.Should().NotContain(tag => tag.Value != null && tag.Value.Contains("salary-review-alice"));
        activity.GetTagItem(TelemetryTags.ErrorType).Should().Be(nameof(ErrorType.Validation));
    }

    [Fact]
    public async Task Handle_RecordsCountAndDurationWithMatchingTags()
    {
        // Both instruments must carry the same tags, or a rate chart and a latency
        // chart cannot be filtered the same way and stop being comparable.
        using var metrics = new MetricCollector(ApplicationDiagnostics.MeterName);

        await Behavior<TestCommand>(CreateMetrics()).Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        MetricCollector.Measurement count =
            metrics.For("knowledgeassistant.request.count").Should().ContainSingle().Subject;
        MetricCollector.Measurement duration =
            metrics.For("knowledgeassistant.request.duration").Should().ContainSingle().Subject;

        count.Value.Should().Be(1);
        duration.Value.Should().BeGreaterThanOrEqualTo(0);
        duration.Tags.Should().BeEquivalentTo(count.Tags);
    }

    [Theory]
    [InlineData(typeof(TestCommand), "command")]
    [InlineData(typeof(TestQuery), "query")]
    public async Task Handle_ClassifiesTheRequestAsACommandOrAQuery(Type requestType, string expectedKind)
    {
        // Worth splitting: a slow query is a user waiting, while a slow command is
        // usually an upstream service degrading. Aggregated together, one hides
        // the other.
        using var metrics = new MetricCollector(ApplicationDiagnostics.MeterName);
        ApplicationMetrics applicationMetrics = CreateMetrics();

        if (requestType == typeof(TestCommand))
        {
            await Behavior<TestCommand>(applicationMetrics).Handle(
                new TestCommand(), () => Task.FromResult(Result.Success("ok")), CancellationToken.None);
        }
        else
        {
            await Behavior<TestQuery>(applicationMetrics).Handle(
                new TestQuery(), () => Task.FromResult(Result.Success("ok")), CancellationToken.None);
        }

        metrics.For("knowledgeassistant.request.count").Should().ContainSingle()
            .Which.Tags[TelemetryTags.RequestKind].Should().Be(expectedKind);
    }

    [Fact]
    public async Task Handle_ReturnsTheHandlersResponseUnchanged()
    {
        // A behaviour that observes must not alter. Everything else in the suite
        // depends on this being true.
        Result<string> expected = Result.Success("the original response");

        Result<string> actual = await Behavior<TestCommand>(CreateMetrics()).Handle(
            new TestCommand(),
            () => Task.FromResult(expected),
            CancellationToken.None);

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Handle_OnSuccess_InvokesTheSlicesOwnMetrics()
    {
        var sliceMetrics = new RecordingSliceMetrics();
        Result<string> response = Result.Success("ok");

        await Behavior<TestCommand>(CreateMetrics(), sliceMetrics).Handle(
            new TestCommand(),
            () => Task.FromResult(response),
            CancellationToken.None);

        sliceMetrics.Recorded.Should().ContainSingle().Which.Should().BeSameAs(response);
    }

    [Fact]
    public async Task Handle_OnFailure_DoesNotInvokeTheSlicesOwnMetrics()
    {
        // A failed result has no value to measure, and a recorder reading Value
        // would throw inside telemetry — turning a handled failure into an
        // unhandled one.
        var sliceMetrics = new RecordingSliceMetrics();

        await Behavior<TestCommand>(CreateMetrics(), sliceMetrics).Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Failure<string>(Error.Failure("Some.Error", "x"))),
            CancellationToken.None);

        sliceMetrics.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithNoSliceMetricsRegistered_StillSucceeds()
    {
        // The optional dependency is what lets a slice with nothing worth
        // measuring carry no ceremony at all.
        Result<string> result = await Behavior<TestCommand>(CreateMetrics(), sliceMetrics: null).Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheHandlerThrows_MarksTheActivityAndRethrows()
    {
        // An exception here is a genuine fault, distinct from the expected
        // failures that arrive as a failed Result. It must be recorded and must
        // still reach the caller.
        using var activities = new ActivityCollector(ApplicationDiagnostics.ActivitySourceName);
        using var metrics = new MetricCollector(ApplicationDiagnostics.MeterName);

        var boom = new InvalidOperationException("boom");

        Func<Task> act = () => Behavior<TestCommand>(CreateMetrics()).Handle(
            new TestCommand(),
            () => Task.FromException<Result<string>>(boom),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        activities.Single().Status.Should().Be(ActivityStatusCode.Error);

        metrics.For("knowledgeassistant.request.count").Should().ContainSingle()
            .Which.Tags[TelemetryTags.ErrorCode].Should().Be("Unhandled");
    }

    [Fact]
    public async Task Handle_WhenNothingIsListening_StillReturnsTheResponse()
    {
        // No ActivityCollector here, so StartActivity returns null. Instrumented
        // code has to stay correct on that path, because it is the one a host with
        // telemetry disabled always takes.
        Result<string> result = await Behavior<TestCommand>(CreateMetrics()).Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.Value.Should().Be("ok");
    }
}
