using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;
using Inspector.Findings;

namespace ILInspector.Research;

public enum CallSiteEvidenceKind
{
    ExceptionConstruction,
    Localloc,
    Calli,
}

/// <summary>
/// One typed physical location supporting a caller-side relationship fact.
/// </summary>
public sealed record CallSiteEvidenceCoordinate
{
    public CallSiteEvidenceCoordinate(
        MethodIdentity method,
        int ilOffset,
        CallSiteEvidenceKind kind)
    {
        Location = ResearchEvidenceLocation.ForInstruction(
            method,
            ilOffset);
        Kind = kind;
    }

    public ResearchEvidenceLocation Location { get; }
    public CallSiteEvidenceKind Kind { get; }
}

public sealed record CallSiteSemanticsEvidence(
    MethodIdentity Callee,
    ImmutableArray<string> ExceptionTypes,
    ImmutableArray<CallSiteEvidenceCoordinate> Coordinates)
{
    public string Detail =>
        $"may-throw {string.Join("/", ExceptionTypes)}";

    public static bool TryCreate(
        DirectCall call,
        MemberProjectionAnalysisInput analysis,
        out CallSiteSemanticsEvidence? evidence)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(analysis);

        if (!TryResolveCallee(
                call,
                analysis,
                out MethodIdentity? callee)
            || callee is null)
        {
            evidence = null;
            return false;
        }

        ImmutableArray<string> exceptionTypes =
        [
            .. analysis.Signals
                .GetValueOrDefault(
                    callee.MetadataToken,
                    MethodSignals.None)
                .ExceptionTypes
                .Where(type =>
                    !IsArgumentValidationException(type))
                .Take(2),
        ];
        if (exceptionTypes.IsDefaultOrEmpty)
        {
            evidence = null;
            return false;
        }

        ImmutableArray<CallSiteEvidenceCoordinate> coordinates =
        [
            .. analysis.CallsByEvidenceMethod
                .GetValueOrDefault(callee.MetadataToken, [])
                .Where(candidate =>
                    candidate.Kind == CallKind.NewObject
                    && exceptionTypes.Contains(
                        ConstructedTypeName(
                            candidate.Callee.DeclaringType),
                        StringComparer.Ordinal))
                .Select(candidate =>
                    new CallSiteEvidenceCoordinate(
                        candidate.EvidenceMethod,
                        candidate.ILOffset,
                        CallSiteEvidenceKind.ExceptionConstruction))
                .Distinct()
                .OrderBy(coordinate =>
                    coordinate.Location.Method.ModuleVersionId)
                .ThenBy(coordinate =>
                    coordinate.Location.Method.MetadataToken)
                .ThenBy(coordinate =>
                    coordinate.Location.ILOffset),
        ];

        evidence = new CallSiteSemanticsEvidence(
            callee,
            exceptionTypes,
            coordinates);
        return true;
    }

    internal static bool TryResolveCallee(
        DirectCall call,
        MemberProjectionAnalysisInput analysis,
        out MethodIdentity? callee)
    {
        int token = call.CalleeDefinitionToken;
        callee = analysis.Signals.ContainsKey(token)
            || analysis.LeverageByToken.ContainsKey(token)
                ? analysis.CallGraph.DeclaredMethods.FirstOrDefault(
                    method => method.MetadataToken == token)
                : null;
        return callee is not null;
    }

    static bool IsArgumentValidationException(string type)
        => type is "ArgumentException"
            or "ArgumentNullException"
            or "ArgumentOutOfRangeException";

    static string ConstructedTypeName(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } element
                ? element.Name
                : type.Name;
}

public sealed record CallSiteSafetyEvidence(
    MethodIdentity Callee,
    ImmutableArray<CallSiteEvidenceCoordinate> Coordinates)
{
    public string Detail
    {
        get
        {
            string[] details =
            [
                .. Coordinates
            .Select(static coordinate =>
                coordinate.Kind switch
                {
                    CallSiteEvidenceKind.Localloc => "stackalloc",
                    CallSiteEvidenceKind.Calli => "calli",
                    _ => throw new InvalidOperationException(
                        "Unsupported callee safety evidence kind "
                            + $"'{coordinate.Kind}'."),
                })
            .Distinct(StringComparer.Ordinal),
            ];
            return details.Length == 0
                ? "unsafe"
                : $"unsafe; {string.Join("; ", details)}";
        }
    }

    public static bool TryCreate(
        DirectCall call,
        MemberProjectionAnalysisInput analysis,
        out CallSiteSafetyEvidence? evidence)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(analysis);

        if (!CallSiteSemanticsEvidence.TryResolveCallee(
                call,
                analysis,
                out MethodIdentity? callee)
            || callee is null
            || !analysis.UnsafeEvidenceByToken.TryGetValue(
                callee.MetadataToken,
                out IReadOnlyList<UnsafeEvidence>? unsafeEvidence))
        {
            evidence = null;
            return false;
        }

        ImmutableArray<CallSiteEvidenceCoordinate> coordinates =
        [
            .. unsafeEvidence
                .Select(ToCoordinate)
                .Where(static coordinate => coordinate is not null)
                .Select(static coordinate => coordinate!)
                .Distinct()
                .OrderBy(coordinate =>
                    coordinate.Location.Method.ModuleVersionId)
                .ThenBy(coordinate =>
                    coordinate.Location.Method.MetadataToken)
                .ThenBy(coordinate =>
                    coordinate.Location.ILOffset)
                .ThenBy(coordinate => coordinate.Kind),
        ];
        evidence = new CallSiteSafetyEvidence(callee, coordinates);
        return true;
    }

    static CallSiteEvidenceCoordinate? ToCoordinate(
        UnsafeEvidence evidence)
        => (evidence.Detail, evidence.Kind, evidence.ILOffset)
            switch
            {
                ("localloc", "opcode", int offset) => new(
                    evidence.Member,
                    offset,
                    CallSiteEvidenceKind.Localloc),
                (_, "calli", int offset) => new(
                    evidence.Member,
                    offset,
                    CallSiteEvidenceKind.Calli),
                _ => null,
            };
}

sealed class CallSiteSemanticsFactProducer : IResearchFactProducer
{
    static readonly AnnotationDescriptor CalleeSemantics =
        new("semantics.callee", AnnotationCategory.Semantics, "callee carries notable behavior semantics");
    static readonly AnnotationDescriptor CalleeSafety =
        new("safety.callee", AnnotationCategory.Semantics, "callee carries unsafe implementation evidence");

    public string Name => "call-site-semantics";
    public IReadOnlyList<string> Produces { get; } =
        [CalleeSemantics.Id, CalleeSafety.Id];
    public IReadOnlyList<string> DependsOn { get; } = [];
    public ResearchFactRequirements Requirements { get; } =
        ResearchFactRequirements.ForAssembly(
            LibraryBodyAnalysisFeatures.MethodEvidence);

    public IReadOnlyList<Finding<IAnnotation>> Produce(ResearchFactContext context)
    {
        if (context.Analysis is not { } analysis
            || context.Imported.MetadataToken == 0)
            return [];
        var callSites =
            analysis.InspectCallSites(context.Imported.MetadataToken);
        if (callSites.IsEmpty)
            return [];

        var facts = new List<Finding<IAnnotation>>();
        foreach (var finding in callSites)
        {
            var call = finding.Payload;
            if (CallSiteSemanticsEvidence.TryCreate(
                    call,
                    analysis,
                    out CallSiteSemanticsEvidence? semantics)
                && semantics is not null)
            {
                facts.Add(ResearchFactFinding.Project(
                    finding,
                    new Annotation<CallSiteSemanticsEvidence>(
                        CalleeSemantics,
                        call.ILOffset,
                        semantics,
                        Formatter: static item => item.Detail)));
            }

            if (CallSiteSafetyEvidence.TryCreate(
                    call,
                    analysis,
                    out CallSiteSafetyEvidence? safety)
                && safety is not null)
            {
                facts.Add(ResearchFactFinding.Project(
                    finding,
                    new Annotation<CallSiteSafetyEvidence>(
                        CalleeSafety,
                        call.ILOffset,
                        safety,
                        Formatter: static item => item.Detail)));
            }
        }
        return facts;
    }
}
