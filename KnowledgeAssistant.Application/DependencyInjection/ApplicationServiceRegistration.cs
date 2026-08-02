using System.Reflection;
using FluentValidation;
using KnowledgeAssistant.Application.Behaviors;
using KnowledgeAssistant.Application.Diagnostics;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeAssistant.Application.DependencyInjection;

/// <summary>
/// The Application layer's composition root.
/// </summary>
/// <remarks>
/// Mirrors <c>Infrastructure/DependencyInjection</c> so each layer owns its own
/// registration and the host calls one method per layer. Without it, the API
/// project would have to know which assembly to scan for handlers and
/// validators — knowledge that belongs to this layer.
/// </remarks>
public static class ApplicationServiceRegistration
{
    /// <summary>
    /// Registers the mediator, the validators, and the clock.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Assembly applicationAssembly = typeof(ApplicationServiceRegistration).Assembly;

        // Scanning beats registering handlers by hand: a new slice is discovered
        // automatically, so nobody can add a command and forget the wiring —
        // a failure that otherwise appears only as a runtime resolution error.
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(applicationAssembly));

        // includeInternalTypes is required because the validators are internal.
        // Omitting it registers nothing, and the handler then fails to resolve
        // IValidator<T> at request time rather than at startup.
        services.AddValidatorsFromAssembly(applicationAssembly, includeInternalTypes: true);

        // TryAdd, so a test host can substitute FakeTimeProvider by registering
        // it first. TimeProvider is a BCL type, not a vendor dependency, so
        // depending on it directly does not breach the layer boundary.
        services.TryAddSingleton(TimeProvider.System);

        services.AddApplicationTelemetry(applicationAssembly);

        return services;
    }

    /// <summary>
    /// Registers the instrumentation every slice is observed through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note what is <i>not</i> here: no exporter, no telemetry vendor, no
    /// decision about where measurements go. This layer emits through BCL types
    /// and the composition root decides who listens — which is why the Azure
    /// Monitor packages appear in the API project alone.
    /// </para>
    /// <para>
    /// The behaviour is registered open-generically, so it wraps every request
    /// type that exists now and every one added later. Registration order is the
    /// execution order in MediatR, and this one is registered first deliberately:
    /// the span it opens must enclose the work of any behaviour added after it,
    /// or that work is timed by nothing and traced nowhere.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddApplicationTelemetry(
        this IServiceCollection services,
        Assembly applicationAssembly)
    {
        // Idempotent, and the host has usually called it already. Calling it here
        // anyway is what lets this layer be composed into a plain ServiceCollection
        // — a console worker, a test — without the caller knowing that metrics
        // need a factory.
        services.AddMetrics();

        services.TryAddSingleton<ApplicationMetrics>();

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ObservabilityBehavior<,>));

        RegisterSliceMetrics(services, applicationAssembly);

        return services;
    }

    /// <summary>
    /// Registers every slice's own <see cref="IResponseMetricsRecorder{TResponse}"/>.
    /// </summary>
    /// <remarks>
    /// Discovered by scanning, for the same reason handlers and validators are: a
    /// slice that adds measurements should not also have to remember to edit this
    /// file. Forgetting would not fail the build or any test — the metrics would
    /// simply never be recorded, which is the kind of absence nobody notices until
    /// they go looking for a chart that was never populated.
    /// </remarks>
    private static void RegisterSliceMetrics(IServiceCollection services, Assembly applicationAssembly)
    {
        foreach (Type recorder in applicationAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }))
        {
            foreach (Type contract in recorder.GetInterfaces()
                .Where(contract => contract.IsGenericType
                                && contract.GetGenericTypeDefinition() == typeof(IResponseMetricsRecorder<>)))
            {
                services.TryAddSingleton(contract, recorder);
            }
        }
    }
}
