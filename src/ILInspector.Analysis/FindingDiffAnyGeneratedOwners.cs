using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// The declared method that owns each compiler-generated method of one image,
/// through the exact lifted-owner and state-machine resolution Method Query
/// Source uses, and an iterator's by its certified kickoff. Finding Diff
/// Any's Type level reads it as a <see cref="FindingDiffAnyGeneratedOwner"/>
/// (<c>docs/design/finding-diff-any.md#type</c>).
/// </summary>
/// <remarks>
/// Resolution work is bounded per instance, so a caller creates one for each
/// Type it asks about. A method the resolution does not authenticate, or one
/// whose resolution exceeds that bound, has no owner.
/// </remarks>
public sealed class FindingDiffAnyGeneratedOwners
{
    readonly MetadataReader _reader;
    readonly MethodAnalysisLookup _lookup;

    /// <param name="image">The image whose generated methods are resolved.</param>
    /// <param name="path">The image's path or name; it names a module without an assembly row.</param>
    public FindingDiffAnyGeneratedOwners(PEReader image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        _reader = image.GetMetadataReader();
        _lookup = new MethodAnalysisLookup(path, _reader, image);
    }

    /// <summary>
    /// The declared method that owns <paramref name="generated"/>, or a nil
    /// handle when it is not generated code or no single owner is authenticated.
    /// </summary>
    public MethodDefinitionHandle Owner(MethodDefinitionHandle generated)
    {
        MethodDefinition definition = _reader.GetMethodDefinition(generated);
        TypeDefinitionHandle type = definition.GetDeclaringType();
        LibraryBodyPrimaryMetadataResolver resolver = _lookup.PrimaryMetadataResolver;
        MethodIdentity identity = resolver.CreateMethodIdentity(
            type,
            generated,
            definition,
            resolver.CreateScope(_reader.GetTypeDefinition(type), definition));
        ExactMethodDeclaredSource source;
        try
        {
            source = _lookup.ResolveDeclaredSource(generated, definition, identity);
        }
        catch (MethodDefinitionGeneratedExpansionBoundException)
        {
            return default;
        }
        if (!source.IsAuthenticated)
            return default;
        if (source.Owner is { } owner)
        {
            EntityHandle handle = MetadataTokens.EntityHandle(owner.MetadataToken);
            return handle.Kind == HandleKind.MethodDefinition ? (MethodDefinitionHandle)handle : default;
        }
        // Execution attribution leaves iterators out, since their bodies run
        // after the kickoff returns. Every method of a certified iterator state
        // machine is generated code of its kickoff, which a lifted kickoff
        // carries on to its own owner.
        if (!_lookup.DeclaredSources.StateMachineSourceResolver.TryGetCertifiedIteratorKickoff(type, out MethodDefinitionHandle kickoff))
            return default;
        string name = _reader.GetString(_reader.GetMethodDefinition(kickoff).Name);
        return CompilerGeneratedNames.IsLocalFunctionOrLambda(name) ? Owner(kickoff) : kickoff;
    }
}
