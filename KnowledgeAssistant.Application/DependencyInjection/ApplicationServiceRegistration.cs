using System.Reflection;
using FluentValidation;
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

        return services;
    }
}
