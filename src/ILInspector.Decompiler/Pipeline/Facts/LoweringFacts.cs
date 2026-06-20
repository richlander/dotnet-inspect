using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal enum LoweringFactRegister
{
    LocalRewriter,
    ClosureConversion,
}

internal readonly record struct LoweringFactKey(LoweringFactRegister Register, string Row)
{
    public override string ToString() => $"{Register}.{Row}";
}

internal readonly record struct FactPrimitive(string Id, string Evidence)
{
    public override string ToString() => Id;
}

internal sealed record LoweringFactEntry(
    LoweringFactKey Key,
    Type Mechanism,
    ImmutableArray<FactPrimitive> RequiredFacts,
    string PositiveCoverage,
    string AdversarialCoverage,
    string? MissingDiscriminator = null);

internal interface ILoweringFactProvider
{
    IEnumerable<LoweringFactEntry> Entries { get; }
}
