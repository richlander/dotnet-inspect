using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Direct coverage of the shared per-site "type is apparent" predicate
/// (<see cref="CSharpPrinter.TypeIsApparent"/>) that the target-typed-<c>new</c>
/// shortener consumes and a future opt-in <c>var</c> lens will consume. Each apparent
/// shape is paired with a close negative so the predicate stays conservative: it must
/// report apparent only when the right-hand side spells the declared type exactly.
/// </summary>
public class TypeApparencyTests
{
    static readonly TypeRef VoidType = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef IntType = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef ListOfInt = TypeRef.Definition("synthetic", "N", "MyList");
    static readonly TypeRef OtherType = TypeRef.Definition("synthetic", "N", "Other");

    static NewObject Creation(TypeRef type)
        => new(new MethodRef(type, ".ctor", VoidType, [], HasThis: true), []);

    [Fact]
    public void ObjectCreation_OfExactType_IsApparent()
        => Assert.True(CSharpPrinter.TypeIsApparent(ListOfInt, Creation(ListOfInt)));

    [Fact]
    public void ObjectCreation_OfDifferentType_IsNotApparent()
        => Assert.False(CSharpPrinter.TypeIsApparent(OtherType, Creation(ListOfInt)));

    [Fact]
    public void ArrayCreation_OfMatchingElementType_IsApparent()
        => Assert.True(CSharpPrinter.TypeIsApparent(
            TypeRef.SzArray(IntType),
            new NewArray(IntType, new Constant(4, IntType))));

    [Fact]
    public void ArrayCreation_ComparedAgainstBareElementType_IsNotApparent()
        // `new int[n]` names `int[]`, not `int`: a declaration typed `int` is not
        // apparent from an array creation.
        => Assert.False(CSharpPrinter.TypeIsApparent(
            IntType,
            new NewArray(IntType, new Constant(4, IntType))));

    [Fact]
    public void ExplicitCast_ToExactType_IsApparent()
        => Assert.True(CSharpPrinter.TypeIsApparent(
            ListOfInt,
            new CastClass(ListOfInt, new LoadArgument(0, "x", OtherType))));

    [Fact]
    public void ExplicitCast_ToDifferentType_IsNotApparent()
        => Assert.False(CSharpPrinter.TypeIsApparent(
            OtherType,
            new CastClass(ListOfInt, new LoadArgument(0, "x", OtherType))));

    [Fact]
    public void PlainValueRead_IsNotApparent()
        // A parameter/local read (or any non-type-naming expression) leaves the type
        // implicit — conservatively not apparent.
        => Assert.False(CSharpPrinter.TypeIsApparent(ListOfInt, new LoadArgument(0, "x", ListOfInt)));

    [Fact]
    public void DefaultValue_IsNotApparent_ConservativeByDesign()
        // `default(T)` names the type, but a target-typed `default` renders bare; the
        // v1 predicate declines it rather than risk a `var x = default;` that loses T.
        => Assert.False(CSharpPrinter.TypeIsApparent(IntType, new DefaultValue(IntType)));
}
