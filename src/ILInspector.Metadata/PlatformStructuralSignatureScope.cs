using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal sealed class PlatformStructuralSignatureScope
{
    readonly MetadataReader _reader;
    readonly AssemblyReferenceProjectionCache _referenceProjection;
    readonly Action<int>? _beforeDecodeWork;
    readonly Dictionary<AssemblyReferenceHandle, bool> _trustedReferences = [];
    bool? _currentAssemblyIsPlatform;

    internal PlatformStructuralSignatureScope(
        MetadataReader reader,
        AssemblyReferenceProjectionCache? referenceProjection = null,
        Action<int>? beforeDecodeWork = null)
    {
        _reader = reader;
        _referenceProjection =
            referenceProjection
            ?? new AssemblyReferenceProjectionCache(reader);
        _beforeDecodeWork = beforeDecodeWork;
    }

    internal bool IsTrustedPlatformType(
        EntityHandle handle,
        bool currentAssemblyHasPlatformIdentityTrust = false)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                IsTrustedCurrentAssembly(
                    currentAssemblyHasPlatformIdentityTrust),
            HandleKind.TypeReference =>
                IsTrustedPlatformReference(
                    (TypeReferenceHandle)handle),
            _ => false,
        };

    bool IsTrustedCurrentAssembly(
        bool currentAssemblyHasPlatformIdentityTrust)
    {
        if (!currentAssemblyHasPlatformIdentityTrust
            || !_reader.IsAssembly)
        {
            return false;
        }

        if (_currentAssemblyIsPlatform is { } cached)
            return cached;

        var definition = _reader.GetAssemblyDefinition();
        _beforeDecodeWork?.Invoke(
            checked(
                _reader.GetBlobReader(definition.Name).Length
                + _reader.GetBlobReader(definition.Culture).Length
                + _reader.GetBlobReader(definition.PublicKey).Length));
        bool trusted = PlatformKeys.IsPlatform(
            AssemblyReferenceIdentity
                .FromAssemblyDefinition(_reader)
                .PublicKeyToken);
        _currentAssemblyIsPlatform = trusted;
        return trusted;
    }

    bool IsTrustedPlatformReference(
        TypeReferenceHandle handle)
    {
        Span<TypeReferenceHandle> chain =
            stackalloc TypeReferenceHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    _reader,
                    handle,
                    chain,
                    out _,
                    out EntityHandle terminal,
                    out _)
            || terminal.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        var referenceHandle =
            (AssemblyReferenceHandle)terminal;
        if (_trustedReferences.TryGetValue(
                referenceHandle,
                out bool cached))
        {
            return cached;
        }

        var reference =
            _reader.GetAssemblyReference(referenceHandle);
        _beforeDecodeWork?.Invoke(
            checked(
                _reader.GetBlobReader(reference.Name).Length
                + _reader.GetBlobReader(reference.Culture).Length
                + _reader.GetBlobReader(
                    reference.PublicKeyOrToken).Length));
        bool trusted = PlatformKeys.IsPlatform(
            AssemblyReferenceIdentity.From(
                referenceHandle,
                _referenceProjection)
            .PublicKeyToken);
        _trustedReferences.Add(referenceHandle, trusted);
        return trusted;
    }
}
