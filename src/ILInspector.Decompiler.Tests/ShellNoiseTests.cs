using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The validity check filters a CS0305 ("generic type requires N type arguments")
/// on a bare identifier ONLY when it is a sibling-free-shell import collision (the
/// decompiler emitted a non-generic type, or a member receiver, whose simple name
/// collides with a <c>using</c>-imported generic). A genuinely mis-rendered generic
/// — a dropped type-argument list — lands on the same bare identifier, so the filter
/// must NOT suppress it: these tests prove the model-consulting discriminator keeps
/// that real defect reported.
/// </summary>
public class ShellNoiseTests
{
    static TypeRef ListOfInt =>
        TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "List`1"),
            [TypeRef.CoreLib("System", "Int32")]);

    static TypeRef NonGenericComparison =>
        TypeRef.Definition("ILInspector.Decompiler", "ILInspector.Decompiler.Pipeline.Ir", "Comparison");

    static TypeRef NonGenericConvert =>
        TypeRef.Definition("ILInspector.Decompiler", "ILInspector.Decompiler.Pipeline", "Convert");

    static TypeRef NestedEnvironment =>
        TypeRef.Definition("ILInspector.Decompiler", "ILInspector.Decompiler.Pipeline", "LocalFunctionRaisingPass+Environment");

    [Fact]
    public void GenericInstance_IsRecognizedByItsSimpleName()
    {
        // List<int> in the model => a bare `List` render is a dropped-argument
        // defect, not a collision => NOT filtered (stays reported).
        Assert.True(ShellNoise.ContainsGenericTypeNamed(ListOfInt, "List"));
    }

    [Fact]
    public void BareGenericDefinition_IsRecognized()
    {
        // The exact shape a dropped-type-argument bug renders: a generic definition
        // (arity suffix `1) reached with no type-argument list.
        var bareGeneric = TypeRef.CoreLib("System.Collections.Generic", "List`1");
        Assert.True(ShellNoise.ContainsGenericTypeNamed(bareGeneric, "List"));
    }

    [Fact]
    public void NestedGeneric_IsRecognizedThroughElementAndArguments()
    {
        Assert.True(ShellNoise.ContainsGenericTypeNamed(TypeRef.SzArray(ListOfInt), "List"));
        var dictionaryOfList = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Collections.Generic", "Dictionary`2"),
            [TypeRef.CoreLib("System", "String"), ListOfInt]);
        Assert.True(ShellNoise.ContainsGenericTypeNamed(dictionaryOfList, "List"));
    }

    [Fact]
    public void NonGenericType_IsNotMistakenForAGeneric()
    {
        // The collision case: a non-generic type whose bare name happens to match an
        // imported generic. No generic of that name in the model => filtered as noise.
        Assert.False(ShellNoise.ContainsGenericTypeNamed(NonGenericComparison, "Comparison"));
    }

    [Fact]
    public void DifferentSimpleName_DoesNotMatch()
    {
        Assert.False(ShellNoise.ContainsGenericTypeNamed(ListOfInt, "Comparison"));
    }

    [Fact]
    public void ReferencesGenericTypeNamed_KeepsADroppedArgumentGenericReported()
    {
        // A function whose model references List<int> => a bare `List` CS0305 must
        // stay reported (the discriminator returns true, so the filter declines).
        var withGeneric = Function(locals: [ListOfInt]);
        Assert.True(ShellNoise.ReferencesGenericTypeNamed(withGeneric, "List"));
    }

    [Fact]
    public void ReferencesGenericTypeNamed_FiltersTheNonGenericCollision()
    {
        // A function whose model holds only a non-generic `Comparison` => its bare
        // CS0305 is the import collision and is filtered.
        var withCollision = Function(locals: [NonGenericComparison]);
        Assert.False(ShellNoise.ReferencesGenericTypeNamed(withCollision, "Comparison"));
    }

    [Fact]
    public void ReferencesNonGenericTypeNamed_FindsLocalSiblingTypeCollision()
    {
        var withCollision = Function(locals: [NonGenericConvert]);
        Assert.True(ShellNoise.ReferencesNonGenericTypeNamed(withCollision, "Convert"));
    }

    [Fact]
    public void ReferencesNonGenericTypeNamed_FindsNestedSiblingTypeCollision()
    {
        var withCollision = Function(locals: [NestedEnvironment]);
        Assert.True(ShellNoise.ReferencesNonGenericTypeNamed(withCollision, "Environment"));
    }

    [Fact]
    public void ReferencesNonGenericTypeNamed_DoesNotHideGenericDefects()
    {
        var withGeneric = Function(locals: [ListOfInt]);
        Assert.False(ShellNoise.ReferencesNonGenericTypeNamed(withGeneric, "List"));
    }

    static IrFunction Function(ImmutableArray<TypeRef> locals) =>
        new(
            "M",
            TypeRef.Definition("asm", "NS", "Host"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            locals,
            new BlockContainer());
}
