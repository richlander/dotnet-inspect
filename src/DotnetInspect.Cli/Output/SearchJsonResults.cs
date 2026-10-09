using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;

namespace DotnetInspect.Cli.Output;

internal sealed record ExtensionMethodJsonResult(
    string Method,
    string Class,
    string ExtendedType,
    string Library,
    string? Signature,
    List<string>? Signatures,
    int? Overloads,
    string Kind,
    string? Source,
    string? SourceVersion,
    string? ReachablePath,
    string? ReachableFromType)
{
    internal static ExtensionMethodJsonResult From(ExtensionMethodResult result) => new(
        result.MethodName,
        result.ExtensionClass,
        result.ExtendedType,
        result.Assembly,
        result.Signature,
        result.Signatures,
        result.Overloads,
        result.Kind,
        result.Source,
        result.SourceVersion,
        result.ReachablePath,
        result.ReachableFromType);
}

internal sealed record TypeHierarchyRelationJsonResult(
    string Type,
    string Library,
    string Source)
{
    internal static TypeHierarchyRelationJsonResult From(
        TypeHierarchyRelationCandidate candidate) =>
        new(candidate.Type, candidate.Library, candidate.Source);
}

internal sealed record TypeHierarchyRelationsJsonResult(
    List<TypeHierarchyRelationJsonResult>? Implementers,
    List<TypeHierarchyRelationJsonResult>? DerivedTypes)
{
    internal static TypeHierarchyRelationsJsonResult From(
        TypeHierarchyRelationsInspection inspection,
        RowWindow? rows) =>
        new(
            inspection.Implementers is { } implementers
                ? [.. RowWindow.Apply(
                    rows,
                    implementers.Candidates).Select(
                    TypeHierarchyRelationJsonResult.From)]
                : null,
            inspection.DerivedTypes is { } derivedTypes
                ? [.. RowWindow.Apply(
                    rows,
                    derivedTypes.Candidates).Select(
                    TypeHierarchyRelationJsonResult.From)]
                : null);
}
