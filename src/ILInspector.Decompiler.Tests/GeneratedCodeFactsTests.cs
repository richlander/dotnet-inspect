using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class GeneratedCodeFactsTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_closureHolder = TypeRef.Definition("UserAssembly", "Samples", "Outer+<>c");

    [Fact]
    public void NonCapturingLambdaMethod_RequiresGeneratedDeclaringType()
    {
        var method = LambdaMethod(declaringTypeIsCompilerGenerated: true);

        Assert.True(GeneratedCodeFacts.IsNonCapturingLambdaMethod(method));
        Assert.False(GeneratedCodeFacts.IsNonCapturingLambdaMethod(method with { DeclaringTypeIsCompilerGenerated = false }));
    }

    [Fact]
    public void NonCapturingLambdaMethod_RequiresClosureHolderAndLambdaMethodName()
    {
        var method = LambdaMethod(declaringTypeIsCompilerGenerated: true);

        Assert.False(GeneratedCodeFacts.IsNonCapturingLambdaMethod(method with
        {
            DeclaringType = TypeRef.Definition("UserAssembly", "Samples", "Outer+<>c__DisplayClass0_0"),
        }));
        Assert.False(GeneratedCodeFacts.IsNonCapturingLambdaMethod(method with { Name = "M" }));
    }

    [Fact]
    public void GeneratedNameHelpers_UseLeafNestedTypeName()
    {
        Assert.True(GeneratedCodeFacts.IsStaticLambdaClosureHolderName(s_closureHolder));
        Assert.True(GeneratedCodeFacts.IsDisplayClassName(TypeRef.Definition(
            "UserAssembly",
            "Samples",
            "Outer+<>c__DisplayClass0_0")));
    }

    static MethodRef LambdaMethod(bool declaringTypeIsCompilerGenerated)
        => new(s_closureHolder, "<M>b__0_0", s_int, [s_int], HasThis: true)
        {
            DeclaringTypeIsCompilerGenerated = declaringTypeIsCompilerGenerated,
        };
}
