using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

internal readonly record struct MethodClassificationOperationPredicate;

internal readonly record struct MethodClassificationMethodRow;

internal sealed record MethodClassificationOperationPlan;

internal sealed class MethodClassificationOperationVocabulary
    : PortableQueryVocabulary<
        MethodClassificationOperationPredicate,
        MethodClassificationOperationPlan>
{
    public override string Identity =>
        MethodClassificationQuerySpace.OperationVocabularyIdentity;

    public override IReadOnlyList<string> RequiredDimensions => [];

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<
            MethodClassificationOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<
            MethodClassificationOperationPredicate>? declaration)
    {
        declaration = null;
        return false;
    }

    public override bool AdmitsStageKind(RowSelectionStageKind kind) => false;

    public override bool TryGetNamedOrder(
        string reference,
        out PortableQueryOrderPurpose purpose)
    {
        purpose = default;
        return false;
    }

    public override bool IsOrderable(string key) => false;

    public override bool CollapsesDuplicateBindings => true;

    public override bool AreTermsCompatible(
        PortableQueryResolvedTerm<
            MethodClassificationOperationPredicate> first,
        PortableQueryResolvedTerm<
            MethodClassificationOperationPredicate> second) =>
        true;

    public override MethodClassificationOperationPlan CreatePlan(
        PortableQueryResolvedIntent<
            MethodClassificationOperationPredicate> resolved) =>
        new();
}

/// <summary>
/// QuerySpace declarations for independently closed Method Classification
/// requests over one managed-assembly resource.
/// </summary>
internal static class MethodClassificationQuerySpace
{
    internal const string OperationVocabularyIdentity =
        "method-classification/operation/v1";

    const string PInvokeMethodsRowSet =
        "pinvoke-methods";

    const string AsyncMethodsRowSet =
        "async-methods";

    const string PointerSignatureMethodsRowSet =
        "pointer-signature-methods";

    const string RuntimeAsyncMethodsRowSet =
        "runtime-async-methods";

    const string CompilerAsyncMethodsRowSet =
        "compiler-async-methods";

    const string ClassifiedMethodsRowScopeIdentity =
        "method-classification/classified-methods/v1";

    const string ClassifiedMethodsRowVocabularyIdentity =
        "method-classification/classified-method-rows/v1";

    static readonly string[] ClassifiedMethodRowSets =
    [
        PInvokeMethodsRowSet,
        AsyncMethodsRowSet,
        PointerSignatureMethodsRowSet,
        RuntimeAsyncMethodsRowSet,
        CompilerAsyncMethodsRowSet,
    ];

    const string RowsResultContract =
        "method-classification/rows/v1";

    const string CountResultContract =
        "method-classification/count/v1";

    const string ExistsResultContract =
        "method-classification/exists/v1";

    static readonly QueryOperationDefinition<
        MethodClassificationOperationPredicate,
        MethodClassificationOperationPlan> OperationDefinition =
        QueryOperationDefinition<
            MethodClassificationOperationPredicate,
            MethodClassificationOperationPlan>.Create(
                "method-classification",
                new MethodClassificationOperationVocabulary(),
                ["managed-assembly"],
                ["method"],
                ClassifiedMethodRowSets,
                [],
                [],
                [
                    new(
                        "default",
                        [],
                        []),
                ]);

    static readonly QueryOperationRoute<
        MethodClassificationOperationPredicate,
        MethodClassificationOperationPlan> Route =
        QueryOperationRoute<
            MethodClassificationOperationPredicate,
            MethodClassificationOperationPlan>.Create(
                "method-classification/default",
                OperationDefinition,
                "managed-assembly",
                "method",
                ClassifiedMethodRowSets,
                "default",
                [],
                []);

    static readonly QuerySpaceRowScopeBinding<
        MethodClassificationMethodRow> ClassifiedMethodsRowScope =
            new(
                new QuerySpaceRowScopeDescriptor(
                    ClassifiedMethodsRowScopeIdentity,
                    ClassifiedMethodsRowVocabularyIdentity,
                    ClassifiedMethodRowSets,
                    [],
                    [],
                    []),
                RowQueryVocabulary<
                    MethodClassificationMethodRow>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [],
                        []));

    static QuerySpaceBinding QuerySpace { get; } =
        QuerySpaceBinding.Create(
            "method-classification/query-space/v1",
            Route,
            [ClassifiedMethodsRowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
                QuerySpaceTerminalRequirement.Exists,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    RowsResultContract),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContract),
                new(
                    QuerySpaceTerminalRequirement.Exists,
                    ExistsResultContract),
            ]);

    internal static QuerySpaceRequest CreateRequest(
        MethodClassificationAnalyzer analyzer,
        ClassificationExecution execution) =>
        QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Empty,
            [RowSetFor(analyzer)],
            [],
            execution.Closing switch
            {
                ClassificationClosing.Rows or ClassificationClosing.Head =>
                    QuerySpaceTerminalRequirement.Rows,
                ClassificationClosing.Count =>
                    QuerySpaceTerminalRequirement.Count,
                ClassificationClosing.Exists =>
                    QuerySpaceTerminalRequirement.Exists,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(execution)),
            });

    static string RowSetFor(MethodClassificationAnalyzer analyzer) =>
        analyzer switch
        {
            MethodClassificationAnalyzer.PInvoke =>
                PInvokeMethodsRowSet,
            MethodClassificationAnalyzer.Async =>
                AsyncMethodsRowSet,
            MethodClassificationAnalyzer.PointerSignature =>
                PointerSignatureMethodsRowSet,
            MethodClassificationAnalyzer.RuntimeAsync =>
                RuntimeAsyncMethodsRowSet,
            MethodClassificationAnalyzer.CompilerAsync =>
                CompilerAsyncMethodsRowSet,
            _ => throw new ArgumentOutOfRangeException(nameof(analyzer)),
        };
}
