using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

public sealed record DocumentationImplementationSubject(
    MetadataTypeDefinitionName TypeIdentity,
    MemberAnchor MemberIdentity,
    int MetadataToken);

public static class DocumentationImplementationSubjectResolver
{
    public static DocumentationImplementationSubject? Resolve(
        LibraryReference library,
        LibraryContentOwner owner,
        DocumentationSubjectReference subject,
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(bounds);
        if (!ReferenceEquals(subject.Library, library))
        {
            throw new ArgumentException(
                "The documentation subject belongs to another Library.",
                nameof(subject));
        }
        if (subject.MemberIdentity is null
            || library.ImplementationAssembly is not { } implementation)
        {
            return null;
        }
        if (ReferenceEquals(library.ApiAssembly, implementation)
            && subject.MetadataToken is { } metadataToken)
        {
            return new(
                subject.TypeIdentity,
                subject.MemberIdentity,
                metadataToken);
        }

        using LibraryOperationLease operation =
            IssueOperation(library, owner);
        return operation.Snapshot(
            implementation,
            new ResolutionState(
                implementation,
                subject,
                scope,
                bounds),
            static (view, state, token) =>
                view.UseReadStream(
                    content => Resolve(
                        content,
                        state.Implementation,
                        state,
                        token)),
            cancellationToken);
    }

    private static DocumentationImplementationSubject? Resolve(
        Stream content,
        LibraryContentReference implementation,
        ResolutionState state,
        CancellationToken cancellationToken)
    {
        using var peReader = new PEReader(content);
        if (!MetadataFormatAdmission.AdmitImage(peReader))
        {
            throw new InvalidOperationException(
                "The selected Library implementation is not a managed assembly.");
        }

        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        if (!reader.IsAssembly)
        {
            throw new InvalidOperationException(
                "The selected Library implementation is a managed module.");
        }

        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
        if (implementation.AssemblyIdentity is not { } expected
            || !identity.IsEquivalentTo(expected.Identity))
        {
            throw new InvalidOperationException(
                "The selected Library implementation identity changed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        ApiSurfaceExtractionResult extraction =
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                state.Scope,
                state.Bounds);
        if (extraction
            is ApiSurfaceExtractionResult.Exceeded exceeded)
        {
            throw new InvalidOperationException(
                "The selected Library implementation surface exceeded "
                    + $"the {exceeded.Bound} bound.");
        }

        ApiSurface surface =
            ((ApiSurfaceExtractionResult.Extracted)extraction).Surface;
        DocumentationImplementationSubject? resolved = null;
        foreach (ApiType type in surface.Types)
        {
            if (type.DefinitionName != state.Subject.TypeIdentity)
                continue;

            foreach (ApiMember member in type.Members)
            {
                if (member.DeclaringTypeDefinitionName is not null
                    || member.MetadataToken is not { } metadataToken
                    || MetadataTokens.EntityHandle(metadataToken).Kind
                        != HandleKind.MethodDefinition
                    || !ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                        type,
                        member,
                        out XmlDocMemberIdentity xmlIdentity)
                    || xmlIdentity != state.Subject.CompiledXmlIdentity)
                {
                    continue;
                }

                var candidate =
                    new DocumentationImplementationSubject(
                        type.DefinitionName!,
                        ApiMemberIdentity.GetMemberAnchor(type, member),
                        metadataToken);
                if (resolved is not null)
                {
                    throw new InvalidOperationException(
                        "The selected Library implementation has multiple "
                            + $"subjects '{xmlIdentity.Value}'.");
                }

                resolved = candidate;
            }
        }

        return resolved;
    }

    private static LibraryOperationLease IssueOperation(
        LibraryReference library,
        LibraryContentOwner owner) =>
        owner.IssueOperationLease(library) switch
        {
            LibraryOperationLeaseIssueOutcome.Issued issued =>
                issued.Lease,
            LibraryOperationLeaseIssueOutcome outcome =>
                throw new InvalidOperationException(
                    "The selected Library rejected implementation "
                        + $"inspection ({outcome.GetType().Name})."),
        };

    private sealed record ResolutionState(
        LibraryContentReference Implementation,
        DocumentationSubjectReference Subject,
        ApiSurfaceExtractionScope Scope,
        ApiSurfaceExtractionBounds Bounds);
}
