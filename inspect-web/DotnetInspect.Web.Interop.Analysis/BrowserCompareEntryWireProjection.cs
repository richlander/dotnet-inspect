using System.Runtime.Versioning;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// Lowers the public managed Compare entry result without inspecting the
/// Registry-private execution target.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserCompareEntryWireProjection
{
    internal static BrowserCompareEntryResult Project(
        CompareFacetEntryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        BrowserCompareEntry entry = Project(result.Request);
        return result switch
        {
            CompareFacetEntryResult.Available =>
                new(
                    1,
                    BrowserCompareEntryResultKind.Available,
                    entry,
                    Unavailable: null,
                    Failure: null),
            CompareFacetEntryResult.Unavailable unavailable =>
                new(
                    1,
                    BrowserCompareEntryResultKind.Unavailable,
                    entry,
                    new BrowserCompareEntryUnavailable(
                        Project(unavailable.Reason.Kind),
                        unavailable.Reason.Message),
                    Failure: null),
            CompareFacetEntryResult.Failed failed =>
                new(
                    1,
                    BrowserCompareEntryResultKind.Failed,
                    entry,
                    Unavailable: null,
                    new BrowserCompareEntryFailure(failed.Message)),
            _ => throw new InvalidOperationException(
                "Unknown Compare entry result."),
        };
    }

    static BrowserCompareEntry Project(
        CompareFacetEntryRequest request) =>
        new(
            request.Descriptor.Id.Value,
            request.Descriptor.Title,
            Project(request.Subject, request.Package.Descriptor));

    static BrowserCompareSubject Project(
        StructuralSubjectIdentity subject,
        WorkspacePackageDescriptor package)
    {
        StructuralSubjectIdentity.LibrarySubject? library =
            subject switch
            {
                StructuralSubjectIdentity.LibrarySubject value => value,
                StructuralSubjectIdentity.TypeSubject value =>
                    value.Library,
                StructuralSubjectIdentity.MemberSubject value =>
                    value.DeclaringType.Library,
                StructuralSubjectIdentity.AllLibrariesSubject => null,
                _ => throw new InvalidOperationException(
                    "Compare entry has an unsupported structural subject."),
            };
        MetadataTypeDefinitionName? type =
            subject switch
            {
                StructuralSubjectIdentity.TypeSubject value =>
                    value.Identity.Type,
                StructuralSubjectIdentity.MemberSubject value =>
                    value.Identity.DeclaringType,
                _ => null,
            };
        MemberAnchor? member =
            subject is StructuralSubjectIdentity.MemberSubject valueMember
                ? valueMember.Identity.Member
                : null;

        return new BrowserCompareSubject(
            Project(subject.Kind),
            new BrowserComparePackageSubject(
                package.PackageId,
                package.PackageVersion,
                package.TargetFramework,
                package.RuntimeIdentifier),
            new BrowserCompareLibrarySubject(
                library is null,
                library is null
                    ? null
                    : Project(library.Identity.Assembly)),
            type is null
                ? null
                : new BrowserCompareTypeSubject(
                    type.Namespace,
                    [.. type.Segments]),
            member is null
                ? null
                : new BrowserCompareMemberSubject(
                    member.StableSelector,
                    member.CanonicalSignature,
                    member.Fingerprint,
                    member.TypeFullName,
                    member.MemberName));
    }

    static BrowserCompareAssemblyIdentity Project(
        AssemblyReferenceIdentity assembly) =>
        new(
            assembly.Name,
            assembly.Version?.ToString(),
            assembly.Culture,
            assembly.PublicKeyToken);

    static BrowserCompareSubjectKind Project(
        StructuralSubjectKind kind) =>
        kind switch
        {
            StructuralSubjectKind.Library =>
                BrowserCompareSubjectKind.Library,
            StructuralSubjectKind.Type =>
                BrowserCompareSubjectKind.Type,
            StructuralSubjectKind.Member =>
                BrowserCompareSubjectKind.Member,
            _ => throw new InvalidOperationException(
                "Compare entry has an unsupported structural subject kind."),
        };

    static BrowserCompareEntryUnavailabilityKind Project(
        ViewFacetUnavailabilityKind kind) =>
        kind switch
        {
            ViewFacetUnavailabilityKind.CapabilityAbsent =>
                BrowserCompareEntryUnavailabilityKind.CapabilityAbsent,
            ViewFacetUnavailabilityKind.Retired =>
                BrowserCompareEntryUnavailabilityKind.Retired,
            _ => throw new InvalidOperationException(
                "Unknown Compare entry unavailability kind."),
        };
}
