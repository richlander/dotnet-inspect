using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

public enum CallSiteEvidenceKind
{
    ExceptionConstruction,
    Localloc,
    Calli,
}

/// <summary>
/// One physical callee-body coordinate that supports a caller-side relationship fact.
/// </summary>
public sealed record CallSiteEvidenceCoordinate(
    MethodIdentity Method,
    int ILOffset,
    CallSiteEvidenceKind Kind);

public sealed record CallSiteSemanticsEvidence(
    MethodIdentity Callee,
    ImmutableArray<string> ExceptionTypes,
    ImmutableArray<CallSiteEvidenceCoordinate> Coordinates)
{
    public string Detail => $"may-throw {string.Join("/", ExceptionTypes)}";

    public static bool TryCreate(
        DirectCall call,
        ResearchAssemblyContext assembly,
        out CallSiteSemanticsEvidence? evidence)
    {
        bool resolved = TryResolveCallee(call, assembly, out MethodIdentity? callee);
        var signals = callee is null
            ? MethodSignals.None
            : assembly.Signals.GetValueOrDefault(callee.MetadataToken, MethodSignals.None);
        ImmutableArray<string> exceptionTypes = DomainExceptionTypes(signals);
        if (!resolved || callee is null || exceptionTypes.Length == 0)
        {
            evidence = null;
            return false;
        }

        ImmutableArray<CallSiteEvidenceCoordinate> coordinates =
            assembly.CallsByCaller
                .GetValueOrDefault(callee.MetadataToken, [])
                .Where(candidate =>
                    candidate.Kind == CallKind.NewObject
                    && exceptionTypes.Contains(
                        ConstructedTypeName(candidate.Callee.DeclaringType),
                        StringComparer.Ordinal))
                .Select(candidate => new CallSiteEvidenceCoordinate(
                    candidate.EvidenceMethod,
                    candidate.ILOffset,
                    CallSiteEvidenceKind.ExceptionConstruction))
                .Distinct()
                .OrderBy(coordinate => coordinate.ILOffset)
                .ToImmutableArray();
        evidence = new CallSiteSemanticsEvidence(
            callee,
            exceptionTypes,
            coordinates);
        return true;
    }

    internal static bool TryResolveCallee(
        DirectCall call,
        ResearchAssemblyContext assembly,
        out MethodIdentity? callee)
    {
        int calleeToken = call.CalleeDefinitionToken;
        callee = assembly.Signals.ContainsKey(calleeToken)
            || assembly.LeverageByToken.ContainsKey(calleeToken)
            ? assembly.Index.DeclaredMethods.FirstOrDefault(
                method => method.MetadataToken == calleeToken)
            : null;
        return callee is not null;
    }

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

/// <summary>
/// Typed remote provenance for a caller relationship whose resolved callee has
/// stack-allocation or indirect-invocation evidence.
/// </summary>
public sealed record CallSiteSafetyEvidence(
    MethodIdentity Callee,
    ImmutableArray<CallSiteEvidenceCoordinate> Coordinates)
{
    public string Detail => string.Join("; ", Coordinates
        .Select(static coordinate => coordinate.Kind switch
        {
            CallSiteEvidenceKind.Localloc => "stackalloc",
            CallSiteEvidenceKind.Calli => "unsafe calli",
            _ => throw new InvalidOperationException(
                $"Unsupported callee safety evidence kind '{coordinate.Kind}'."),
        })
        .Distinct(StringComparer.Ordinal));

    public static bool TryCreate(
        DirectCall call,
        ResearchAssemblyContext assembly,
        out CallSiteSafetyEvidence? evidence)
    {
        if (!CallSiteSemanticsEvidence.TryResolveCallee(
                call,
                assembly,
                out MethodIdentity? callee)
            || callee is null
            || !assembly.UnsafeEvidenceByToken.TryGetValue(
                callee.MetadataToken,
                out IReadOnlyList<UnsafeEvidence>? unsafeEvidence))
        {
            evidence = null;
            return false;
        }

        ImmutableArray<CallSiteEvidenceCoordinate> coordinates =
        [
            .. unsafeEvidence
                .Where(item => item.Member.MetadataToken == callee.MetadataToken)
                .Select(item => ToCoordinate(item))
                .Where(coordinate => coordinate is not null)
                .Select(coordinate => coordinate!)
                .Distinct()
                .OrderBy(coordinate => coordinate.ILOffset)
                .ThenBy(coordinate => coordinate.Kind)
                .ToImmutableArray(),
        ];
        if (coordinates.IsDefaultOrEmpty)
        {
            evidence = null;
            return false;
        }

        evidence = new CallSiteSafetyEvidence(callee, coordinates);
        return true;
    }

    static CallSiteEvidenceCoordinate? ToCoordinate(UnsafeEvidence evidence)
        => (evidence.Detail, evidence.Kind, evidence.ILOffset) switch
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
        new("safety.callee", AnnotationCategory.Semantics, "callee carries stack-allocation or unsafe indirect-call evidence");

    public string Name => "call-site-semantics";
    public IReadOnlyList<string> Produces { get; } = ["semantics.callee", "safety.callee"];
    public IReadOnlyList<string> DescriptorIds => [CalleeSemantics.Id, CalleeSafety.Id];
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

            if (CallSiteSafetyEvidence.TryCreate(
                    call,
                    assembly,
                    out CallSiteSafetyEvidence? safetyEvidence)
                && safetyEvidence is not null)
            {
                facts.Add(new Annotation<CallSiteSafetyEvidence>(
                    CalleeSafety,
                    call.ILOffset,
                    safetyEvidence,
                    Formatter: static item => item.Detail));
            }
        }
        return facts;
    }
}
