using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

public sealed record CallSiteSemanticsEvidence(
    MethodIdentity Callee,
    ImmutableArray<string> ExceptionTypes,
    ImmutableArray<int> ExceptionConstructionOffsets)
{
    public string Detail => $"may-throw {string.Join("/", ExceptionTypes)}";

    public static bool TryCreate(
        DirectCall call,
        ResearchAssemblyContext assembly,
        out CallSiteSemanticsEvidence? evidence)
    {
        int calleeToken = ResolveCallee(call, assembly);
        MethodIdentity? callee = calleeToken == 0
            ? null
            : assembly.Index.DeclaredMethods.FirstOrDefault(
                method => method.MetadataToken == calleeToken);
        var signals = callee is null
            ? MethodSignals.None
            : assembly.Signals.GetValueOrDefault(calleeToken, MethodSignals.None);
        ImmutableArray<string> exceptionTypes = DomainExceptionTypes(signals);
        if (callee is null || exceptionTypes.Length == 0)
        {
            evidence = null;
            return false;
        }

        ImmutableArray<int> constructionOffsets =
            assembly.Index.GetDirectCallsByCaller()
                .GetValueOrDefault(calleeToken, [])
                .Where(candidate =>
                    candidate.Kind == CallKind.NewObject
                    && exceptionTypes.Contains(
                        ConstructedTypeName(candidate.Callee.DeclaringType),
                        StringComparer.Ordinal))
                .Select(candidate => candidate.ILOffset)
                .Distinct()
                .Order()
                .ToImmutableArray();
        evidence = new CallSiteSemanticsEvidence(
            callee,
            exceptionTypes,
            constructionOffsets);
        return true;
    }

    static int ResolveCallee(DirectCall call, ResearchAssemblyContext assembly)
        => assembly.Signals.ContainsKey(call.CalleeDefinitionToken)
            || assembly.LeverageByToken.ContainsKey(call.CalleeDefinitionToken)
                ? call.CalleeDefinitionToken
                : 0;

    static ImmutableArray<string> DomainExceptionTypes(MethodSignals signals)
        => [.. signals.ExceptionTypes
            .Where(type => !IsArgumentValidationException(type))
            .Take(2)];

    static bool IsArgumentValidationException(string type)
        => type is "ArgumentException"
            or "ArgumentNullException"
            or "ArgumentOutOfRangeException";

    static string ConstructedTypeName(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } element
            ? element.Name
            : type.Name;
}

sealed class CallSiteSemanticsFactProducer : IResearchFactProducer
{
    static readonly AnnotationDescriptor CalleeSemantics =
        new("semantics.callee", AnnotationCategory.Semantics, "callee carries notable behavior semantics");
    static readonly AnnotationDescriptor CalleeSafety =
        new("safety.callee", AnnotationCategory.Semantics, "callee carries unsafe implementation evidence");

    public string Name => "call-site-semantics";
    public IReadOnlyList<string> Produces { get; } = ["semantics.callee", "safety.callee"];
    public IReadOnlyList<string> DependsOn { get; } = [];
    public ResearchFactRequirements Requirements { get; } =
        ResearchFactRequirements.ForAssembly(
            LibraryBodyAnalysisFeatures.MethodEvidence);

    public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context)
    {
        if (context.Assembly is not { } assembly || context.Imported.MetadataToken == 0)
            return [];
        var callSites = assembly.InspectCallSites(context.Imported.MetadataToken);
        if (callSites.IsEmpty)
            return [];

        var facts = new List<IAnnotation>();
        foreach (var finding in callSites)
        {
            var call = finding.Payload;
            int calleeToken = call.CalleeDefinitionToken;
            if (CallSiteSemanticsEvidence.TryCreate(
                    call,
                    assembly,
                    out var evidence)
                && evidence is not null)
            {
                facts.Add(new Annotation<CallSiteSemanticsEvidence>(
                    CalleeSemantics,
                    call.ILOffset,
                    evidence,
                    Formatter: static item => item.Detail));
            }

            var unsafeDetail = UnsafeDetail(assembly, calleeToken);
            if (unsafeDetail is not null)
                facts.Add(new Annotation(CalleeSafety, call.ILOffset, unsafeDetail));
        }
        return facts;
    }

    static string? UnsafeDetail(ResearchAssemblyContext assembly, int token)
    {
        if (!assembly.UnsafeEvidenceByToken.TryGetValue(token, out var calleeEvidence) || calleeEvidence.Count == 0)
            return null;

        var parts = new List<string> { "unsafe" };
        if (calleeEvidence.Any(item => item.Detail == "localloc"))
            parts.Add("stackalloc");
        if (calleeEvidence.Any(item => item.Kind == "calli"))
            parts.Add("calli");
        return string.Join("; ", parts);
    }
}
