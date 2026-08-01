using System.Reflection;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Infrastructure.DependencyInjection;
using NetArchTest.Rules;

namespace KnowledgeAssistant.Tests.Architecture;

/// <summary>
/// Conventions that keep the CQRS surface consistent as slices are added.
/// </summary>
/// <remarks>
/// ARCHITECTURE.md §6 asks for exactly these: every command and query reachable
/// only through the mediator, and handlers that cannot be constructed directly.
/// They matter most for the slice nobody has written yet — the one that would
/// otherwise establish a second, divergent style.
/// </remarks>
public sealed class ConventionTests
{
    private static Assembly ApplicationAssembly => typeof(ICommand<>).Assembly;
    private static Assembly InfrastructureAssembly => typeof(InfrastructureServiceRegistration).Assembly;

    [Fact]
    public void HandlersAreInternal()
    {
        // Nothing outside Application should construct a handler: the only
        // legitimate entry point is sending the request through the mediator.
        // MediatR still finds them, because assembly scanning sees internal types.
        IEnumerable<Type> publicHandlers = ApplicationAssembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Where(type => type.IsPublic);

        publicHandlers.Should().BeEmpty(
            "handlers are reached through the mediator, never constructed by a caller");
    }

    [Fact]
    public void HandlersAreSealed()
    {
        IEnumerable<Type> unsealed = ApplicationAssembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Where(type => type is { IsClass: true, IsAbstract: false, IsSealed: false });

        unsealed.Should().BeEmpty("a handler is a leaf; nothing should extend one");
    }

    [Fact]
    public void ValidatorsAreInternal()
    {
        IEnumerable<Type> publicValidators = ApplicationAssembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Validator", StringComparison.Ordinal))
            .Where(type => type.IsPublic);

        publicValidators.Should().BeEmpty(
            "validators run inside the slice; callers see their results as errors, not their types");
    }

    [Fact]
    public void EveryCommandAndQueryHasExactlyOneHandler()
    {
        // The failure this prevents is a runtime resolution error on the first
        // request to a new endpoint, which is a long way from the change that
        // caused it.
        Type[] requests = [.. ApplicationAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(IsCommandOrQuery))];

        Type[] handlers = [.. ApplicationAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(IsHandler))];

        requests.Should().NotBeEmpty("the assembly should contain at least one command or query");

        foreach (Type request in requests)
        {
            int matching = handlers.Count(handler => handler
                .GetInterfaces()
                .Where(IsHandler)
                .Any(handlerInterface => handlerInterface.GetGenericArguments()[0] == request));

            matching.Should().Be(1, $"{request.Name} must have exactly one handler");
        }
    }

    [Fact]
    public void InfrastructureAdaptersAreInternal()
    {
        // The adapters satisfy Application's ports and are registered by name in
        // one file. Nothing else should be able to name them, which is what makes
        // renaming or replacing one a contained change.
        IEnumerable<Type> publicAdapters = InfrastructureAssembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Service", StringComparison.Ordinal))
            .Where(type => type is { IsClass: true, IsPublic: true });

        publicAdapters.Should().BeEmpty(
            "adapters are an implementation detail behind the ports they satisfy");
    }

    [Fact]
    public void OptionsClassesArePublicAndSealed()
    {
        // Options are bound by the host and validated at startup, so they are the
        // one Infrastructure shape that is legitimately public.
        IEnumerable<Type> offenders = InfrastructureAssembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Options", StringComparison.Ordinal))
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => !type.IsPublic || !type.IsSealed);

        offenders.Should().BeEmpty("options are bound by the host and are not extension points");
    }

    [Fact]
    public void ApplicationTypesDoNotExposeAzureInTheirNames()
    {
        // A weaker cousin of the vendor-dependency rule, and a deliberate
        // exception: IAzureSearchService was named that way by explicit request.
        // Pinning the exception in a test means a second one is a conversation
        // rather than a precedent.
        string[] permitted = ["IAzureSearchService"];

        IEnumerable<string> offenders = ApplicationAssembly
            .GetTypes()
            .Where(type => type.IsPublic)
            .Select(type => type.Name)
            .Where(name => name.Contains("Azure", StringComparison.Ordinal))
            .Where(name => !permitted.Contains(name, StringComparer.Ordinal));

        offenders.Should().BeEmpty(
            "ports are named in the domain's language; IAzureSearchService is a recorded exception");
    }

    [Fact]
    public void EveryPortIsAnInterfaceInTheInterfacesNamespace()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespace("KnowledgeAssistant.Application.Interfaces")
            .And()
            .HaveNameStartingWith("I")
            .Should()
            .BeInterfaces()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"outbound ports are interfaces. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    private static bool IsCommandOrQuery(Type type) =>
        type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
         type.GetGenericTypeDefinition() == typeof(IQuery<>));

    private static bool IsHandler(Type type) =>
        type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
         type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
}
