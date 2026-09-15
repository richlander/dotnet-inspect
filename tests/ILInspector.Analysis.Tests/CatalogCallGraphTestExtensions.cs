using System.Collections.Immutable;

using DotnetInspector.Services;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

internal static class CatalogCallGraphTestExtensions
{
    internal static CallTreeNode BuildCallerTree(
        this LibraryBodyIndex root,
        int rootMethodToken,
        IReadOnlyList<LibraryBodyIndex>? callerScopes,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        if (callerScopes is null)
        {
            return root.BuildCallerTree(
                rootMethodToken,
                maxDepth,
                maxNodes);
        }

        using CatalogCallGraphScope scope =
            CreateScope(root, callerScopes);
        return scope.BuildCallerTree(
            root,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }

    internal static CallTreeNode BuildCallTree(
        this LibraryBodyIndex root,
        int rootMethodToken,
        IReadOnlyList<LibraryBodyIndex> calleeScopes,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        using CatalogCallGraphScope scope =
            CreateScope(root, calleeScopes);
        return scope.BuildCallTree(
            root,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }

    internal static CatalogCallGraphScope CreateScope(
        LibraryBodyIndex root,
        IReadOnlyList<LibraryBodyIndex> scopes)
    {
        var seen = new HashSet<LibraryBodyIndex>(
            ReferenceEqualityComparer.Instance);
        var indexBuilder =
            ImmutableArray.CreateBuilder<LibraryBodyIndex>();
        if (seen.Add(root))
            indexBuilder.Add(root);
        foreach (LibraryBodyIndex index in scopes)
        {
            if (seen.Add(index))
                indexBuilder.Add(index);
        }

        ImmutableArray<LibraryBodyIndex> indexes =
            indexBuilder.ToImmutable();
        var entries = indexes.Select(index =>
        {
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.CreateFromPath(
                    index.Path,
                    AssemblyResolutionProvenance.Local(
                        "call-graph test participant"));
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(index.Path)
                {
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward = true,
                });
            return (Index: index, Assembly: assembly, Policy:
                (IAssemblyBindingPolicy)resolver);
        })
            .GroupBy(entry => (
                entry.Assembly.Identity,
                entry.Index.DeclaredMethods.FirstOrDefault()
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
                    entry.Index,
                    entry.Assembly)));
    }
}
