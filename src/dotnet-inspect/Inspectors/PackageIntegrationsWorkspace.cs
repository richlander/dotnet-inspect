using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record PackageIntegrationAssembly(
    string Path,
    string? TargetFramework);

/// <summary>
/// Owns the binding-consistent package groups used by one all-library
/// Integrations request.
/// </summary>
internal sealed class PackageIntegrationsWorkspace : IDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly Dictionary<string, ParticipantResult> _participants;

    PackageIntegrationsWorkspace(
        InspectionWorkspace workspace,
        Dictionary<string, ParticipantResult> participants,
        int contextGroupCount)
    {
        _workspace = workspace;
        _participants = participants;
        ContextGroupCount = contextGroupCount;
    }

    internal int ContextGroupCount { get; }

    internal static PackageIntegrationsWorkspace Create(
        IEnumerable<PackageIntegrationAssembly> assemblies,
        string packageName,
        string packageVersion)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        var workspace = new InspectionWorkspace();
        try
        {
            var results = new Dictionary<string, ParticipantResult>(
                StringComparer.Ordinal);
            int contextGroupCount = 0;
            foreach (IGrouping<string, PackageIntegrationAssembly> context
                in assemblies.GroupBy(
                    static assembly => assembly.TargetFramework ?? "",
                    StringComparer.OrdinalIgnoreCase))
            {
                List<Root> roots = [];
                foreach (PackageIntegrationAssembly assembly in context)
                {
                    var provenance = AssemblyResolutionProvenance.Package(
                        packageName,
                        packageVersion,
                        assembly.TargetFramework,
                        rid: null);
                    if (!ResolvedAssemblyReference.TryCreateFromPath(
                            assembly.Path,
                            provenance,
                            out ResolvedAssemblyReference? reference))
                    {
                        continue;
                    }

                    var policy = new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            reference.Path!)
                        {
                            TargetFramework =
                                assembly.TargetFramework,
                        });
                    roots.Add(new Root(assembly, reference, policy));
                }

                if (roots.Count == 0)
                    continue;

                var groupPolicy =
                    new SourceRelativeAssemblyGroupBindingPolicy(
                        roots.Select(static root =>
                            (root.Reference,
                                (IAssemblyBindingPolicy)root.Policy)));
                AssemblyContextGroup group =
                    workspace.CreateAssemblyContextGroup(
                        roots.Select(root =>
                            new AssemblyContextParticipant(
                                root.Reference,
                                groupPolicy)));
                AssemblyContextIntegrationsResult integrations =
                    AssemblyContextIntegrationsQuery.Execute(group);

                for (int index = 0; index < roots.Count; index++)
                {
                    Root root = roots[index];
                    AssemblyIntegrationsEntry entry =
                        integrations.Assemblies[index];
                    results.Add(
                        Path.GetFullPath(root.Input.Path),
                        new ParticipantResult(
                            group,
                            root.Reference,
                            entry));
                }

                contextGroupCount++;
            }

            return new PackageIntegrationsWorkspace(
                workspace,
                results,
                contextGroupCount);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    internal async Task<TResult> UseAssemblyAsync<TResult>(
        string path,
        Func<
            ResolvedAssemblyReference?,
            AssemblyIntegrationsEntry?,
            Task<TResult>> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(callback);

        if (!_participants.TryGetValue(
                Path.GetFullPath(path),
                out ParticipantResult? participant))
        {
            return await callback(null, null).ConfigureAwait(false);
        }

        AssemblyImageAccessResult<TResult> access =
            await participant.Group.UseAssemblyReferenceAsync(
                    participant.Reference,
                    retained => callback(
                        retained,
                        participant.Integrations))
                .ConfigureAwait(false);
        return access switch
        {
            AssemblyImageAccessResult<TResult>.Available available =>
                available.Value,
            AssemblyImageAccessResult<TResult>.Rejected =>
                await callback(
                        null,
                        participant.Integrations)
                    .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    public void Dispose() => _workspace.Dispose();

    sealed record Root(
        PackageIntegrationAssembly Input,
        ResolvedAssemblyReference Reference,
        AssemblyDependencyResolver Policy);

    sealed record ParticipantResult(
        AssemblyContextGroup Group,
        ResolvedAssemblyReference Reference,
        AssemblyIntegrationsEntry Integrations);
}
