using System.Collections.Immutable;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Scoped Metadata and Roslyn views of a frozen set. Use only within the
/// CompileReferenceSet.Use callback; the source is never a compiler reference.
/// </summary>
public sealed class CompileReferenceContext : IAssemblyReferenceResolver
{
    readonly CompileReferenceSet _set;

    internal CompileReferenceContext(CompileReferenceSet set)
    {
        _set = set;
        Source = set.Source.Assembly;
        CompilerReferences = [.. set.References.Select(descriptor =>
            MetadataReference.CreateFromImage(descriptor.Image.Snapshot.Content, descriptor.Properties))];
    }

    public ResolvedAssemblyReference Source { get; }
    public ImmutableArray<PortableExecutableReference> CompilerReferences { get; }

    /// <summary>Exact identity resolution only; this slice authorizes no platform candidates.</summary>
    public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (scope != AssemblyResolutionScope.Any)
            return null;
        return _set.References
            .SingleOrDefault(reference => identity.IsEquivalentTo(reference.Image.Identity))?.Image.Assembly;
    }
}
