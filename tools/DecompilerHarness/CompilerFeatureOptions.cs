using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

static class CompilerFeatureOptions
{
    const System.Reflection.MethodImplAttributes RuntimeAsync =
        (System.Reflection.MethodImplAttributes)0x2000;

    public abstract record Resolution
    {
        public sealed record Available(
            CSharpParseOptions Options,
            MemorySafetyMode Mode)
            : Resolution;

        public sealed record Unavailable(
            MemorySafetyModeDecision Decision,
            string Reason)
            : Resolution;
    }

    public static Resolution Resolve(string assemblyPath)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        return Resolve(pe);
    }

    public static CSharpParseOptions ParseOptions(string assemblyPath)
        => Resolve(assemblyPath) switch
        {
            Resolution.Available available => available.Options,
            Resolution.Unavailable unavailable => throw new InvalidOperationException(
                unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown compiler feature resolution."),
        };

    public static CSharpParseOptions ParseOptions(PEReader pe)
        => Resolve(pe) switch
        {
            Resolution.Available available => available.Options,
            Resolution.Unavailable unavailable => throw new InvalidOperationException(
                unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown compiler feature resolution."),
        };

    public static Resolution Resolve(PEReader pe)
    {
        if (!pe.HasMetadata)
        {
            var rules = new MemorySafetyRulesResult.Unavailable(
                new MemorySafetyMetadataFailure(
                    MemorySafetyMetadataFailureKind.Malformed,
                    "artifact has no managed metadata"),
                []);
            var missingMetadata =
                new MemorySafetyModeDecision.Unavailable(rules);
            return new Resolution.Unavailable(
                missingMetadata,
                MemorySafetyModeDecision.DescribeUnavailable(rules));
        }

        MemorySafetyModeDecision decision =
            MemorySafetyModeDecision.Resolve(
                MemorySafetyMetadataIndex.Create(pe.GetMetadataReader()).Rules);
        if (decision is MemorySafetyModeDecision.Unavailable unavailable)
        {
            return new Resolution.Unavailable(
                unavailable,
                MemorySafetyModeDecision.DescribeUnavailable(
                    unavailable.Rules));
        }

        var available = (MemorySafetyModeDecision.Available)decision;
        bool usesUpdatedMemorySafetyRules =
            available.Mode == MemorySafetyMode.Updated;
        var options = new CSharpParseOptions(
            usesUpdatedMemorySafetyRules
                ? LanguageVersion.Preview
                : LanguageVersion.Latest);
        var features = new List<KeyValuePair<string, string>>();
        if (usesUpdatedMemorySafetyRules)
        {
            features.Add(new("updated-memory-safety-rules", "true"));
        }

        if (pe.HasMetadata && ModuleUsesRuntimeAsync(pe))
            features.Add(new("runtime-async", "on"));

        options = features.Count == 0
            ? options
            : options.WithFeatures(features);
        return new Resolution.Available(options, available.Mode);
    }

    static bool ModuleUsesRuntimeAsync(PEReader pe)
    {
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.MethodDefinitions)
        {
            if ((reader.GetMethodDefinition(handle).ImplAttributes & RuntimeAsync) != 0)
                return true;
        }

        return false;
    }
}
