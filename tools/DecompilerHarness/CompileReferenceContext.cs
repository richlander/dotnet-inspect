using System.Collections.Immutable;
using DotnetInspector.Artifacts;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Scoped Metadata and Roslyn views of a frozen set. Use only within the
/// CompileReferenceSet.Use callback; the source is never a compiler reference.
/// </summary>
public sealed class CompileReferenceContext : IAssemblyReferenceResolver, IAssemblyBindingPolicy
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
    public AssemblyBindingPolicyVersion Version => _set.BindingVersion;

    /// <summary>Origin-free compatibility lookup remains exact and Any-scope only.</summary>
    public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (scope != AssemblyResolutionScope.Any)
            return null;
        return _set.References
            .SingleOrDefault(reference => identity.IsEquivalentTo(reference.Image.Identity))?.Image.Assembly;
    }

    public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Origin is AssemblyBindingOrigin.RequestingAssembly origin)
        {
            ArtifactAcquisitionRegistration? registration = origin.Registration.ArtifactRegistration;
            bool ownsOrigin = registration is not null
                && (_set.PlatformBindings?.OwnsOrigin(registration)
                    ?? (ReferenceEquals(Source.Registration.ArtifactRegistration, registration)
                        || _set.References.Any(reference => ReferenceEquals(reference.Image.ArtifactRegistration, registration))));
            if (!FrozenPlatformBindings.IsSeed(origin.Lineage) || !ownsOrigin)
                return new(Version, AssemblyBindingSelection.Invalid(new(AssemblyBindingFailureKind.InvalidBindingOrigin)));
        }
        if (request.Scope == AssemblyResolutionScope.Platform && _set.PlatformBindings is { } platform)
            return new(Version, platform.Select(request));
        return new(Version, request.Target is AssemblyBindingTarget.AssemblyReference reference
            && Resolve(reference.Identity, request.Scope) is { } selected
                ? AssemblyBindingSelection.Found(selected)
                : AssemblyBindingSelection.CannotSelect(new(AssemblyBindingFailureKind.IdentityPolicyRequired)));
    }
}
