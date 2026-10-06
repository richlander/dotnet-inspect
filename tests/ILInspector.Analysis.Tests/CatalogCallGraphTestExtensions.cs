using System.Collections.Immutable;

using DotnetInspector.Services;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

internal static class CatalogCallGraphTestExtensions
{
    internal static CallTreeNode BuildCallerTree(
        this LibraryBodyAnalysisExecution root,
        int rootMethodToken,
        int maxDepth = 3,
        int maxNodes = 25) =>
        root.CallGraph.BuildCallerTree(
            rootMethodToken,
            maxDepth,
            maxNodes);

    internal static CallTreeNode BuildCallerTree(
        this LibraryBodyAnalysisExecution root,
        int rootMethodToken,
        IReadOnlyList<LibraryBodyAnalysisExecution> callerScopes,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        using CatalogCallGraphScope scope =
            CreateScope(root, callerScopes);
        return scope.BuildCallerTree(
            root.CallGraph,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }

    internal static CallTreeNode BuildCallTree(
        this LibraryBodyAnalysisExecution root,
        int rootMethodToken,
        int maxDepth = 3,
        int maxNodes = 25) =>
        root.CallGraph.BuildCallTree(
            rootMethodToken,
            maxDepth,
            maxNodes);

    internal static CallTreeNode BuildCallTree(
        this LibraryBodyAnalysisExecution root,
        int rootMethodToken,
        IReadOnlyList<LibraryBodyAnalysisExecution> calleeScopes,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        using CatalogCallGraphScope scope =
            CreateScope(root, calleeScopes);
        return scope.BuildCallTree(
            root.CallGraph,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }

    internal static CatalogCallGraphScope CreateScope(
        LibraryBodyAnalysisExecution root,
        IReadOnlyList<LibraryBodyAnalysisExecution> scopes)
    {
        var seen = new HashSet<LibraryBodyAnalysisExecution>(
            ReferenceEqualityComparer.Instance);
        var indexBuilder =
            ImmutableArray.CreateBuilder<LibraryBodyAnalysisExecution>();
        if (seen.Add(root))
            indexBuilder.Add(root);
        foreach (LibraryBodyAnalysisExecution index in scopes)
        {
            if (seen.Add(index))
                indexBuilder.Add(index);
        }

        ImmutableArray<LibraryBodyAnalysisExecution> indexes =
            indexBuilder.ToImmutable();
        var entries = indexes.Select(index =>
        {
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromPath(
                    index.Receipt.SourceName,
                    AssemblyResolutionProvenance.Local(
                        "call-graph test participant"));
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(index.Receipt.SourceName)
                {
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                });
            return (Index: index, Assembly: assembly, Policy:
                (IAssemblyBindingPolicy)resolver);
        })
            .GroupBy(entry => (
                entry.Assembly.Identity,
                entry.Index.CallGraph.DeclaredMethods.FirstOrDefault()
                    ?.ModuleVersionId ?? Guid.Empty))
            .Select(group => group.First())
            .ToImmutableArray();

        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            entries.Select(entry =>
                (entry.Assembly, entry.Policy)));
        return new CatalogCallGraphScope(
            policy,
            entries.Select(entry =>
                new CatalogCallGraphParticipant(
                    entry.Index.CallGraph,
                    entry.Assembly)));
    }
}
