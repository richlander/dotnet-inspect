using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Proves that an async-sibling candidate is callable from the async source
/// under CLR member-access, receiver, friend-assembly, and nested-type rules.
/// Unknown or malformed access evidence fails closed.
/// <c>OptimizationOpportunities_PrivateAccessIsDirectionalAcrossNestedTypes</c>,
/// <c>OptimizationOpportunities_FriendAccessRequiresProvableReceiver</c>, and
/// <c>AsyncSiblingFriendAccess_StrongNamedGrantorRequiresFullFriendKey</c>
/// gate that behavior.
/// </summary>
internal sealed class LibraryBodyAsyncSiblingAccessibilityAnalyzer(
    MetadataReader reader,
    AssemblyReferenceIdentity assemblyIdentity,
    LibraryBodyAsyncSiblingDispatchAnalyzer dispatchAnalyzer)
{
    readonly MetadataReader _reader = reader;
    readonly AssemblyReferenceIdentity _assemblyIdentity =
        assemblyIdentity;
    readonly LibraryBodyAsyncSiblingDispatchAnalyzer
        _dispatchAnalyzer = dispatchAnalyzer;

    internal bool IsCallableAsyncSibling(
        MethodDefinition method,
        bool sameAssembly,
        TypeRef candidateDeclaringType,
        TypeRef synchronousDeclaringType,
        MethodIdentity asyncSource,
        MethodAttributes synchronousAttributes,
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType)
    {
        var access =
            method.Attributes & MethodAttributes.MemberAccessMask;
        bool sameType =
            LibraryBodyAsyncSiblingSignatureMatcher
                .SameTypeDefinition(
                    candidateDeclaringType,
                    asyncSource.DeclaringType);
        bool synchronousReceiverProven =
            LibraryBodyAsyncSiblingSignatureMatcher
                .SameTypeDefinition(
                    synchronousDeclaringType,
                    asyncSource.DeclaringType);
        MethodAttributes synchronousAccess =
            synchronousAttributes
                & MethodAttributes.MemberAccessMask;
        InternalAccessEvidence internalAccess =
            sameAssembly
                ? new(Granted: true, MayApply: true)
                : InternalAccessToSource(candidateReader);
        bool friendAccessMayApply =
            !sameAssembly
            && synchronousAccess
                == MethodAttributes.FamORAssem
            && internalAccess.MayApply;
        bool protectedReceiverProven = sameType
            || synchronousReceiverProven
            || (method.Attributes
                    & MethodAttributes.Static) != 0
            || synchronousAccess
                is MethodAttributes.Family
                    or MethodAttributes.FamANDAssem
            || !sameAssembly
                && synchronousAccess
                    == MethodAttributes.FamORAssem
                && !friendAccessMayApply;
        bool sourceDerivesFromCandidate = false;
        if (access is MethodAttributes.Family
            or MethodAttributes.FamANDAssem
            or MethodAttributes.FamORAssem)
        {
            sourceDerivesFromCandidate =
                _dispatchAnalyzer.SourceDerivesFrom(
                    asyncSource.MetadataToken,
                    candidateReader,
                    candidateType);
        }
        return access switch
        {
            MethodAttributes.Public => true,
            MethodAttributes.Assembly =>
                internalAccess.Granted,
            MethodAttributes.Family =>
                sourceDerivesFromCandidate
                && protectedReceiverProven,
            MethodAttributes.FamORAssem =>
                internalAccess.Granted
                || sourceDerivesFromCandidate
                    && protectedReceiverProven,
            MethodAttributes.Private => sameAssembly
                && SharesPrivateAccessDomain(
                    candidateReader,
                    candidateType,
                    asyncSource.MetadataToken),
            MethodAttributes.FamANDAssem =>
                sameAssembly
                && sourceDerivesFromCandidate
                && protectedReceiverProven,
            _ => false,
        };
    }

    readonly record struct InternalAccessEvidence(
        bool Granted,
        bool MayApply);

    InternalAccessEvidence InternalAccessToSource(
        MetadataReader candidateReader)
    {
        if (!candidateReader.IsAssembly
            || !_reader.IsAssembly)
        {
            return new(
                Granted: false,
                MayApply: true);
        }

        foreach (CustomAttributeHandle handle
            in candidateReader.GetAssemblyDefinition()
                .GetCustomAttributes())
        {
            try
            {
                CustomAttribute attribute =
                    candidateReader.GetCustomAttribute(handle);
                MemberRef constructor =
                    MemberResolver.ResolveMethod(
                        candidateReader,
                        attribute.Constructor,
                        GenericScope.Empty);
                if (!FrameworkIdentity.IsCoreLibraryType(
                        LibraryBodyAsyncSiblingSignatureMatcher
                            .DefinitionType(
                                constructor.DeclaringType),
                        "System.Runtime.CompilerServices",
                        "InternalsVisibleToAttribute"))
                {
                    continue;
                }
                if (constructor.Name != ".ctor"
                    || constructor.Kind
                        != MemberKind.Constructor
                    || !constructor.HasThis
                    || constructor.ParameterTypes.Length != 1
                    || !FrameworkIdentity.IsCoreLibraryType(
                        constructor.ParameterTypes[0],
                        "System",
                        "String"))
                {
                    return new(
                        Granted: false,
                        MayApply: true);
                }

                BlobReader value =
                    candidateReader.GetBlobReader(
                        attribute.Value);
                if (value.ReadUInt16() != 0x0001)
                {
                    return new(
                        Granted: false,
                        MayApply: true);
                }
                string? friend = value.ReadSerializedString();
                if (friend is null)
                {
                    return new(
                        Granted: false,
                        MayApply: true);
                }
                var friendIdentity = new AssemblyName(friend);
                if (string.Equals(
                        friendIdentity.Name,
                        _assemblyIdentity.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new(
                        Granted: FriendIdentityGrantsAccess(
                            candidateReader,
                            _reader,
                            friendIdentity,
                            friend),
                        MayApply: true);
                }
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                        .IsRecoverableMethodFailure(ex)
                    || ex is FileLoadException)
            {
                return new(
                    Granted: false,
                    MayApply: true);
            }
        }
        return new(
            Granted: false,
            MayApply: false);
    }

    internal static bool FriendIdentityGrantsAccess(
        MetadataReader grantingReader,
        MetadataReader sourceReader,
        AssemblyName friendIdentity,
        string friend)
    {
        if (!grantingReader.IsAssembly
            || !sourceReader.IsAssembly
            || !string.Equals(
                friendIdentity.Name,
                sourceReader.GetString(
                    sourceReader.GetAssemblyDefinition().Name),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] sourcePublicKey =
            sourceReader.GetBlobBytes(
                sourceReader.GetAssemblyDefinition().PublicKey);
        byte[] grantingPublicKey =
            grantingReader.GetBlobBytes(
                grantingReader.GetAssemblyDefinition().PublicKey);
        byte[] friendPublicKey =
            friendIdentity.GetPublicKey() ?? [];
        byte[] friendPublicKeyToken =
            friendIdentity.GetPublicKeyToken() ?? [];
        return friendIdentity.Version is null
            && string.IsNullOrEmpty(friendIdentity.CultureName)
            && friendIdentity.ContentType
                == AssemblyContentType.Default
            && HasSupportedFriendIdentityClauses(friend)
            && (friendPublicKey.Length != 0
                || friendPublicKeyToken.Length == 0)
            && (grantingPublicKey.Length == 0
                || friendPublicKey.Length != 0)
            && sourcePublicKey.AsSpan()
                .SequenceEqual(friendPublicKey);
    }

    static bool HasSupportedFriendIdentityClauses(string friend)
    {
        int separator = friend.IndexOf(',');
        if (separator < 0)
            return true;

        bool sawPublicKey = false;
        ReadOnlySpan<char> remaining =
            friend.AsSpan(separator + 1);
        while (!remaining.IsEmpty)
        {
            int next = remaining.IndexOf(',');
            ReadOnlySpan<char> clause = (
                next < 0
                    ? remaining
                    : remaining[..next]).Trim();
            int equals = clause.IndexOf('=');
            if (equals <= 0
                || sawPublicKey
                || !clause[..equals].Trim().Equals(
                    "PublicKey",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            sawPublicKey = true;
            remaining = next < 0
                ? []
                : remaining[(next + 1)..];
        }
        return true;
    }

    bool SharesPrivateAccessDomain(
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType,
        int sourceMethodToken)
    {
        if (!ReferenceEquals(candidateReader, _reader))
            return false;
        try
        {
            EntityHandle sourceHandle =
                MetadataTokens.EntityHandle(sourceMethodToken);
            if (sourceHandle.Kind
                != HandleKind.MethodDefinition)
            {
                return false;
            }
            TypeDefinitionHandle sourceType =
                _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)sourceHandle)
                    .GetDeclaringType();
            Span<TypeDefinitionHandle> rootToLeaf =
                stackalloc TypeDefinitionHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            return MetadataRelationshipTraversal
                    .TryWalkTypeDefinitionDeclaringChain(
                        _reader,
                        sourceType,
                        rootToLeaf,
                        out int consumedNodes,
                        out _,
                        out _)
                && rootToLeaf[..consumedNodes]
                    .Contains(candidateType);
        }
        catch (Exception ex)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(ex))
        {
            return false;
        }
    }

    internal static bool TryTopLevelType(
        MetadataReader reader,
        TypeDefinitionHandle type,
        out TypeDefinitionHandle topLevel)
    {
        Span<TypeDefinitionHandle> rootToLeaf =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeDefinitionDeclaringChain(
                    reader,
                    type,
                    rootToLeaf,
                    out int consumedNodes,
                    out _,
                    out _)
            || consumedNodes == 0)
        {
            topLevel = default;
            return false;
        }
        topLevel = rootToLeaf[0];
        return true;
    }
}
