using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using InertText;

namespace DotnetInspector.Sections;

public enum LibraryReferenceInspectionFailureKind
{
    Unexpected,
}

public readonly record struct LibraryReferenceInspectionFailure(
    LibraryReferenceInspectionFailureKind Kind,
    InertString Detail);

public readonly record struct LibraryReferenceInspectionRejection(
    AssemblyReferenceRowsRejectionKind Kind,
    InertString Detail);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(LibraryReferenceInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(LibraryReferenceInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(LibraryReferenceInspectionOutcome.Failed),
    "failed")]
public abstract record LibraryReferenceInspectionOutcome
{
    private protected LibraryReferenceInspectionOutcome()
    {
    }

    public sealed record Available(
        ImmutableArray<AssemblyReferenceRow> References)
        : LibraryReferenceInspectionOutcome;

    public sealed record Rejected(
        LibraryReferenceInspectionRejection Reason)
        : LibraryReferenceInspectionOutcome;

    public sealed record Failed(
        LibraryReferenceInspectionFailure Reason)
        : LibraryReferenceInspectionOutcome;
}

/// <summary>
/// Produces one detached direct-reference outcome for CLI and Browser/Wasm.
/// </summary>
public static class LibraryReferenceInspection
{
    private static readonly InspectionShare Share =
        new InspectionShare.NonProjectable(
            "library-references/share",
            "Exact Library reference requests do not yet have a canonical Workspace Share projection.");

    public static InspectionEnvelope<LibraryReferenceInspectionOutcome> Execute(
        AssemblyReferencesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        LibraryReferenceInspectionOutcome content = result switch
        {
            AssemblyReferencesResult.Available available =>
                Available(
                    available.Identities.Select(
                        AssemblyReferencesQuery.ToRow)),
            AssemblyReferencesResult.Failed failed =>
                Failed(failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown assembly-reference result."),
        };
        return new(content, Share);
    }

    public static InspectionEnvelope<LibraryReferenceInspectionOutcome> Execute(
        AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        LibraryReferenceInspectionOutcome content = result switch
        {
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>
                .Available available =>
                    Available(available.Value),
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>
                .Rejected rejected =>
                    new LibraryReferenceInspectionOutcome.Rejected(
                        Rejection(
                            AssemblyContextReferencesQuery
                                .GetRowsRejection(rejected))),
            AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>
                .Failed failed =>
                    Failed(failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context reference result."),
        };
        return new(content, Share);
    }

    private static LibraryReferenceInspectionOutcome.Available Available(
        IEnumerable<AssemblyReferenceRow> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        return new(
        [
            .. references
                .OrderBy(
                    static reference => reference.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static reference => reference.Name,
                    StringComparer.Ordinal)
                .ThenBy(
                    static reference => reference.Version,
                    StringComparer.Ordinal)
                .ThenBy(
                    static reference => reference.Culture,
                    StringComparer.Ordinal)
                .ThenBy(
                    static reference => reference.PublicKeyToken,
                    StringComparer.Ordinal),
        ]);
    }

    private static LibraryReferenceInspectionOutcome.Failed Failed(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(
            new(
                LibraryReferenceInspectionFailureKind.Unexpected,
                new InertString(
                    TextPolicy.Field,
                    exception.Message)));
    }

    private static LibraryReferenceInspectionRejection Rejection(
        AssemblyReferenceRowsRejection rejection) =>
        new(
            rejection.Kind,
            new InertString(
                TextPolicy.Field,
                rejection.Detail));
}
