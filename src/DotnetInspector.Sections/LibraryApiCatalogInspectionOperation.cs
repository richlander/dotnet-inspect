using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<LibraryApiCatalogInspectionOutcome>))]
public enum LibraryApiCatalogInspectionOutcome
{
    Available,
    Unavailable,
}

public sealed record LibraryApiCatalogInspectionFailure(
    string Operation,
    string Kind,
    string Detail);

public sealed record LibraryApiCatalogInspectionResult
{
    public LibraryApiCatalogInspectionResult(
        LibraryApiCatalogInspectionOutcome outcome,
        int typeCount,
        ImmutableArray<LibraryApiCatalogInspectionFailure> failures)
    {
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (typeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(typeCount));
        if (failures.IsDefault)
        {
            throw new ArgumentException(
                "Library API catalog failures must be initialized.",
                nameof(failures));
        }
        if (outcome == LibraryApiCatalogInspectionOutcome.Unavailable
            && failures.Length == 0)
        {
            throw new ArgumentException(
                "An unavailable Library API catalog requires failure evidence.",
                nameof(failures));
        }

        Outcome = outcome;
        TypeCount = typeCount;
        Failures = failures;
    }

    public LibraryApiCatalogInspectionOutcome Outcome { get; }

    public int TypeCount { get; }

    public ImmutableArray<LibraryApiCatalogInspectionFailure> Failures
    {
        get;
    }

    public bool IsAvailable =>
        Outcome == LibraryApiCatalogInspectionOutcome.Available;
}

public sealed record LibraryApiCatalogTypeTarget(
    ApiType Type,
    ResolvedAssemblyReference Assembly,
    AssemblyBindingOccurrence Occurrence);

/// <summary>
/// Live resolved Library API catalog valid while the paired Workspace remains
/// admitted.
/// </summary>
public sealed record LibraryApiCatalogLiveTarget(
    ApiSurface Surface,
    ResolvedAssemblyReference RootAssembly,
    IAssemblyBindingPolicy BindingPolicy,
    IReadOnlyList<LibraryApiCatalogTypeTarget> Types);

/// <summary>
/// Executes one resolved Library API catalog inspection, including forwarded
/// Type definitions and their retained source and binding evidence.
/// </summary>
public static class LibraryApiCatalogInspectionOperation
{
    public static async Task<TResult> ExecuteWithLiveCatalogAsync<TResult>(
        ResolvedAssemblyReference assembly,
        IAssemblyBindingPolicy bindingPolicy,
        ApiSurfaceScope scope,
        Func<
            InspectionEnvelope<LibraryApiCatalogInspectionResult>,
            LibraryApiCatalogLiveTarget?,
            CancellationToken,
            ValueTask<TResult>> use,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(use);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        cancellationToken.ThrowIfCancellationRequested();
        await using var workspace = new InspectionWorkspace();
        ResolvedAssemblyApiCatalogExecution execution =
            ResolvedAssemblyApiCatalogQuery.Execute(
                workspace,
                assembly,
                bindingPolicy,
                scope);
        cancellationToken.ThrowIfCancellationRequested();

        LibraryApiCatalogLiveTarget? target = execution.Catalog is { } catalog
            ? new(
                catalog,
                assembly,
                bindingPolicy,
                [
                    .. execution.TypeTargets.Select(
                        static type => new LibraryApiCatalogTypeTarget(
                            type.Type,
                            type.Assembly,
                            type.Occurrence)),
                ])
            : null;
        LibraryApiCatalogInspectionResult result =
            target is null
                ? new(
                    LibraryApiCatalogInspectionOutcome.Unavailable,
                    typeCount: 0,
                    [
                        new(
                            "project Library API catalog",
                            "Unavailable",
                            execution.UnavailableDetail
                            ?? "The selected Library API could not be read."),
                    ])
                : new(
                    LibraryApiCatalogInspectionOutcome.Available,
                    target.Surface.Types.Count,
                    [
                        .. target.Surface.InspectionFailures.Select(
                            static failure =>
                                new LibraryApiCatalogInspectionFailure(
                                    failure.Operation,
                                    failure.Kind,
                                    failure.Detail)),
                    ]);
        var envelope =
            new InspectionEnvelope<LibraryApiCatalogInspectionResult>(
                result,
                new InspectionShare.NonProjectable(
                    "resolved-library-api-catalog/share",
                    "A complete portable Workspace scenario was not supplied."),
                Diagnostics(result));
        return await use(
                envelope,
                target,
                cancellationToken)
            .ConfigureAwait(false);
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        LibraryApiCatalogInspectionResult result) =>
    [
        .. result.Failures.Select(failure =>
            new InspectionDiagnostic(
                "library-api-catalog.incomplete",
                result.IsAvailable
                    ? InspectionDiagnosticSeverity.Warning
                    : InspectionDiagnosticSeverity.Error,
                $"{failure.Operation}: {failure.Kind}: {failure.Detail}")),
    ];
}
