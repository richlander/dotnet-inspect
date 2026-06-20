namespace ILInspector.Decompiler.Pipeline;

internal sealed class SwitchRecoveryFacts : ILoweringFactProvider
{
    public IEnumerable<LoweringFactEntry> Entries =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.PatternSwitchStatement)),
            typeof(SwitchRaisingPass),
            [
                new FactPrimitive("generated.string-hash-helper", "GeneratedCodeIdentity.IsStringHashHelper"),
                new FactPrimitive("member.corelib-identity:String.op_Equality", "MemberIdentity.IsStringEquality"),
                new FactPrimitive("member.corelib-identity:String.Length", "MemberIdentity.IsStringLengthGetter"),
                new FactPrimitive("member.corelib-identity:String.Chars", "MemberIdentity.IsStringCharsGetter"),
                new FactPrimitive("place.variable", "PlaceIdentity.SameVariable"),
            ],
            PositiveCoverage: "StringSwitchRaisingTests small op_Equality-chain and bucketed string switch fixtures",
            AdversarialCoverage: "StringSwitchRaisingTests single equality, user String.op_Equality, and user ComputeStringHash lookalikes",
            MissingDiscriminator: "pattern switches are still owed"),
    ];
}
