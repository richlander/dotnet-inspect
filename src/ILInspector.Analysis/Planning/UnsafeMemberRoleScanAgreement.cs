using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

internal enum UnsafeMemberRoleScanVerdictKind
{
    /// <summary>The census reads no body here, or fails it before the scan.</summary>
    NotApplicable,

    /// <summary>The scan left the body to the full rule.</summary>
    Deferred,

    /// <summary>The scan decided the body, and the full rule agrees.</summary>
    Agreed,

    /// <summary>The scan decided the body, and the full rule disagrees.</summary>
    Disagreed,
}

/// <summary>The role scan's verdict on one body.</summary>
internal readonly record struct UnsafeMemberRoleScanVerdict(
    UnsafeMemberRoleScanVerdictKind Kind,
    string? Detail)
{
    public static UnsafeMemberRoleScanVerdict NotApplicable =>
        new(UnsafeMemberRoleScanVerdictKind.NotApplicable, null);

    public static UnsafeMemberRoleScanVerdict Deferred =>
        new(UnsafeMemberRoleScanVerdictKind.Deferred, null);

    public static UnsafeMemberRoleScanVerdict Agreed =>
        new(UnsafeMemberRoleScanVerdictKind.Agreed, null);

    public static UnsafeMemberRoleScanVerdict Disagree(string detail) =>
        new(UnsafeMemberRoleScanVerdictKind.Disagreed, detail);
}

/// <summary>
/// The role scan's agreement with the full rule over one image: the bodies the
/// census reads, how many the scan decided, and each decided body the full
/// rule disagrees with.
/// </summary>
internal sealed record UnsafeMemberRoleScanAgreement(
    int Bodies,
    int Decided,
    ImmutableArray<string> Disagreements);

internal sealed class UnsafeMemberRoleScanAgreementBuilder
{
    int _bodies;
    int _decided;
    readonly ImmutableArray<string>.Builder _disagreements =
        ImmutableArray.CreateBuilder<string>();

    public void Add(int token, UnsafeMemberRoleScanVerdict verdict)
    {
        if (verdict.Kind == UnsafeMemberRoleScanVerdictKind.NotApplicable)
            return;
        _bodies++;
        if (verdict.Kind == UnsafeMemberRoleScanVerdictKind.Deferred)
            return;
        _decided++;
        if (verdict.Kind == UnsafeMemberRoleScanVerdictKind.Disagreed)
            _disagreements.Add($"0x{token:X8}: {verdict.Detail}");
    }

    public UnsafeMemberRoleScanAgreement Build() =>
        new(_bodies, _decided, _disagreements.ToImmutable());
}

/// <summary>
/// Visits every method definition with the census rule's layers and checks
/// each body the role scan decides against the full rule. It is the gate and
/// measurement for the scan, not a product path.
/// </summary>
internal sealed class UnsafeMemberRoleScanAgreementProducer
    : MethodDefinitionProducer<
        (int Token, UnsafeMemberRoleScanVerdict Verdict),
        UnsafeMemberRoleScanAgreementBuilder,
        UnsafeMemberRoleScanAgreement>
{
    UnsafeMemberRoleScanAgreementProducer()
        : base(
            "UnsafeMemberRoleScanAgreement",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body
                | MethodDefinitionLayers.Calls
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.SemanticIdentity)
    {
    }

    public static UnsafeMemberRoleScanAgreementProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    internal override (int Token, UnsafeMemberRoleScanVerdict Verdict) Visit(
        scoped MethodDefinitionView view) =>
        (view.Token, UnsafeMemberCensusProducer.CheckRoleScan(view));

    internal override UnsafeMemberRoleScanAgreementBuilder Seed() => new();

    internal override UnsafeMemberRoleScanAgreementBuilder Accumulate(
        UnsafeMemberRoleScanAgreementBuilder accumulator,
        (int Token, UnsafeMemberRoleScanVerdict Verdict) fact)
    {
        accumulator.Add(fact.Token, fact.Verdict);
        return accumulator;
    }

    internal override UnsafeMemberRoleScanAgreement Complete(
        UnsafeMemberRoleScanAgreementBuilder accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.Build();
}

/// <summary>Runs the role scan agreement over one image.</summary>
internal static class UnsafeMemberRoleScanAgreementAnalysis
{
    static readonly WorkDescription s_description =
        ProducerPlanner.Plan([new ProducerRequest(UnsafeMemberRoleScanAgreementProducer.Instance)])
            is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException(
                "The role scan agreement request must plan.");

    /// <summary>
    /// The agreement over every method definition of the image, or the
    /// failure that prevented it.
    /// </summary>
    public static UnsafeMemberRoleScanAgreement Measure(string path, byte[] image)
    {
        var operation = AssemblyAnalysisOperation<UnsafeMemberRoleScanAgreement>.Create(
            path,
            MethodDefinitionSourceRequest<UnsafeMemberRoleScanAgreement>.Create(
                s_description,
                UnsafeMemberRoleScanAgreementProducer.Instance,
                MethodDefinitionSourceBreadth.AllDefinitions));
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(image, writable: false));
        AssemblyAnalysisServiceResult<UnsafeMemberRoleScanAgreement> result =
            session.SnapshotOperation(
                operation,
                access => AssemblyAnalysisService.Instance.Execute(operation, access));
        if (result is not AssemblyAnalysisServiceResult<UnsafeMemberRoleScanAgreement>.Completed completed)
            throw new InvalidDataException($"The role scan agreement did not complete: {result}");

        ProducerResult<UnsafeMemberRoleScanAgreement> agreement =
            completed.Execution.ResultOf(UnsafeMemberRoleScanAgreementProducer.Instance);
        return agreement.HasValue && agreement.Value is { } value
            ? value
            : throw new InvalidDataException(
                "The role scan agreement failed because "
                + $"{agreement.Failure?.Unit ?? agreement.Critical?.Unit ?? "(unknown)"} could not be analyzed: "
                + (agreement.Failure?.Message ?? agreement.Critical?.Message ?? agreement.FailedPrerequisite));
    }
}
