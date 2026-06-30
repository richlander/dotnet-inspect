using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research;

sealed class CallSiteSemanticsFactProducer : IResearchFactProducer
{
    static readonly AnnotationDescriptor CalleeSemantics =
        new("semantics.callee", AnnotationCategory.Semantics, "callee carries notable behavior semantics");
    static readonly AnnotationDescriptor CalleeSafety =
        new("safety.callee", AnnotationCategory.Semantics, "callee carries unsafe implementation evidence");

    public string Name => "call-site-semantics";
    public IReadOnlyList<string> Produces { get; } = ["semantics.callee", "safety.callee"];
    public IReadOnlyList<string> DependsOn { get; } = [];

    public IReadOnlyList<Annotation> Produce(ResearchFactContext context)
    {
        if (context.Assembly is not { } assembly || context.Imported.MetadataToken == 0)
            return [];
        if (!assembly.CallsByCaller.TryGetValue(context.Imported.MetadataToken, out var calls))
            return [];

        var facts = new List<Annotation>();
        foreach (var call in calls)
        {
            int calleeToken = ResolveCallee(call, assembly);
            if (calleeToken == 0)
                continue;

            var signals = assembly.Signals.GetValueOrDefault(calleeToken, MethodSignals.None);
            if (!signals.ExceptionTypes.IsEmpty)
                facts.Add(new Annotation(CalleeSemantics, call.ILOffset, ExceptionDetail(signals)));

            var unsafeDetail = UnsafeDetail(assembly.Index.UnsafeEvidence, calleeToken);
            if (unsafeDetail is not null)
                facts.Add(new Annotation(CalleeSafety, call.ILOffset, unsafeDetail));
        }
        return facts;
    }

    static int ResolveCallee(DirectCall call, ResearchAssemblyContext assembly)
        => assembly.Signals.ContainsKey(call.CalleeDefinitionToken) || assembly.LeverageByToken.ContainsKey(call.CalleeDefinitionToken)
            ? call.CalleeDefinitionToken
            : 0;

    static string ExceptionDetail(MethodSignals signals)
        => $"may-throw {string.Join("/", signals.ExceptionTypes.Take(2))}";

    static string? UnsafeDetail(IReadOnlyList<UnsafeEvidence> evidence, int token)
    {
        var calleeEvidence = evidence
            .Where(item => item.Member.MetadataToken == token)
            .ToArray();
        if (calleeEvidence.Length == 0)
            return null;

        var parts = new List<string> { "unsafe" };
        if (calleeEvidence.Any(item => item.Detail == "localloc"))
            parts.Add("stackalloc");
        if (calleeEvidence.Any(item => item.Kind == "calli"))
            parts.Add("calli");
        return string.Join("; ", parts);
    }
}
