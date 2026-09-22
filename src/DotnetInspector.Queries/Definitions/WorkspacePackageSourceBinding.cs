using System.Collections.ObjectModel;

using NuGetFetch;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Host-neutral result of binding explicit credentials to portable Workspace
/// package-source declarations.
/// </summary>
public abstract record WorkspacePackageSourceBindingResult
{
    private protected WorkspacePackageSourceBindingResult()
    {
    }

    public sealed record Bound(WorkspacePackageSourceBindingPlan Plan)
        : WorkspacePackageSourceBindingResult;

    public sealed record Rejected(string Message)
        : WorkspacePackageSourceBindingResult;
}

/// <summary>
/// Ordered package sources and authentication requirements left for the host.
/// </summary>
public sealed class WorkspacePackageSourceBindingPlan
{
    internal WorkspacePackageSourceBindingPlan(
        PackageSource[] sources,
        WorkspacePackageSourceDefinition[] unboundAuthenticationRequirements)
    {
        Sources = new ReadOnlyCollection<PackageSource>(
            (PackageSource[])sources.Clone());
        UnboundAuthenticationRequirements =
            new ReadOnlyCollection<WorkspacePackageSourceDefinition>(
                (WorkspacePackageSourceDefinition[])
                    unboundAuthenticationRequirements.Clone());
    }

    public IReadOnlyList<PackageSource> Sources { get; }

    public IReadOnlyList<WorkspacePackageSourceDefinition>
        UnboundAuthenticationRequirements { get; }
}

/// <summary>
/// Binds host-supplied Basic credentials without choosing how a host satisfies
/// remaining authentication requirements.
/// </summary>
public static class WorkspacePackageSourceBinding
{
    public static WorkspacePackageSourceBindingResult Create(
        IReadOnlyList<WorkspacePackageSourceDefinition> definitions,
        IReadOnlyDictionary<string, PackageSourceCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(credentials);
        WorkspacePackageSourceDefinition.ValidateSet(definitions);

        var required = definitions
            .Where(static source =>
                source.Authentication
                    == WorkspacePackageSourceAuthentication.AuthenticationRequired)
            .ToDictionary(
                static source => source.Endpoint,
                StringComparer.Ordinal);
        foreach ((string endpoint, PackageSourceCredential credential)
            in credentials)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return new WorkspacePackageSourceBindingResult.Rejected(
                    "An explicit credential endpoint must not be empty.");
            }
            if (!required.ContainsKey(endpoint))
            {
                return new WorkspacePackageSourceBindingResult.Rejected(
                    $"Explicit credential endpoint '{endpoint}' is not an "
                        + "authentication-required source in this Workspace.");
            }
            if (credential is null
                || string.IsNullOrWhiteSpace(credential.Username))
            {
                return new WorkspacePackageSourceBindingResult.Rejected(
                    $"Explicit credential username for endpoint '{endpoint}' "
                        + "must not be empty.");
            }
            if (string.IsNullOrEmpty(credential.Password))
            {
                return new WorkspacePackageSourceBindingResult.Rejected(
                    $"Explicit credential password for endpoint '{endpoint}' "
                        + "must not be empty.");
            }
        }

        foreach (IGrouping<string, WorkspacePackageSourceDefinition> origin
            in required.Values.GroupBy(
                static source => new Uri(source.Endpoint)
                    .GetLeftPart(UriPartial.Authority),
                StringComparer.Ordinal))
        {
            int explicitCount = origin.Count(
                source => credentials.ContainsKey(source.Endpoint));
            if (explicitCount != 0 && explicitCount != origin.Count())
            {
                return new WorkspacePackageSourceBindingResult.Rejected(
                    $"Authenticated Workspace source origin '{origin.Key}' "
                        + "mixes explicit credentials with an unbound "
                        + "authentication requirement. Bind every endpoint "
                        + "on that origin or none.");
            }
        }

        PackageSource[] sources =
        [
            .. definitions.Select(source =>
                new PackageSource(
                    source.Endpoint,
                    source.Endpoint,
                    credentials.TryGetValue(
                        source.Endpoint,
                        out PackageSourceCredential? credential)
                        ? credential
                        : null)),
        ];
        WorkspacePackageSourceDefinition[] unbound =
        [
            .. definitions.Where(source =>
                source.Authentication
                    == WorkspacePackageSourceAuthentication.AuthenticationRequired
                && !credentials.ContainsKey(source.Endpoint)),
        ];
        return new WorkspacePackageSourceBindingResult.Bound(
            new WorkspacePackageSourceBindingPlan(sources, unbound));
    }
}
