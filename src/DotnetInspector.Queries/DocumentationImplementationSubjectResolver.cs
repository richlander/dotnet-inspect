using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

public abstract record DocumentationImplementationSubjectResolution
{
    private protected DocumentationImplementationSubjectResolution()
    {
    }

    public sealed record Resolved(
        DocumentationImplementationSubjectReference Subject)
        : DocumentationImplementationSubjectResolution;

    public sealed record Unavailable
        : DocumentationImplementationSubjectResolution;

    public sealed record Ambiguous(int MatchCount)
        : DocumentationImplementationSubjectResolution;

    public sealed record Incomplete(ApiSurfaceExtractionBound Bound)
        : DocumentationImplementationSubjectResolution;
}

public static class DocumentationImplementationSubjectResolver
{
    public static DocumentationImplementationSubjectResolution Resolve(
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
            return new DocumentationImplementationSubjectResolution
                .Unavailable();
        }
        if (ReferenceEquals(library.ApiAssembly, implementation)
            && subject.MetadataToken is { } metadataToken)
        {
            return new DocumentationImplementationSubjectResolution
                .Resolved(
                    DocumentationImplementationSubjectReference
                        .FromApiSubject(subject));
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

    public static IDocumentationAuthoredSourceOperation
        CreateTerminalOperation(
            DocumentationAuthoredSourceOperationBinding binding,
            DocumentationImplementationSubjectResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution
            is DocumentationImplementationSubjectResolution.Resolved)
        {
            throw new ArgumentException(
                "A resolved implementation subject requires a SourceHouse operation.",
                nameof(resolution));
        }

        return new ImplementationResolutionOperation(
            binding,
            resolution);
    }

    private static DocumentationImplementationSubjectResolution Resolve(
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
            return new DocumentationImplementationSubjectResolution
                .Incomplete(exceeded.Bound);
        }

        ApiSurface surface =
            ((ApiSurfaceExtractionResult.Extracted)extraction).Surface;
        DocumentationImplementationSubjectReference? resolved = null;
        int matchCount = 0;
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

                matchCount++;
                resolved ??=
                    new DocumentationImplementationSubjectReference(
                        type.DefinitionName!,
                        ApiMemberIdentity.GetMemberAnchor(type, member),
                        metadataToken,
                        xmlIdentity);
            }
        }

        return matchCount switch
        {
            0 => new DocumentationImplementationSubjectResolution
                .Unavailable(),
            1 => new DocumentationImplementationSubjectResolution
                .Resolved(resolved!),
            _ => new DocumentationImplementationSubjectResolution
                .Ambiguous(matchCount),
        };
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

    private sealed class ImplementationResolutionOperation
        : IDocumentationAuthoredSourceOperation
    {
        private static readonly
            DocumentationAuthoredSourceOperationWorkCharge s_emptyWork =
                new(0, 0, 0, 0, DocumentationWork: null);

        private readonly DocumentationAuthoredSourceOperationBinding
            _binding;
        private readonly DocumentationImplementationSubjectResolution
            _resolution;
        private int _invoked;

        internal ImplementationResolutionOperation(
            DocumentationAuthoredSourceOperationBinding binding,
            DocumentationImplementationSubjectResolution resolution)
        {
            _binding = binding;
            _resolution = resolution;
        }

        public ValueTask<DocumentationAuthoredSourceOperationOutcome>
            InvokeAsync(
                DocumentationAuthoredSourceOperationInvocation invocation,
                LibraryOperationLease operationLease,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(invocation);
            ArgumentNullException.ThrowIfNull(operationLease);
            DocumentationAuthoredSourceOperationOutcome outcome;
            if (Interlocked.Exchange(ref _invoked, 1) != 0)
            {
                outcome = Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.AlreadyInvoked);
            }
            else if (!ReferenceEquals(invocation.Binding, _binding))
            {
                outcome = Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.BindingMismatch);
            }
            else if (!ReferenceEquals(
                    operationLease.Reference,
                    _binding.Library))
            {
                outcome = Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind
                        .LeaseReferenceMismatch);
            }
            else
            {
                operationLease.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                outcome = DateTimeOffset.UtcNow >= invocation.Deadline
                    ? Incomplete(
                        invocation,
                        DocumentationAuthoredIncompleteBoundary.Deadline,
                        "ImplementationResolutionDeadline")
                    : ResolutionOutcome(invocation);
                return ValueTask.FromResult(outcome);
            }

            operationLease.Dispose();
            return ValueTask.FromResult(outcome);
        }

        private DocumentationAuthoredSourceOperationOutcome
            ResolutionOutcome(
                DocumentationAuthoredSourceOperationInvocation invocation) =>
            _resolution switch
            {
                DocumentationImplementationSubjectResolution.Unavailable =>
                    new DocumentationAuthoredSourceOperationOutcome
                        .Unavailable(
                            invocation,
                            DocumentationAuthoredUnavailableKind
                                .DeclarationNotFound,
                            s_emptyWork,
                            Settlement(),
                            new("ImplementationSubjectUnavailable")),
                DocumentationImplementationSubjectResolution.Ambiguous
                    ambiguous =>
                    new DocumentationAuthoredSourceOperationOutcome
                        .Unavailable(
                            invocation,
                            DocumentationAuthoredUnavailableKind
                                .DeclarationAmbiguous,
                            s_emptyWork,
                            Settlement(),
                            new(
                                "ImplementationSubjectAmbiguous",
                                ambiguous.MatchCount.ToString(
                                    CultureInfo.InvariantCulture))),
                DocumentationImplementationSubjectResolution.Incomplete
                    incomplete =>
                    Incomplete(
                        invocation,
                        DocumentationAuthoredIncompleteBoundary
                            .ImplementationSurface,
                        "ImplementationSurfaceBoundExceeded",
                        incomplete.Bound.ToString()),
                _ => throw new InvalidOperationException(
                    "A terminal implementation resolution operation cannot contain a resolved subject."),
            };

        private static DocumentationAuthoredSourceOperationOutcome
            Rejected(
                DocumentationAuthoredSourceOperationInvocation invocation,
                DocumentationAuthoredRejectionKind rejection) =>
            new DocumentationAuthoredSourceOperationOutcome.Rejected(
                invocation,
                rejection,
                s_emptyWork,
                Settlement());

        private static DocumentationAuthoredSourceOperationOutcome
            Incomplete(
                DocumentationAuthoredSourceOperationInvocation invocation,
                DocumentationAuthoredIncompleteBoundary boundary,
                string code,
                string? detail = null) =>
            new DocumentationAuthoredSourceOperationOutcome.Incomplete(
                invocation,
                boundary,
                s_emptyWork,
                Settlement(),
                new(code, detail));

        private static DocumentationAuthoredLeaseSettlement Settlement() =>
            new(DocumentationAuthoredLeaseConsumer.Operation);
    }
}
