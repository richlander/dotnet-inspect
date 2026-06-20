namespace ILInspector.Decompiler.Pipeline;

internal sealed class SwitchRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.PatternSwitchStatement)),
            typeof(SwitchRaisingPass),
            [
                new FactPrimitive("member.corelib-identity:String.op_Equality", "MemberIdentity.IsStringEquality"),
                new FactPrimitive("place.variable", "PlaceIdentity.SameVariable"),
            ],
            PositiveCoverage: "StringSwitchRaisingTests small op_Equality-chain string switch fixtures",
            AdversarialCoverage: "StringSwitchRaisingTests single equality and user String.op_Equality lookalikes",
            MissingDiscriminator: "hash-bucket string switch form and pattern switches are still owed"),
    ];
}
