using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Resolves exact compiler documentation identities through one realized
/// Library's owner-issued API correspondence.
/// </summary>
public static class CompiledDocumentationSubjectResolver
{
    public static IReadOnlyDictionary<
        string,
        DocumentationSubjectReference> Resolve(
        LibraryReference library,
        LibraryContentOwner owner,
        IReadOnlyCollection<string> documentationIds,
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds,
        CancellationToken cancellationToken = default)
        => ResolveCore(
            library,
            owner,
            documentationIds,
            scope,
            bounds,
            requireAllSubjects: true,
            cancellationToken);

    /// <summary>
    /// Resolves the requested IDs represented by the selected Library surface
    /// and omits IDs that belong only to another view.
    /// </summary>
    public static IReadOnlyDictionary<
        string,
        DocumentationSubjectReference> ResolveAvailable(
        LibraryReference library,
        LibraryContentOwner owner,
        IReadOnlyCollection<string> documentationIds,
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds,
        CancellationToken cancellationToken = default)
        => ResolveCore(
            library,
            owner,
            documentationIds,
            scope,
            bounds,
            requireAllSubjects: false,
            cancellationToken);

    private static IReadOnlyDictionary<
        string,
        DocumentationSubjectReference> ResolveCore(
        LibraryReference library,
        LibraryContentOwner owner,
        IReadOnlyCollection<string> documentationIds,
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds,
        bool requireAllSubjects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(documentationIds);
        ArgumentNullException.ThrowIfNull(bounds);
        if (documentationIds.Count == 0)
        {
            return new Dictionary<
                string,
                DocumentationSubjectReference>();
        }

        string[] requestedIds = [.. documentationIds];
        if (requestedIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Documentation IDs cannot be empty.",
                nameof(documentationIds));
        }
        if (requestedIds.Distinct(StringComparer.Ordinal).Count()
            != requestedIds.Length)
        {
            throw new ArgumentException(
                "Documentation IDs must be unique.",
                nameof(documentationIds));
        }

        using LibraryOperationLease operation =
            IssueOperation(library, owner);
        LibraryApiSurfaceInspectionOutcome inspection =
            LibraryApiSurfaceInspection.Execute(
                new(library, scope, bounds),
                operation,
                cancellationToken);
        if (inspection
            is not LibraryApiSurfaceInspectionOutcome.Completed completed)
        {
            throw new InvalidOperationException(
                $"The selected Library API surface could not be inspected ({inspection}).");
        }

        var requested =
            new HashSet<string>(requestedIds, StringComparer.Ordinal);
        var subjects =
            new Dictionary<string, DocumentationSubjectReference>(
                requested.Count,
                StringComparer.Ordinal);
        foreach (ApiType type in completed.Correspondence.Surface.Types)
        {
            if (ApiMemberIdentity.TryGetXmlDocTypeIdentity(
                    type,
                    out XmlDocMemberIdentity typeIdentity)
                && requested.Contains(typeIdentity.Value))
            {
                AddSubject(
                    typeIdentity.Value,
                    DocumentationSubjectReference.ForType(
                        completed.Correspondence,
                        type));
            }
            foreach (ApiMember member in type.Members)
            {
                if (member.DeclaringTypeDefinitionName is not null
                    || !ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                        type,
                        member,
                        out XmlDocMemberIdentity memberIdentity)
                    || !requested.Contains(memberIdentity.Value))
                {
                    continue;
                }
                AddSubject(
                    memberIdentity.Value,
                    DocumentationSubjectReference.ForMember(
                        completed.Correspondence,
                        type,
                        member));
            }
        }

        if (requireAllSubjects)
        {
            string? missing =
                requested.FirstOrDefault(id => !subjects.ContainsKey(id));
            if (missing is not null)
            {
                throw new InvalidOperationException(
                    $"The selected Library has no subject '{missing}'.");
            }
        }
        return subjects;

        void AddSubject(
            string documentationId,
            DocumentationSubjectReference subject)
        {
            if (!subjects.TryAdd(documentationId, subject))
            {
                throw new InvalidOperationException(
                    $"The selected Library has multiple subjects '{documentationId}'.");
            }
        }
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
                    "The selected Library rejected documentation "
                        + $"access ({outcome.GetType().Name})."),
        };
}
