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
    public void CapturingLambdaMethod_RequiresGeneratedDisplayClass()
    {
        var onDisplayClass = LambdaMethod(declaringTypeIsCompilerGenerated: true) with
        {
            DeclaringType = TypeRef.Definition("UserAssembly", "Samples", "Outer+<>c__DisplayClass0_0"),
        };

        Assert.True(GeneratedCodeIdentity.IsCapturingLambdaMethod(onDisplayClass));
        // The non-capturing <>c singleton and the capturing display class are distinct forms.
        Assert.False(GeneratedCodeIdentity.IsCapturingLambdaMethod(LambdaMethod(declaringTypeIsCompilerGenerated: true)));
        Assert.False(GeneratedCodeIdentity.IsCapturingLambdaMethod(onDisplayClass with { DeclaringTypeCompilerGenerated = MetadataFactState.No }));
    }

    [Fact]
    public void GeneratedNameHelpers_UseLeafNestedTypeName()
    {
        Assert.True(GeneratedCodeIdentity.IsStaticLambdaClosureHolderName(s_closureHolder));
        Assert.True(GeneratedCodeIdentity.IsDisplayClassName(TypeRef.Definition(
            "UserAssembly",
            "Samples",
            "Outer+<>c__DisplayClass0_0")));
        Assert.True(GeneratedCodeIdentity.IsIteratorStateMachineTypeName(TypeRef.GenericInstance(
            TypeRef.Definition("UserAssembly", "Samples", "Outer+<M>d__0`1"),
            [s_int])));
    }

    [Fact]
    public void IteratorStateMachineConstructor_RequiresGeneratedDeclaringTypeAndCtor()
    {
        var constructor = IteratorConstructor(declaringTypeIsCompilerGenerated: true);

        Assert.True(GeneratedCodeIdentity.IsIteratorStateMachineConstructor(constructor));
        Assert.False(GeneratedCodeIdentity.IsIteratorStateMachineConstructor(constructor with
        {
            DeclaringTypeCompilerGenerated = MetadataFactState.No,
        }));
        Assert.False(GeneratedCodeIdentity.IsIteratorStateMachineConstructor(constructor with { Name = "MoveNext" }));
        Assert.False(GeneratedCodeIdentity.IsIteratorStateMachineConstructor(constructor with
        {
            DeclaringType = TypeRef.Definition("UserAssembly", "Samples", "Outer+Iterator"),
        }));
    }

    static MethodRef LambdaMethod(bool declaringTypeIsCompilerGenerated)
        => new(s_closureHolder, "<M>b__0_0", s_int, [s_int], HasThis: true)
        {
            DeclaringTypeCompilerGenerated = declaringTypeIsCompilerGenerated ? MetadataFactState.Yes : MetadataFactState.No,
        };

    static MethodRef IteratorConstructor(bool declaringTypeIsCompilerGenerated)
        => new(TypeRef.Definition("UserAssembly", "Samples", "Outer+<M>d__0"), ".ctor", TypeRef.CoreLib("System", "Void"), [s_int], HasThis: false)
        {
            DeclaringTypeCompilerGenerated = declaringTypeIsCompilerGenerated ? MetadataFactState.Yes : MetadataFactState.No,
        };
}
