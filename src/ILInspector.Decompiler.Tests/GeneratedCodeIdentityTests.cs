using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class GeneratedCodeIdentityTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_closureHolder = TypeRef.Definition("UserAssembly", "Samples", "Outer+<>c");

    [Fact]
    public void NonCapturingLambdaMethod_RequiresGeneratedDeclaringType()
    {
        var method = LambdaMethod(declaringTypeIsCompilerGenerated: true);

        Assert.True(GeneratedCodeIdentity.IsNonCapturingLambdaMethod(method));
        Assert.False(GeneratedCodeIdentity.IsNonCapturingLambdaMethod(method with { DeclaringTypeCompilerGenerated = MetadataFactState.No }));
    }

    [Fact]
    public void NonCapturingLambdaMethod_RequiresClosureHolderAndLambdaMethodName()
    {
        var method = LambdaMethod(declaringTypeIsCompilerGenerated: true);

        Assert.False(GeneratedCodeIdentity.IsNonCapturingLambdaMethod(method with
        {
            DeclaringType = TypeRef.Definition("UserAssembly", "Samples", "Outer+<>c__DisplayClass0_0"),
        }));
        Assert.False(GeneratedCodeIdentity.IsNonCapturingLambdaMethod(method with { Name = "M" }));
    }

    [Fact]
    public void GeneratedNameHelpers_UseLeafNestedTypeName()
    {
        Assert.True(GeneratedCodeIdentity.IsStaticLambdaClosureHolderName(s_closureHolder));
        Assert.True(GeneratedCodeIdentity.IsDisplayClassName(TypeRef.Definition(
            "UserAssembly",
            "Samples",
            "Outer+<>c__DisplayClass0_0")));
    }

    static MethodRef LambdaMethod(bool declaringTypeIsCompilerGenerated)
        => new(s_closureHolder, "<M>b__0_0", s_int, [s_int], HasThis: true)
        {
            DeclaringTypeCompilerGenerated = declaringTypeIsCompilerGenerated ? MetadataFactState.Yes : MetadataFactState.No,
        };
}
