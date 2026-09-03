using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Findings;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using LegacyFixedBuffer = ILInspector.Decompiler.Fixtures.LegacyUnsafe.FixedBufferResiduals;
using LegacyPointerArithmetic = ILInspector.Decompiler.Fixtures.LegacyUnsafe.PointerArithmeticFixtures;
using LegacyStringPinning = ILInspector.Decompiler.Fixtures.LegacyUnsafe.StringPinningResiduals;
using LegacyStackallocInitializers = ILInspector.Decompiler.Fixtures.LegacyUnsafe.StackallocInitializerResiduals;
using LegacyUnsafe = ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;
using NewFixedBuffer = ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals;
using NewPointerArithmetic = ILInspector.Decompiler.Fixtures.NewUnsafe.PointerArithmeticFixtures;
using NewStackallocInitializers = ILInspector.Decompiler.Fixtures.NewUnsafe.StackallocInitializerResiduals;
using NewStringPinning = ILInspector.Decompiler.Fixtures.NewUnsafe.StringPinningResiduals;
using NewUnsafe = ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 6 guard for the decompiler product quality ladder (#1599):
/// unsafe/native/byref correctness. This is an initial guard, not a completion
/// claim for every unsafe-code corner. It reuses the existing compiled unsafe
/// fixture matrix instead of adding duplicate fixture assemblies: new-rules and
/// legacy unsafe assemblies carry byte-identical IL and differ only by the
/// memory-safety module attribute, while synthetic canaries pin unspellable
/// volatile/pinned residuals that C# cannot compile directly.
/// </summary>
[Trait("Area", "Validity")]
public class LadderRung6GateTests
{
    static readonly string NewUnsafePath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
    static readonly string LegacyUnsafePath = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
    static readonly string NewUnsafeType = typeof(NewUnsafe).FullName!;
    static readonly string LegacyUnsafeType = typeof(LegacyUnsafe).FullName!;
    static readonly string ByRefFixturePath = FixtureCatalog.DecompilerLadderRung4.AssemblyPath();
    static readonly string ByRefFixtureType = typeof(LadderRung4.CSharp7LocalSyntax).FullName!;
    static readonly string FixedBufferType = typeof(NewFixedBuffer).FullName!;
    static readonly string LegacyPointerArithmeticType = typeof(LegacyPointerArithmetic).FullName!;
    static readonly string PointerArithmeticType = typeof(NewPointerArithmetic).FullName!;
    static readonly string LegacyFixedBufferType = typeof(LegacyFixedBuffer).FullName!;
    static readonly string StringPinningType = typeof(NewStringPinning).FullName!;
    static readonly string LegacyStringPinningType = typeof(LegacyStringPinning).FullName!;
    static readonly string StackallocInitializerType = typeof(NewStackallocInitializers).FullName!;
    static readonly string LegacyStackallocInitializerType = typeof(LegacyStackallocInitializers).FullName!;

    static readonly string[] ExpectedUnsafeMembers =
    [
        "CallRisky",
        "ConsumePointer",
        "DerefPointer",
        "FreePointer",
        "InvokeFunctionPointer",
        "PassAddress",
        "Risky",
        "StackAllocDefault",
        "StackAllocEventData",
        "StackAllocSkipInit",
        "SumPinned",
    ];

    [Fact]
    public void Rung6Fixtures_ExposeExactUnsafeMemberSet()
    {
        Assert.Equal(
            ExpectedUnsafeMembers,
            LoadRaisedMembers(NewUnsafePath, NewUnsafeType)
                .Select(m => m.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(
            ExpectedUnsafeMembers,
            LoadRaisedMembers(LegacyUnsafePath, LegacyUnsafeType)
                .Select(m => m.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Rung6Fixtures_HaveNoInvalidFullOutput()
    {
        var results = ValidityCheck.Evaluate([NewUnsafePath, LegacyUnsafePath], importSiblingBodies: true)
            .Where(r => r.TypeName == NewUnsafeType || r.TypeName == LegacyUnsafeType)
            .ToList();

        Assert.NotEmpty(results);

        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.Id}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Rung 6 requires no invalid Full; malformed Full: " + string.Join("; ", malformedFull));

        var semanticFull = results
            .Where(r => r.IsFull && r.HasSemanticDefect)
            .Select(r => $"{r.Id}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semanticFull.Length == 0,
            "Rung 6 requires zero non-noise semantic defects on Full methods; defects: "
                + string.Join("; ", semanticFull));

        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.Id} was not semantically bound."));
    }

    [Fact]
    public void Rung6Fixtures_RenderUnsafeNativeSurfaceRecognizably()
    {
        var newRules = LoadRaisedMembers(NewUnsafePath, NewUnsafeType);
        var legacy = LoadRaisedMembers(LegacyUnsafePath, LegacyUnsafeType);

        Assert.All(newRules.Concat(legacy), member =>
        {
            Assert.Equal(DecompilationFidelity.Full, member.Function.Fidelity);
            Assert.Null(Completeness.Residual(member.Function));
            Assert.True(member.Result.Succeeded, $"{member.Name} did not print.");
        });

        string NewBody(string name) => newRules.Single(m => m.Name == name).Body;
        string LegacyBody(string name) => legacy.Single(m => m.Name == name).Body;

        var deref = FirstUnsafeBlockBody(NewBody("DerefPointer"));
        Assert.Contains("return *", deref);
        Assert.Contains("*(int*)(&value)", NewBody("DerefPointer"));
        Assert.Contains("ConsumePointer((int*)(&value))", NewBody("PassAddress"));
        Assert.Contains("callback(x);", FirstUnsafeBlockBody(NewBody("InvokeFunctionPointer")));
        Assert.Contains("Risky()", FirstUnsafeBlockBody(NewBody("CallRisky")));
        Assert.Contains("NativeMemory.Free(p);", FirstUnsafeBlockBody(NewBody("FreePointer")));

        var skipInit = NewBody("StackAllocSkipInit");
        Assert.Contains("scoped Span<int> s", skipInit);
        Assert.Contains("stackalloc int[", FirstUnsafeBlockBody(skipInit));

        var defaultStackAlloc = NewBody("StackAllocDefault");
        Assert.DoesNotContain("unsafe", defaultStackAlloc);
        Assert.Contains("Span<int> s = stackalloc int[", defaultStackAlloc);

        var eventData = FirstUnsafeBlockBody(NewBody("StackAllocEventData"));
        Assert.Contains("byte* __stackalloc = stackalloc byte[", eventData);
        Assert.Contains("int* values = (int*)__stackalloc;", eventData);
        Assert.Contains("*(values + 1)", eventData);
        Assert.DoesNotContain("*(values + 4)", eventData);

        var pinned = NewBody("SumPinned");
        Assert.Contains("fixed (int* p = ", pinned);
        Assert.Contains("sum += p[i];", FirstUnsafeBlockBody(pinned));
        Assert.DoesNotContain("pinned", pinned);

        Assert.All(ExpectedUnsafeMembers, name =>
        {
            Assert.DoesNotContain("unsafe", LegacyBody(name));
            Assert.DoesNotContain("pinned", LegacyBody(name));
        });
    }

    [Fact]
    public void Rung6NewRulesPointerAddressOf_RecompilesInSafeMethodShell()
    {
        var members = LoadRaisedMembers(NewUnsafePath, NewUnsafeType);
        var derefBody = members.Single(m => m.Name == "DerefPointer").Body;
        var passAddressBody = members.Single(m => m.Name == "PassAddress").Body;

        var derefDiagnostics = RecompileNewRules(
            "static int M(int value)",
            derefBody,
            $"using static {NewUnsafeType};");
        var passAddressDiagnostics = RecompileNewRules(
            "static int M(int value)",
            passAddressBody,
            $"using static {NewUnsafeType};",
            MetadataReference.CreateFromFile(NewUnsafePath));

        AssertNoErrors(derefDiagnostics, derefBody);
        AssertNoErrors(passAddressDiagnostics, passAddressBody);
    }

    [Fact]
    public void Rung6PointerFieldAccess_RendersArrowAndRecompiles()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var fieldX = new FieldRef(point, "X", Int32);
        var fieldY = new FieldRef(point, "Y", Int32);
        var p = new LoadArgument(0, "p", pointPointer);
        var body = CSharpPrinter.Print(Function(
            "ReadPointerField",
            Int32,
            [new Parameter("p", pointPointer)],
            [],
            new StoreField(
                fieldX,
                p,
                new Binary(
                    BinaryKind.Add,
                    isChecked: false,
                    isUnsigned: false,
                    new LoadField(fieldX, (IrExpression)p.Clone()),
                    new LoadField(fieldY, (IrExpression)p.Clone()))),
            new Return(new LoadField(fieldX, (IrExpression)p.Clone())))).Output!;

        Assert.Contains("p->X += p->Y;", body);
        Assert.Contains("return p->X;", body);
        Assert.DoesNotContain("p.X", body);
        Assert.DoesNotContain("p.Y", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M(Point* p)",
                body,
                "public struct Point { public int X; public int Y; }"),
            body);
    }

    [Fact]
    public void Rung6PointerBackingPropertyAccess_RendersArrowAndRecompiles()
    {
        var record = TypeRef.Definition("Synthetic", "LadderRung6", "PropertyPoint", ValueTypeHint.ValueType);
        var recordPointer = TypeRef.Pointer(record);
        var backing = new FieldRef(record, "<X>k__BackingField", Int32)
        {
            BackingPropertyName = "X",
        };
        var body = CSharpPrinter.Print(Function(
            "ReadPointerBackingProperty",
            Int32,
            [new Parameter("p", recordPointer)],
            [],
            new Return(new LoadField(backing, new LoadArgument(0, "p", recordPointer))))).Output!;

        Assert.Contains("return p->X;", body);
        Assert.DoesNotContain("p.X", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M(PropertyPoint* p)",
                body,
                "public struct PropertyPoint { public int X { get; set; } }"),
            body);
    }

    [Fact]
    public void Rung6PointerPrimaryConstructorCaptureAccess_RendersArrowAndRecompiles()
    {
        var capture = TypeRef.Definition("Synthetic", "LadderRung6", "CapturePoint", ValueTypeHint.ValueType);
        var capturePointer = TypeRef.Pointer(capture);
        var field = new FieldRef(capture, "<X>P", Int32);
        var body = CSharpPrinter.Print(Function(
            "ReadPointerPrimaryConstructorCapture",
            Int32,
            [new Parameter("p", capturePointer)],
            [],
            new Return(new LoadField(field, new LoadArgument(0, "p", capturePointer))))).Output!;

        Assert.Contains("return p->X;", body);
        Assert.DoesNotContain("p.X", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M(CapturePoint* p)",
                body,
                "public struct CapturePoint { public int X; }"),
            body);
    }

    [Fact]
    public void Rung6PointerPropertiesIndexersAndMethods_RenderPointerSyntaxAndRecompile()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var p = new LoadArgument(0, "p", pointPointer);
        var property = new MethodRef(point, "get_P", Int32, [], HasThis: true);
        var indexer = new MethodRef(point, "get_Item", Int32, [Int32], HasThis: true);
        var method = new MethodRef(point, "M", Int32, [], HasThis: true);
        var extension = new MethodRef(TypeRef.Definition("Synthetic", "", "Extensions"), "ExtPtr", Int32, [pointPointer], HasThis: false)
        {
            IsExtension = MetadataFactState.Yes,
        };
        var sum = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new Binary(
                BinaryKind.Add,
                isChecked: false,
                isUnsigned: false,
                new Binary(
                    BinaryKind.Add,
                    isChecked: false,
                    isUnsigned: false,
                    new LoadProperty(property, p, []),
                    new LoadProperty(indexer, (IrExpression)p.Clone(), [new Constant(1, Int32)])),
                new Call(method, isVirtual: false, [(IrExpression)p.Clone()])),
            new Call(extension, isVirtual: false, [(IrExpression)p.Clone()]));
        var body = CSharpPrinter.Print(Function(
            "ReadPointerMembers",
            Int32,
            [new Parameter("p", pointPointer)],
            [],
            new Return(sum))).Output!;

        Assert.Contains("p->P", body);
        Assert.Contains("(*p)[1]", body);
        Assert.Contains("p->M()", body);
        Assert.Contains("Extensions.ExtPtr(p)", body);
        Assert.DoesNotContain("p.P", body);
        Assert.DoesNotContain("p[1]", body);
        Assert.DoesNotContain("p.M()", body);
        Assert.DoesNotContain("p.ExtPtr()", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M(Point* p)",
                body,
                """
                public unsafe struct Point
                {
                    public int X;
                    public int P { get; set; }
                    public int this[int i] => X + i;
                    public int M() => X;
                }
                public static unsafe class Extensions
                {
                    public static int ExtPtr(Point* p)
                    {
                        unsafe
                        {
                            return p->X;
                        }
                    }
                }
                """),
            body);
    }

    [Fact]
    public void Rung6PointerInterfaceMembers_RenderPointerSyntaxAndRecompile()
    {
        var typeParameter = TypeRef.GenericParameter(0, "T");
        var pointer = TypeRef.Pointer(typeParameter);
        var iface = TypeRef.Definition("Synthetic", "LadderRung6", "IPointLike", ValueTypeHint.ReferenceType);
        var p = new LoadArgument(0, "p", pointer);
        var property = new MethodRef(iface, "get_P", Int32, [], HasThis: true);
        var indexer = new MethodRef(iface, "get_Item", Int32, [Int32], HasThis: true);
        var method = new MethodRef(iface, "M", Int32, [], HasThis: true);
        var sum = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new Binary(
                BinaryKind.Add,
                isChecked: false,
                isUnsigned: false,
                new LoadProperty(property, p, []),
                new LoadProperty(indexer, (IrExpression)p.Clone(), [new Constant(1, Int32)])),
            new Call(method, isVirtual: true, [(IrExpression)p.Clone()]));
        var body = CSharpPrinter.Print(Function(
            "ReadInterfacePointerMembers",
            Int32,
            [new Parameter("p", pointer)],
            [],
            new Return(sum))).Output!;

        Assert.Contains("p->P", body);
        Assert.Contains("(*p)[1]", body);
        Assert.Contains("p->M()", body);
        Assert.DoesNotContain("p.P", body);
        Assert.DoesNotContain("p[1]", body);
        Assert.DoesNotContain("p.M()", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M<T>(T* p) where T : unmanaged, IPointLike",
                body,
                """
                public interface IPointLike
                {
                    int P { get; }
                    int this[int i] { get; }
                    int M();
                }
                """),
            body);
    }

    [Fact]
    public void Rung6PointerArithmeticReceiver_ParenthesizesBeforeMemberAccess()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var p = new LoadArgument(0, "p", pointPointer);
        var next = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            p,
            new Constant(4, Int32));
        var field = new FieldRef(point, "X", Int32);
        var indexer = new MethodRef(point, "get_Item", Int32, [Int32], HasThis: true);
        var method = new MethodRef(point, "M", Int32, [], HasThis: true);
        var sum = new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new Binary(
                BinaryKind.Add,
                isChecked: false,
                isUnsigned: false,
                new LoadField(field, next),
                new LoadProperty(indexer, (IrExpression)next.Clone(), [new Constant(1, Int32)])),
            new Call(method, isVirtual: false, [(IrExpression)next.Clone()]));
        var body = CSharpPrinter.Print(Function(
            "ReadPointerArithmeticReceiver",
            Int32,
            [new Parameter("p", pointPointer)],
            [],
            new Return(sum))).Output!;

        Assert.Contains("((Point*)((byte*)p + 4))->X", body);
        Assert.Contains("(*((Point*)((byte*)p + 4)))[1]", body);
        Assert.Contains("((Point*)((byte*)p + 4))->M()", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M(Point* p)",
                body,
                """
                public struct Point
                {
                    public int X;
                    public int this[int i] => X + i;
                    public int M() => X;
                }
                """),
            body);
    }

    [Fact]
    public void Rung6PointerCastReceiver_ParenthesizesBeforeMemberAccess()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var bytePointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));
        var p = new LoadArgument(0, "p", bytePointer);
        var cast = new Pipeline.Convert(pointPointer, isChecked: false, isUnsigned: false, p);
        var field = new FieldRef(point, "X", Int32);
        var body = CSharpPrinter.Print(Function(
            "ReadPointerCastReceiver",
            Int32,
            [new Parameter("p", bytePointer)],
            [],
            new Return(new LoadField(field, cast)))).Output!;

        Assert.Contains("((Point*)p)->X", body);
        Assert.DoesNotContain("(Point*)p->X", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M(byte* p)",
                body,
                "public struct Point { public int X; }"),
            body);
    }

    [Fact]
    public void Rung6PointerPrefixIncrementReceiver_ParenthesizesBeforeMemberAccess()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var p = new LoadArgument(0, "p", pointPointer);
        var increment = new IncrementDecrement(p, isIncrement: true, isPrefix: true);
        var method = new MethodRef(point, "M", Int32, [], HasThis: true);
        var body = CSharpPrinter.Print(Function(
            "ReadPointerIncrementReceiver",
            Int32,
            [new Parameter("p", pointPointer)],
            [],
            new Return(new Call(method, isVirtual: false, [increment])))).Output!;

        Assert.Contains("(++p)->M()", body);
        Assert.DoesNotContain("++p->M()", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M(Point* p)",
                body,
                "public struct Point { public int M() => 1; }"),
            body);
    }

    [Fact]
    public void Rung6PointerInheritedObjectMethod_RendersArrowAndRecompiles()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var toString = new MethodRef(TypeRef.CoreLib("System", "Object"), "ToString", String, [], HasThis: true);
        var body = CSharpPrinter.Print(Function(
            "PointerToString",
            String,
            [new Parameter("p", pointPointer)],
            [],
            new Return(new Call(toString, isVirtual: true, [new LoadArgument(0, "p", pointPointer)])))).Output!;

        Assert.Contains("return p->ToString();", body);
        Assert.DoesNotContain("p.ToString()", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe string M(Point* p)",
                body,
                "public struct Point { public int X; }"),
            body);
    }

    [Fact]
    public void Rung6PointerInheritedEnumMethod_RendersArrowAndRecompiles()
    {
        var enumType = TypeRef.Definition("Synthetic", "LadderRung6", "E", ValueTypeHint.ValueType);
        var enumPointer = TypeRef.Pointer(enumType);
        var toString = new MethodRef(TypeRef.CoreLib("System", "Enum"), "ToString", String, [], HasThis: true);
        var body = CSharpPrinter.Print(Function(
            "PointerEnumToString",
            String,
            [new Parameter("p", enumPointer)],
            [],
            new Return(new Call(toString, isVirtual: true, [new LoadArgument(0, "p", enumPointer)])))).Output!;

        Assert.Contains("return p->ToString();", body);
        Assert.DoesNotContain("p.ToString()", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe string M(E* p)",
                body,
                "public enum E { A }"),
            body);
    }

    [Fact]
    public void Rung6PointerRefExtensionMethod_RendersArrowAndRecompiles()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var extension = new MethodRef(TypeRef.Definition("Synthetic", "", "Extensions"), "ExtRef", Int32, [TypeRef.ByRef(point)], HasThis: false)
        {
            IsExtension = MetadataFactState.Yes,
        };
        var body = CSharpPrinter.Print(Function(
            "PointerRefExtension",
            Int32,
            [new Parameter("p", pointPointer)],
            [],
            new Return(new Call(extension, isVirtual: false, [new LoadArgument(0, "p", pointPointer)])))).Output!;

        Assert.Contains("return p->ExtRef();", body);
        Assert.DoesNotContain("Extensions.ExtRef(ref p)", body);
        Assert.DoesNotContain("p.ExtRef()", body);
        AssertNoErrors(
            RecompileNewRules(
                "static unsafe int M(Point* p)",
                body,
                """
                public struct Point { public int X; }
                public static class Extensions
                {
                    public static int ExtRef(this ref Point p) => p.X;
                }
                """),
            body);
    }

    [Fact]
    public void Rung6StaticPointerRefArguments_UseRuleSpecificUnsafeContexts()
    {
        var helpers = TypeRef.Definition("Synthetic", "", "Helpers");
        var byRefInt = TypeRef.ByRef(Int32);
        var takeRef = new MethodRef(helpers, "TakeRef", Void, [byRefInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
        };
        var takeIn = new MethodRef(helpers, "TakeIn", Int32, [byRefInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.In],
        };
        var takeLaterRef = new MethodRef(helpers, "TakeLaterRef", Void, [Int32, byRefInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Value, ArgumentRefKind.Ref],
        };
        var takeLaterIn = new MethodRef(helpers, "TakeLaterIn", Int32, [Int32, byRefInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Value, ArgumentRefKind.In],
        };
        var takeOut = new MethodRef(helpers, "TakeOut", Void, [byRefInt], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.Out],
        };

        const string declarations = """
            public static class Helpers
            {
                public static void TakeRef(ref int value) { }
                public static int TakeIn(in int value) => value;
                public static void TakeLaterRef(int unused, ref int value) { }
                public static int TakeLaterIn(int unused, in int value) => value;
                public static void TakeOut(out int value) => value = 0;
            }
            """;

        AssertCall(takeRef, () => [new LoadArgument(0, "p", IntPointer)], "Helpers.TakeRef(ref *p)");
        AssertCall(takeIn, () => [new LoadArgument(0, "p", IntPointer)], "Helpers.TakeIn(in *p)");
        AssertCall(
            takeLaterRef,
            () => [new Constant(0, Int32), new LoadArgument(0, "p", IntPointer)],
            "Helpers.TakeLaterRef(0, ref *p)");
        AssertCall(
            takeLaterIn,
            () => [new Constant(0, Int32), new LoadArgument(0, "p", IntPointer)],
            "Helpers.TakeLaterIn(0, in *p)");
        AssertCall(takeOut, () => [new LoadArgument(0, "p", IntPointer)], "Helpers.TakeOut(out *p)");

        void AssertCall(
            MethodRef method,
            Func<IReadOnlyList<IrExpression>> createArguments,
            string expected)
        {
            IrFunction CreateFunction() => Function(
                "StaticPointerRefArgument",
                Void,
                [new Parameter("p", IntPointer)],
                [],
                new ExpressionStatement(new Call(
                    method,
                    isVirtual: false,
                    createArguments())),
                new Return(null));

            var (updated, legacy) = PrintRulePair(CreateFunction);

            Assert.Contains(expected, updated);
            Assert.Contains("unsafe", updated);
            Assert.DoesNotContain("unsafe", legacy);
            AssertNoErrors(RecompileNewRules("static unsafe void M(int* p)", updated, declarations), updated);
            AssertNoErrors(RecompileLegacyRules("static unsafe void M(int* p)", legacy, declarations), legacy);
        }
    }

    [Fact]
    public void Rung6PointerRefArgumentsInOtherCalls_UseRuleSpecificUnsafeContexts()
    {
        var byRefInt = TypeRef.ByRef(Int32);
        var holder = TypeRef.Definition("Synthetic", "", "Holder");
        var instanceMethod = new MethodRef(holder, "Take", Void, [byRefInt], HasThis: true)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
        };
        AssertCall(
            () => Function(
                "InstancePointerRefArgument",
                Void,
                [
                    new Parameter("holder", holder),
                    new Parameter("p", IntPointer),
                ],
                [],
                new ExpressionStatement(new Call(
                    instanceMethod,
                    isVirtual: true,
                    [
                        new LoadArgument(0, "holder", holder),
                        new LoadArgument(1, "p", IntPointer),
                    ])),
                new Return(null)),
            "static unsafe void M(Holder holder, int* p)",
            "public sealed class Holder { public void Take(ref int value) { } }",
            "holder.Take(ref *p);");

        var constructor = new MethodRef(holder, ".ctor", Void, [byRefInt], HasThis: true)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
        };
        AssertCall(
            () => Function(
                "ConstructorPointerRefArgument",
                Void,
                [new Parameter("p", IntPointer)],
                [],
                new ExpressionStatement(new NewObject(
                    constructor,
                    [new LoadArgument(0, "p", IntPointer)])),
                new Return(null)),
            "static unsafe void M(int* p)",
            "public sealed class Holder { public Holder(ref int value) { } }",
            "new Holder(ref *p);");

        AssertCall(
            () => Function(
                "LocalFunctionPointerRefArgument",
                Void,
                [new Parameter("p", IntPointer)],
                [],
                new LocalFunctionStatement(
                    "Take",
                    Void,
                    [new Parameter("value", byRefInt)],
                    [ArgumentRefKind.Ref],
                    isStatic: true,
                    [],
                    [],
                    usesUpdatedMemorySafetyRules: true,
                    skipLocalsInit: false,
                    BlockContainer(new Return(null))),
                new ExpressionStatement(new LocalFunctionInvocation(
                    "Take",
                    Void,
                    [new LoadArgument(0, "p", IntPointer)],
                    [byRefInt],
                    [ArgumentRefKind.Ref])),
                new Return(null)),
            "static unsafe void M(int* p)",
            "",
            "Take(ref *p);");

        static void AssertCall(
            Func<IrFunction> createFunction,
            string methodHeader,
            string declarations,
            string expected)
        {
            var (updated, legacy) = PrintRulePair(createFunction);

            Assert.Contains(expected, updated);
            Assert.Contains("unsafe", updated);
            Assert.DoesNotContain("unsafe", legacy);
            AssertNoErrors(RecompileNewRules(methodHeader, updated, declarations), updated);
            AssertNoErrors(RecompileLegacyRules(methodHeader, legacy, declarations), legacy);
        }
    }

    [Fact]
    public void Rung6UpdatedPointerSignatureLocalFunctionCall_NeedsNoUnsafeContext()
    {
        IrFunction CreateFunction() => Function(
            "PointerSignatureLocalFunctionCall",
            Void,
            [new Parameter("p", IntPointer)],
            [],
            new LocalFunctionStatement(
                "Take",
                Void,
                [new Parameter("value", IntPointer)],
                isStatic: true,
                [],
                [],
                usesUpdatedMemorySafetyRules: true,
                skipLocalsInit: false,
                BlockContainer(new Return(null))),
            new ExpressionStatement(new LocalFunctionInvocation(
                "Take",
                Void,
                [new LoadArgument(0, "p", IntPointer)],
                [IntPointer],
                [ArgumentRefKind.Value])),
            new Return(null));

        var (updated, legacy) = PrintRulePair(CreateFunction);

        Assert.DoesNotContain("unsafe\n{\n    Take(p);", updated);
        Assert.DoesNotContain("unsafe\n{\n    Take(p);", legacy);
        AssertNoErrors(RecompileNewRules("static void M(int* p)", updated), updated);
        AssertNoErrors(RecompileLegacyRules("static unsafe void M(int* p)", legacy), legacy);
    }

    [Fact]
    public void Rung6RequiresUnsafeLocalFunction_RetainsModifierAndCallContext()
    {
        IrFunction CreateFunction() => Function(
            "RequiresUnsafeLocalFunction",
            Void,
            [],
            [],
            new LocalFunctionStatement(
                "Take",
                Void,
                [],
                isStatic: true,
                [],
                [],
                usesUpdatedMemorySafetyRules: true,
                skipLocalsInit: false,
                BlockContainer(new Return(null)),
                requiresUnsafe: true),
            new ExpressionStatement(new LocalFunctionInvocation(
                "Take",
                Void,
                [],
                [],
                [],
                requiresUnsafe: true)),
            new Return(null));

        var (updated, legacy) = PrintRulePair(CreateFunction);

        Assert.Contains("static unsafe void Take()", updated);
        Assert.Contains("unsafe\n{\n    Take();", updated);
        Assert.Contains("static unsafe void Take()", legacy);
        Assert.DoesNotContain("unsafe\n{\n    Take();", legacy);
        AssertNoErrors(RecompileNewRules("static void M()", updated), updated);
        AssertNoErrors(RecompileLegacyRules("static unsafe void M()", legacy), legacy);
    }

    [Fact]
    public void Rung6UnsafeLocalFunctionBody_KeepsDeclarationInCallerScope()
    {
        var helpers = TypeRef.Definition("Synthetic", "", "Helpers");
        var risky = new MethodRef(helpers, "Risky", Void, [], HasThis: false)
        {
            RequiresUnsafe = true,
        };

        IrFunction CreateFunction()
        {
            var localBody = BlockContainer(
                new ExpressionStatement(new Call(risky, isVirtual: false, [])),
                new Return(null));
            return Function(
                "UnsafeLocalFunctionBody",
                Void,
                [],
                [],
                new ExpressionStatement(new LocalFunctionInvocation("Take", Void, [])),
                new LocalFunctionStatement(
                    "Take",
                    Void,
                    [],
                    isStatic: true,
                    [],
                    [],
                    usesUpdatedMemorySafetyRules: true,
                    skipLocalsInit: false,
                    localBody),
                new Return(null));
        }

        var (updated, legacy) = PrintRulePair(CreateFunction);
        const string declarations =
            "public static class Helpers { public static unsafe void Risky() { } }";

        Assert.Contains("Take();\nstatic void Take()", updated);
        Assert.Contains("static void Take()\n{\n    unsafe", updated);
        Assert.DoesNotContain("unsafe\n{\n    static void Take()", updated);
        AssertNoErrors(RecompileNewRules("static void M()", updated, declarations), updated);
        AssertNoErrors(RecompileLegacyRules("static unsafe void M()", legacy, declarations), legacy);
    }

    [Fact]
    public void Rung6UnsafeEvaluationBeforeAwait_UsesExplicitBlockUnderBothRuleSets()
    {
        var holder = TypeRef.Definition("Synthetic", "", "Holder");
        var intPointer = TypeRef.Pointer(Int32);
        var taskOfInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Threading.Tasks", "Task`1"),
            [Int32]);
        var getter = new MethodRef(holder, "get_Risky", intPointer, [], HasThis: true)
        {
            RequiresUnsafe = true,
        };
        var fromResult = new MethodRef(
            TypeRef.CoreLib("System.Threading.Tasks", "Task"),
            "FromResult",
            taskOfInt,
            [Int32],
            HasThis: false);

        IrFunction CreateFunction()
        {
            var function = Function(
                "UnsafeEvaluationBeforeAwait",
                taskOfInt,
                [new Parameter("holder", holder)],
                [Int32],
                new StoreLocal(
                    0,
                    Int32,
                    new LoadIndirect(
                        Int32,
                        new LoadProperty(
                            getter,
                            new LoadArgument(0, "holder", holder),
                            []))),
                new Return(new AwaitExpression(
                    new Call(
                        fromResult,
                        isVirtual: false,
                        [new LoadLocal(0, Int32)]),
                    Int32)));
            function.RequiresAsyncBodyModifier = true;
            return function;
        }

        var updatedFunction = CreateFunction();
        updatedFunction.UsesUpdatedMemorySafetyRules = true;
        var updated = CSharpPrinter.Print(updatedFunction);
        var legacyFunction = CreateFunction();
        legacyFunction.UsesUpdatedMemorySafetyRules = false;
        var legacy = CSharpPrinter.Print(legacyFunction);
        const string declarations =
            "using System.Threading.Tasks; "
            + "public sealed class Holder { public unsafe int* Risky => (int*)0; }";
        const string header =
            "static async System.Threading.Tasks.Task<int> M(Holder holder)";

        Assert.False(updated.RequiresUnsafeBodyModifier);
        Assert.False(legacy.RequiresUnsafeBodyModifier);
        Assert.Contains("unsafe\n{", updated.Output);
        Assert.Contains("unsafe\n{", legacy.Output);
        Assert.DoesNotContain("unsafe\n{\n    return await", updated.Output);
        Assert.DoesNotContain("unsafe\n{\n    return await", legacy.Output);
        AssertNoErrors(RecompileNewRules(header, updated.Output!, declarations), updated.Output!);
        AssertNoErrors(RecompileLegacyRules(header, legacy.Output!, declarations), legacy.Output!);
    }

    [Fact]
    public void Rung6AwaitBeforeUnsafeConsumer_PreservesSafeBoundary()
    {
        var taskOfInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Threading.Tasks", "Task`1"),
            [Int32]);
        var risky = new MethodRef(
            TypeRef.Definition("Synthetic", "", "Helpers"),
            "Risky",
            Int32,
            [Int32],
            HasThis: false)
        {
            RequiresUnsafe = true,
        };

        IrFunction CreateFunction()
        {
            var function = Function(
                "AwaitBeforeUnsafeConsumer",
                taskOfInt,
                [new Parameter("task", taskOfInt)],
                [Int32],
                new StoreLocal(
                    0,
                    Int32,
                    new AwaitExpression(
                        new LoadArgument(0, "task", taskOfInt),
                        Int32)),
                new Return(new Call(
                    risky,
                    isVirtual: false,
                    [new LoadLocal(0, Int32)])));
            function.RequiresAsyncBodyModifier = true;
            new ExpressionInliningPass().Run(function, PassContext.None);
            return function;
        }

        var updatedFunction = CreateFunction();
        updatedFunction.UsesUpdatedMemorySafetyRules = true;
        var updated = CSharpPrinter.Print(updatedFunction);
        var legacyFunction = CreateFunction();
        legacyFunction.UsesUpdatedMemorySafetyRules = false;
        var legacy = CSharpPrinter.Print(legacyFunction);
        const string declarations =
            "using System.Threading.Tasks; "
            + "public static class Helpers { public static unsafe int Risky(int value) => value; }";
        const string header =
            "static async System.Threading.Tasks.Task<int> M(System.Threading.Tasks.Task<int> task)";

        Assert.Contains("int V_0 = await task;", updated.Output);
        Assert.Contains("int V_0 = await task;", legacy.Output);
        Assert.DoesNotContain("Risky(await", updated.Output);
        Assert.DoesNotContain("Risky(await", legacy.Output);
        AssertNoErrors(RecompileNewRules(header, updated.Output!, declarations), updated.Output!);
        AssertNoErrors(RecompileLegacyRules(header, legacy.Output!, declarations), legacy.Output!);
    }

    [Fact]
    public void Rung6UnsafeHeaderBeforeAwaitingBody_PreservesSafeBoundary()
    {
        var taskOfInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Threading.Tasks", "Task`1"),
            [Int32]);
        var boolean = TypeRef.CoreLib("System", "Boolean");
        var holder = TypeRef.Definition("Synthetic", "", "Holder");
        var risky = new MethodRef(
            holder,
            "get_Risky",
            boolean,
            [],
            HasThis: true)
        {
            RequiresUnsafe = true,
        };
        var fromResult = new MethodRef(
            TypeRef.CoreLib("System.Threading.Tasks", "Task"),
            "FromResult",
            taskOfInt,
            [Int32],
            HasThis: false);

        IrFunction CreateFunction()
        {
            var thenArm = new Block();
            thenArm.Add(new ExpressionStatement(new AwaitExpression(
                new Call(
                    fromResult,
                    isVirtual: false,
                    [new Constant(1, Int32)]),
                Int32)));
            var function = Function(
                "UnsafeHeaderBeforeAwaitingBody",
                taskOfInt,
                [new Parameter("holder", holder)],
                [boolean],
                new StoreLocal(
                    0,
                    boolean,
                    new LoadProperty(
                        risky,
                        new LoadArgument(0, "holder", holder),
                        [])),
                new IfStatement(
                    new LoadLocal(0, boolean),
                    thenArm,
                    elseArm: null),
                new Return(new AwaitExpression(
                    new Call(
                        fromResult,
                        isVirtual: false,
                        [new Constant(2, Int32)]),
                    Int32)));
            function.RequiresAsyncBodyModifier = true;
            new ExpressionInliningPass().Run(function, PassContext.None);
            return function;
        }

        var updatedFunction = CreateFunction();
        updatedFunction.UsesUpdatedMemorySafetyRules = true;
        var updated = CSharpPrinter.Print(updatedFunction);
        var legacyFunction = CreateFunction();
        legacyFunction.UsesUpdatedMemorySafetyRules = false;
        var legacy = CSharpPrinter.Print(legacyFunction);
        const string declarations =
            "using System.Threading.Tasks; "
            + "public sealed class Holder { public unsafe bool Risky => true; }";
        const string header =
            "static async System.Threading.Tasks.Task<int> M(Holder holder)";

        Assert.Contains("if (V_0)", updated.Output);
        Assert.Contains("if (V_0)", legacy.Output);
        Assert.DoesNotContain("if (holder.Risky)", updated.Output);
        Assert.DoesNotContain("if (holder.Risky)", legacy.Output);
        Assert.DoesNotContain("unsafe\n{\n    if", updated.Output);
        Assert.DoesNotContain("unsafe\n{\n    if", legacy.Output);
        AssertNoErrors(RecompileNewRules(header, updated.Output!, declarations), updated.Output!);
        AssertNoErrors(RecompileLegacyRules(header, legacy.Output!, declarations), legacy.Output!);
    }

    [Fact]
    public void Rung6LegacyAsyncLocalFunction_UsesExplicitInnerBlock()
    {
        var task = TypeRef.CoreLib("System.Threading.Tasks", "Task");
        var yieldAwaitable = TypeRef.CoreLib(
            "System.Runtime.CompilerServices",
            "YieldAwaitable");
        var yield = new MethodRef(
            TypeRef.CoreLib("System.Threading.Tasks", "Task"),
            "Yield",
            yieldAwaitable,
            [],
            HasThis: false);

        IrFunction CreateFunction()
        {
            var function = Function(
                "LegacyAsyncLocalFunction",
                task,
                [],
                [],
                new ExpressionStatement(new AwaitExpression(
                    new Call(yield, isVirtual: false, []),
                    Void)),
                new LocalFunctionStatement(
                    "Read",
                    Int32,
                    [new Parameter("pointer", IntPointer)],
                    [ArgumentRefKind.Value],
                    isStatic: true,
                    [],
                    [],
                    usesUpdatedMemorySafetyRules: false,
                    skipLocalsInit: false,
                    BlockContainer(new Return(new LoadIndirect(
                        Int32,
                        new LoadArgument(0, "pointer", IntPointer))))),
                new Return(null));
            function.RequiresAsyncBodyModifier = true;
            return function;
        }

        var updatedFunction = CreateFunction();
        updatedFunction.UsesUpdatedMemorySafetyRules = true;
        var updated = CSharpPrinter.Print(updatedFunction);
        var legacyFunction = CreateFunction();
        legacyFunction.UsesUpdatedMemorySafetyRules = false;
        var legacy = CSharpPrinter.Print(legacyFunction);
        const string declarations = "using System.Threading.Tasks;";
        const string header = "static async System.Threading.Tasks.Task M()";

        Assert.Contains("static int Read(int* pointer)\n{\n    unsafe", updated.Output);
        Assert.Contains("static int Read(int* pointer)\n{\n    unsafe", legacy.Output);
        AssertNoErrors(RecompileNewRules(header, updated.Output!, declarations), updated.Output!);
        AssertNoErrors(RecompileLegacyRules(header, legacy.Output!, declarations), legacy.Output!);
    }

    [Fact]
    public void Rung6LocalFunctionRefReturn_UsesLocalReturnTypeForUnsafeBlock()
    {
        var refInt = TypeRef.ByRef(Int32);

        IrFunction CreateFunction() => Function(
            "LocalFunctionRefReturn",
            Void,
            [],
            [],
            new LocalFunctionStatement(
                "Reference",
                refInt,
                [new Parameter("p", IntPointer)],
                isStatic: true,
                [],
                [],
                usesUpdatedMemorySafetyRules: true,
                skipLocalsInit: false,
                BlockContainer(new Return(new LoadArgument(0, "p", IntPointer)))),
            new Return(null));

        var (updated, legacy) = PrintRulePair(CreateFunction);

        Assert.Contains("unsafe\n    {\n        return ref *p;\n    }", updated);
        Assert.Contains("=> ref *p;", legacy);
        Assert.DoesNotContain("unsafe\n    {\n        return ref *p;\n    }", legacy);
        AssertNoErrors(RecompileNewRules("static void M()", updated), updated);
        AssertNoErrors(RecompileLegacyRules("static unsafe void M()", legacy), legacy);
    }

    [Fact]
    public void Rung6LegacyPointerDeclaration_RequiresMemberUnsafeModifier()
    {
        IrFunction CreateFunction(bool updatedRules, IrNode statement, ImmutableArray<TypeRef> locals)
        {
            var function = Function(
                "PointerDeclaration",
                Void,
                [],
                locals,
                statement,
                new Return(null));
            function.UsesUpdatedMemorySafetyRules = updatedRules;
            return function;
        }

        AssertRuleDifference(
            new StoreLocal(0, IntPointer, new Constant(null, IntPointer)),
            [IntPointer]);
        AssertRuleDifference(
            new StoreLocal(0, Int32, new SizeOf(IntPointer)),
            [Int32]);
        AssertRuleDifference(
            new ExpressionStatement(new ILInspector.Decompiler.Pipeline.Convert(
                TypeRef.CoreLib("System", "UIntPtr"),
                isChecked: false,
                isUnsigned: false,
                new LoadLocalAddress(0, Int32))),
            [Int32]);
        AssertRuleDifference(
            new Fixed(
                Int32,
                localIndex: 0,
                new Constant(null, TypeRef.ByRef(Int32)),
                BlockContainer()),
            [TypeRef.ByRef(Int32)]);
        var functionPointer = TypeRef.FunctionPointer(Void, [], "");
        AssertRuleDifference(
            new StoreField(
                new FieldRef(TypeRef.Definition("Synthetic", "", "Holder"), "Target", functionPointer),
                instance: null,
                new AddressOfMethod(
                    new MethodRef(
                        TypeRef.Definition("Synthetic", "", "Holder"),
                        "Method",
                        Void,
                        [],
                        HasThis: false),
                    functionPointer)),
            []);

        void AssertRuleDifference(IrNode statement, ImmutableArray<TypeRef> locals)
        {
            var updated = CSharpPrinter.Print(CreateFunction(
                updatedRules: true,
                (IrNode)statement.Clone(),
                locals));
            var legacy = CSharpPrinter.Print(CreateFunction(
                updatedRules: false,
                statement,
                locals));

            Assert.False(updated.RequiresUnsafeBodyModifier);
            Assert.True(legacy.RequiresUnsafeBodyModifier);
        }
    }

    [Fact]
    public void Rung6PointerNullCoalescingFieldStatement_UsesRuleSpecificUnsafeContexts()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [Int32]);
        var field = new FieldRef(point, "Field", nullableInt);

        IrFunction CreateFunction() => Function(
            "PointerNullCoalescingField",
            Void,
            [new Parameter("p", pointPointer)],
            [],
            new NullCoalescingFieldAssignment(
                field,
                new LoadArgument(0, "p", pointPointer),
                new Constant(1, Int32)),
            new Return(null));

        var (updated, legacy) = PrintRulePair(CreateFunction);

        Assert.Contains("p->Field ??= 1;", updated);
        Assert.Contains("unsafe", updated);
        Assert.DoesNotContain("unsafe", legacy);
        const string declarations = "public struct Point { public int? Field; }";
        AssertNoErrors(RecompileNewRules("static unsafe void M(Point* p)", updated, declarations), updated);
        AssertNoErrors(RecompileLegacyRules("static unsafe void M(Point* p)", legacy, declarations), legacy);
    }

    [Fact]
    public void Rung6PointerNullCoalescingFieldExpression_UsesRuleSpecificUnsafeContexts()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [Int32]);
        var field = new FieldRef(point, "Field", nullableInt);

        IrFunction CreateFunction() => Function(
            "PointerNullCoalescingFieldExpression",
            nullableInt,
            [new Parameter("p", pointPointer)],
            [],
            new Return(new NullCoalescingFieldAssignmentExpression(
                field,
                new LoadArgument(0, "p", pointPointer),
                new Constant(1, Int32))));

        var (updated, legacy) = PrintRulePair(CreateFunction);

        Assert.Contains("return p->Field ??= 1;", updated);
        Assert.Contains("unsafe", updated);
        Assert.DoesNotContain("unsafe", legacy);
        const string declarations = "public struct Point { public int? Field; }";
        AssertNoErrors(RecompileNewRules("static unsafe int? M(Point* p)", updated, declarations), updated);
        AssertNoErrors(RecompileLegacyRules("static unsafe int? M(Point* p)", legacy, declarations), legacy);
    }

    [Fact]
    public void Rung6PointerNullCoalescingProperty_UsesRuleSpecificUnsafeContexts()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [Int32]);
        var setter = new MethodRef(point, "set_Property", Void, [nullableInt], HasThis: true);

        IrFunction CreateFunction() => Function(
            "PointerNullCoalescingProperty",
            Void,
            [new Parameter("p", pointPointer)],
            [],
            new NullCoalescingPropertyAssignment(
                setter,
                new LoadArgument(0, "p", pointPointer),
                [],
                new Constant(2, Int32),
                isVirtual: false),
            new Return(null));

        var (updated, legacy) = PrintRulePair(CreateFunction);

        Assert.Contains("p->Property ??= 2;", updated);
        Assert.Contains("unsafe", updated);
        Assert.DoesNotContain("unsafe", legacy);
        const string declarations = "public struct Point { public int? Property { get; set; } }";
        AssertNoErrors(RecompileNewRules("static unsafe void M(Point* p)", updated, declarations), updated);
        AssertNoErrors(RecompileLegacyRules("static unsafe void M(Point* p)", legacy, declarations), legacy);
    }

    [Fact]
    public void Rung6PointerPropertyDeconstruction_UsesRuleSpecificUnsafeContexts()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [Int32]);
        var tuple = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "ValueTuple`2"),
            [nullableInt, Int32]);
        var setter = new MethodRef(point, "set_Property", Void, [nullableInt], HasThis: true);

        IrFunction CreateFunction() => Function(
            "PointerPropertyDeconstruction",
            Void,
            [
                new Parameter("p", pointPointer),
                new Parameter("tuple", tuple),
            ],
            [Int32],
            new DeconstructionAssignment(
                [
                    DeconstructionTarget.Property(
                        setter,
                        new LoadArgument(0, "p", pointPointer),
                        [],
                        isVirtual: false),
                    DeconstructionTarget.Local(0, Int32, isDeclared: false),
                ],
                new LoadArgument(1, "tuple", tuple)),
            new Return(null));

        var (updated, legacy) = PrintRulePair(CreateFunction);

        Assert.Contains("(p->Property,", updated);
        Assert.Contains("unsafe", updated);
        Assert.DoesNotContain("unsafe", legacy);
        const string declarations = """
            public struct Point
            {
                public int? Property { get; set; }
            }
            """;
        AssertNoErrors(
            RecompileNewRules("static unsafe void M(Point* p, (int?, int) tuple)", updated, declarations),
            updated);
        AssertNoErrors(
            RecompileLegacyRules("static unsafe void M(Point* p, (int?, int) tuple)", legacy, declarations),
            legacy);
    }

    [Fact]
    public void Rung6RequiresUnsafeDelegateCreation_UsesRuleSpecificUnsafeContexts()
    {
        var action = TypeRef.CoreLib("System", "Action");
        var helpers = TypeRef.Definition("Synthetic", "", "Helpers");
        var risky = new MethodRef(helpers, "Risky", Void, [], HasThis: false)
        {
            RequiresUnsafe = true,
        };

        IrFunction CreateFunction() => Function(
            "RequiresUnsafeDelegateCreation",
            Void,
            [],
            [action],
            new StoreLocal(
                0,
                action,
                new DelegateCreation(
                    action,
                    risky,
                    isVirtual: false,
                    new Constant(null, TypeRef.CoreLib("System", "Object")))),
            new Return(null));

        var (updated, legacy) = PrintRulePair(CreateFunction);

        Assert.Contains("new Action(Helpers.Risky)", updated);
        Assert.Contains("unsafe", updated);
        Assert.DoesNotContain("unsafe", legacy);
        const string declarations = """
            public static class Helpers
            {
                public static unsafe void Risky() { }
            }
            """;
        AssertNoErrors(RecompileNewRules("static void M()", updated, declarations), updated);
        AssertNoErrors(RecompileLegacyRules("static void M()", legacy, declarations), legacy);
    }

    [Fact]
    public void Rung6RaisedRequiresUnsafeMembers_UseRuleSpecificUnsafeContexts()
    {
        var helpers = TypeRef.Definition("Synthetic", "", "Helpers");
        var risky = RequiresUnsafe(new MethodRef(helpers, "Risky", Void, [], HasThis: false));
        var functionPointer = TypeRef.FunctionPointer(Void, [], "");
        AssertRaised(
            () => new StoreLocal(0, functionPointer, new AddressOfMethod(risky, functionPointer)),
            [],
            [functionPointer],
            "static unsafe void M()",
            "public static class Helpers { public static unsafe void Risky() { } }",
            "&Helpers.Risky");

        var counter = TypeRef.Definition("Synthetic", "", "Counter", ValueTypeHint.ValueType);
        var increment = RequiresUnsafe(new MethodRef(counter, "op_Increment", counter, [counter], HasThis: false));
        AssertRaised(
            () => new ExpressionStatement(new IncrementDecrement(
                new LoadArgument(0, "value", counter),
                isIncrement: true,
                isPrefix: false,
                isUserDefined: true,
                consumedMethod: increment)),
            [new Parameter("value", counter)],
            [],
            "static unsafe void M(Counter value)",
            """
            public struct Counter
            {
                public static unsafe Counter operator ++(Counter value) => value;
            }
            """,
            "value++;");

        var holder = TypeRef.Definition("Synthetic", "", "Holder");
        var holderConstructor = new MethodRef(holder, ".ctor", Void, [], HasThis: true);
        var holderSetter = RequiresUnsafe(new MethodRef(holder, "set_P", Void, [Int32], HasThis: true));
        AssertRaised(
            () => new ExpressionStatement(new ObjectInitializerExpression(
                new NewObject(holderConstructor, []),
                isCollection: false,
                [new InitializerEntry("P", [new Constant(1, Int32)], holderSetter)])),
            [],
            [],
            "static unsafe void M()",
            """
            public sealed class Holder
            {
                public unsafe int P { get; set; }
            }
            """,
            "new Holder");

        var staticSetter = RequiresUnsafe(new MethodRef(helpers, "set_A", Void, [Int32], HasThis: false));
        var safeStaticSetter = new MethodRef(helpers, "set_B", Void, [Int32], HasThis: false);
        AssertRaised(
            () => new ChainedAssignment(
                [
                    ChainedAssignmentTarget.StaticProperty(staticSetter, isVirtual: false),
                    ChainedAssignmentTarget.StaticProperty(safeStaticSetter, isVirtual: false),
                ],
                new Constant(1, Int32)),
            [],
            [],
            "static unsafe void M()",
            """
            public static class Helpers
            {
                public static unsafe int A { get; set; }
                public static int B { get; set; }
            }
            """,
            "Helpers.A = Helpers.B = 1;");

        var pair = TypeRef.Definition("Synthetic", "", "Pair");
        var deconstruct = RequiresUnsafe(new MethodRef(
            pair,
            "Deconstruct",
            Void,
            [TypeRef.ByRef(Int32), TypeRef.ByRef(Int32)],
            HasThis: true));
        AssertRaised(
            () => new DeconstructionAssignment(
                [
                    DeconstructionTarget.Local(0, Int32, isDeclared: true),
                    DeconstructionTarget.Local(1, Int32, isDeclared: true),
                ],
                new LoadArgument(0, "pair", pair),
                deconstruct),
            [new Parameter("pair", pair)],
            [Int32, Int32],
            "static unsafe void M(Pair pair)",
            """
            public sealed class Pair
            {
                public unsafe void Deconstruct(out int first, out int second)
                    => (first, second) = (1, 2);
            }
            """,
            "(int");

        AssertRaised(
            () => new StoreLocal(
                0,
                TypeRef.CoreLib("System", "Boolean"),
                new PositionalPattern(
                    new LoadArgument(0, "pair", pair),
                    [
                        new PositionalPatternSubpattern(ComparisonKind.Equal),
                        new PositionalPatternSubpattern(ComparisonKind.GreaterThan),
                    ],
                    [new Constant(1, Int32), new Constant(0, Int32)],
                    deconstruct)),
            [new Parameter("pair", pair)],
            [TypeRef.CoreLib("System", "Boolean")],
            "static unsafe void M(Pair pair)",
            """
            public sealed class Pair
            {
                public unsafe void Deconstruct(out int first, out int second)
                    => (first, second) = (1, 2);
            }
            """,
            "pair is");

        var patternAccessor = RequiresUnsafe(new MethodRef(holder, "get_P", Int32, [], HasThis: true));
        AssertRaised(
            () => new ExpressionStatement(new RecursivePropertyDeclarationPattern(
                new LoadArgument(0, "value", holder),
                patternAccessor,
                Int32,
                localIndex: 0)),
            [new Parameter("value", holder)],
            [Int32],
            "static unsafe void M(Holder value)",
            """
            public sealed class Holder
            {
                public unsafe int P => 0;
            }
            """,
            "value is { P: int");

        AssertRaised(
            () => new ExpressionStatement(new PatternSwitchExpression(
                new LoadArgument(0, "value", holder),
                [
                    new PatternSwitchExpressionArm(
                        holder,
                        localIndex: null,
                        new PropertySubpattern(patternAccessor, Int32, LocalIndex: 0),
                        new Constant(1, Int32)),
                ],
                new Constant(0, Int32))),
            [new Parameter("value", holder)],
            [Int32],
            "static unsafe void M(Holder value)",
            """
            public sealed class Holder
            {
                public unsafe int P => 0;
            }
            """,
            "switch");

        var item = TypeRef.Definition("Synthetic", "", "Item");
        var itemSetter = RequiresUnsafe(new MethodRef(item, "set_P", Void, [Int32], HasThis: true));
        AssertRaised(
            () => new ExpressionStatement(new WithExpression(
                new LoadArgument(0, "value", item),
                [new InitializerEntry("P", [new Constant(1, Int32)], itemSetter)])),
            [new Parameter("value", item)],
            [],
            "static unsafe void M(Item value)",
            """
            public sealed record Item
            {
                public unsafe int P { get; init; }
            }
            """,
            "value with");

        var outer = TypeRef.Definition("Synthetic", "", "Outer");
        var inner = TypeRef.Definition("Synthetic", "", "Inner");
        var outerConstructor = new MethodRef(outer, ".ctor", Void, [], HasThis: true);
        var innerSetter = RequiresUnsafe(new MethodRef(inner, "set_P", Void, [Int32], HasThis: true));
        AssertRaised(
            () =>
            {
                var nested = new InitializerBlock(
                    isCollection: false,
                    [new InitializerEntry("P", [new Constant(1, Int32)], innerSetter)]);
                return new ExpressionStatement(new ObjectInitializerExpression(
                    new NewObject(outerConstructor, []),
                    isCollection: false,
                    [new InitializerEntry("Inner", [nested])]));
            },
            [],
            [],
            "static unsafe void M()",
            """
            public sealed class Outer
            {
                public Inner Inner { get; } = new();
            }
            public sealed class Inner
            {
                public unsafe int P { get; set; }
            }
            """,
            "Inner =");

        var resource = TypeRef.Definition("Synthetic", "", "Resource", ValueTypeHint.ValueType);
        var resourceConstructor = new MethodRef(resource, ".ctor", Void, [], HasThis: true);
        var dispose = RequiresUnsafe(new MethodRef(resource, "Dispose", Void, [], HasThis: true));
        AssertRaised(
            () => new UsingStatement(
                0,
                resource,
                new NewObject(resourceConstructor, []),
                BlockContainer(),
                consumedMemberRefs: [dispose]),
            [],
            [resource],
            "static unsafe void M()",
            """
            public ref struct Resource
            {
                public unsafe void Dispose() { }
            }
            """,
            "using");

        var collection = TypeRef.Definition("Synthetic", "", "Collection");
        var getEnumerator = RequiresUnsafe(new MethodRef(
            collection,
            "GetEnumerator",
            TypeRef.Definition("Synthetic", "", "Enumerator", ValueTypeHint.ValueType),
            [],
            HasThis: true));
        AssertRaised(
            () => new ForeachStatement(
                0,
                Int32,
                new LoadArgument(0, "items", collection),
                new Block(1),
                consumedMemberRefs: [getEnumerator]),
            [new Parameter("items", collection)],
            [Int32],
            "static unsafe void M(Collection items)",
            """
            public sealed class Collection
            {
                public unsafe Enumerator GetEnumerator() => new();
            }
            public struct Enumerator
            {
                public int Current => 0;
                public bool MoveNext() => false;
            }
            """,
            "foreach");

        static MethodRef RequiresUnsafe(MethodRef method)
            => method with { RequiresUnsafe = true };

        static void AssertRaised(
            Func<IrNode> createStatement,
            ImmutableArray<Parameter> parameters,
            ImmutableArray<TypeRef> locals,
            string methodHeader,
            string declarations,
            string expected)
        {
            IrFunction CreateFunction() => Function(
                "RaisedRequiresUnsafeMember",
                Void,
                parameters,
                locals,
                createStatement(),
                new Return(null));

            var (updated, legacy) = PrintRulePair(CreateFunction);

            Assert.Contains(expected, updated);
            Assert.Contains("unsafe", updated);
            Assert.DoesNotContain("unsafe", legacy);
            AssertNoErrors(RecompileNewRules(methodHeader, updated, declarations), updated);
            AssertNoErrors(RecompileLegacyRules(methodHeader, legacy, declarations), legacy);
        }
    }

    [Fact]
    public void Rung6PointerRefBindings_UseRuleSpecificUnsafeContexts()
    {
        var byRefInt = TypeRef.ByRef(Int32);

        IrFunction CreateDeclaration() => Function(
            "PointerRefLocalDeclaration",
            Void,
            [new Parameter("p", IntPointer)],
            [byRefInt],
            new StoreLocal(0, byRefInt, new LoadArgument(0, "p", IntPointer)),
            new StoreIndirect(Int32, new LoadLocal(0, byRefInt), new Constant(9, Int32)),
            new Return(null));

        IrFunction CreateRebind() => Function(
            "PointerRefLocalRebind",
            Void,
            [
                new Parameter("seed", byRefInt),
                new Parameter("p", IntPointer),
            ],
            [byRefInt],
            new StoreLocal(0, byRefInt, new LoadArgument(0, "seed", byRefInt)),
            new StoreLocal(0, byRefInt, new LoadArgument(1, "p", IntPointer)),
            new Return(null));

        IrFunction CreateReturn() => Function(
            "PointerRefReturn",
            byRefInt,
            [new Parameter("p", IntPointer)],
            [],
            new Return(new LoadArgument(0, "p", IntPointer)));

        AssertBinding(
            CreateDeclaration,
            "static unsafe void M(int* p)",
            "ref int V_0 = ref *p;");
        AssertBinding(
            CreateRebind,
            "static unsafe void M(ref int seed, int* p)",
            "V_0 = ref *p;");
        AssertBinding(
            CreateReturn,
            "static unsafe ref int M(int* p)",
            "return ref *p;");

        static void AssertBinding(
            Func<IrFunction> createFunction,
            string methodHeader,
            string expected)
        {
            var (updated, legacy) = PrintRulePair(createFunction);

            Assert.Contains(expected, updated);
            Assert.Contains("unsafe", updated);
            Assert.DoesNotContain("unsafe", legacy);
            AssertNoErrors(RecompileNewRules(methodHeader, updated), updated);
            AssertNoErrors(RecompileLegacyRules(methodHeader, legacy), legacy);
        }
    }

    [Fact]
    public void Rung6ForeachBodyUnsafeOperation_WrapsOnlyTheBody()
    {
        var collection = TypeRef.Definition("Synthetic", "LadderRung6", "Collection");

        IrFunction CreateFunction()
        {
            var loopBody = new Block(1);
            loopBody.Add(new StoreIndirect(
                Int32,
                new LoadArgument(1, "p", IntPointer),
                new Constant(1, Int32)));
            return Function(
                "ForeachBodyUnsafeOperation",
                Void,
                [
                    new Parameter("items", collection),
                    new Parameter("p", IntPointer),
                ],
                [Int32],
                new ForeachStatement(
                    0,
                    Int32,
                    new LoadArgument(0, "items", collection),
                    loopBody),
                new Return(null));
        }

        var (updated, legacy) = PrintRulePair(CreateFunction);

        int foreachIndex = updated.IndexOf("foreach", StringComparison.Ordinal);
        int unsafeIndex = updated.IndexOf("unsafe", StringComparison.Ordinal);
        Assert.True(foreachIndex >= 0 && unsafeIndex > foreachIndex, updated);
        Assert.DoesNotContain("unsafe", updated[..foreachIndex]);
        Assert.Contains("*p = 1;", FirstUnsafeBlockBody(updated));
        Assert.DoesNotContain("unsafe", legacy);
        const string declarations = """
            public sealed class Collection
            {
                public Enumerator GetEnumerator() => new();
            }
            public struct Enumerator
            {
                public int Current => 0;
                public bool MoveNext() => false;
            }
            """;
        AssertNoErrors(
            RecompileNewRules("static unsafe void M(Collection items, int* p)", updated, declarations),
            updated);
        AssertNoErrors(
            RecompileLegacyRules("static unsafe void M(Collection items, int* p)", legacy, declarations),
            legacy);
    }

    [Fact]
    public void Rung6PrimitivePointerOperations_OnlyRequireLegacyBodyModifier()
    {
        IrFunction CreateFunction() => Function(
            "PrimitivePointerOperations",
            Void,
            [],
            [IntPointer],
            new StoreLocal(0, IntPointer, new Constant(null, IntPointer)),
            new ExpressionStatement(new IncrementDecrement(
                new LoadLocal(0, IntPointer),
                isIncrement: true,
                isPrefix: false)),
            new Return(null));

        var updatedFunction = CreateFunction();
        updatedFunction.UsesUpdatedMemorySafetyRules = true;
        var updated = CSharpPrinter.Print(updatedFunction);
        var legacyFunction = CreateFunction();
        legacyFunction.UsesUpdatedMemorySafetyRules = false;
        var legacy = CSharpPrinter.Print(legacyFunction);

        Assert.Contains("V_0++;", updated.Output);
        Assert.DoesNotContain("unsafe", updated.Output);
        Assert.False(updated.RequiresUnsafeBodyModifier);
        Assert.DoesNotContain("unsafe", legacy.Output);
        Assert.True(legacy.RequiresUnsafeBodyModifier);
        AssertNoErrors(RecompileNewRules("static void M()", updated.Output!), updated.Output!);
        AssertNoErrors(RecompileLegacyRules("static unsafe void M()", legacy.Output!), legacy.Output!);
    }

    [Fact]
    public void Rung6PointerReceiver_DoesNotRaiseNullConditional()
    {
        var point = TypeRef.Definition("Synthetic", "LadderRung6", "Point", ValueTypeHint.ValueType);
        var pointPointer = TypeRef.Pointer(point);
        var name = new FieldRef(point, "Name", String);
        var p = new LoadArgument(0, "p", pointPointer);
        var conditional = new Conditional(
            p,
            new LoadField(name, (IrExpression)p.Clone()),
            new Constant(null, String))
        {
            MergedType = String,
        };
        var function = Function(
            "PointerNullConditionalDeclines",
            String,
            [new Parameter("p", pointPointer)],
            [],
            new Return(conditional));

        new NullConditionalPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NullConditional>());
    }

    [Fact]
    public void Rung6ByRefFixture_PreservesRefKinds()
    {
        using var pe = new PEReader(File.OpenRead(ByRefFixturePath));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == ByRefFixtureType);

        var readOnlyReturn = Assert.Single(type.Members, m => m.Name == "SelectReadonlyRef");
        Assert.Equal("ref readonly int SelectReadonlyRef(bool useLeft, in int left, in int right)", readOnlyReturn.Signature);

        var writableReturn = Assert.Single(type.Members, m => m.Name == "SelectRef");
        Assert.Equal("ref int SelectRef(bool useLeft, ref int left, ref int right)", writableReturn.Signature);

        var members = LoadRaisedMembers(ByRefFixturePath, ByRefFixtureType);
        Assert.Contains("return ref left;", members.Single(m => m.Name == "SelectReadonlyRef").Body);
        Assert.Contains("return ref right;", members.Single(m => m.Name == "SelectReadonlyRef").Body);
        Assert.Contains("return ref left;", members.Single(m => m.Name == "SelectRef").Body);
        Assert.Contains("return ref right;", members.Single(m => m.Name == "SelectRef").Body);
    }

    [Fact]
    public void Rung6FunctionPointerInvocation_RecompilesThroughFidelityHarness()
    {
        AssertExactCompileBack(NewUnsafePath, NewUnsafeType, "InvokeFunctionPointer");
        AssertExactCompileBack(LegacyUnsafePath, LegacyUnsafeType, "InvokeFunctionPointer");
    }

    [Fact]
    public void Rung6PinnedPointerElementAccess_RecompilesThroughFidelityHarness()
    {
        AssertExactCompileBack(NewUnsafePath, NewUnsafeType, "SumPinned");
        AssertExactCompileBack(LegacyUnsafePath, LegacyUnsafeType, "SumPinned");
    }

    [Fact]
    public void Rung6PointerArithmetic_RecompilesThroughFidelityHarness()
    {
        AssertPointerArithmeticRecovery(NewUnsafePath, PointerArithmeticType);
        AssertPointerArithmeticRecovery(LegacyUnsafePath, LegacyPointerArithmeticType);
    }

    static void AssertPointerArithmeticRecovery(string assemblyPath, string typeName)
    {
        var members = LoadRaisedMembers(assemblyPath, typeName);
        var increment = members.Single(m => m.Name == "PointerIncrement").Body;
        Assert.Contains("p++;", increment);
        Assert.Contains("p--;", increment);
        Assert.DoesNotContain("p += 4", increment);
        Assert.DoesNotContain("p -= 4", increment);

        var arithmetic = members.Single(m => m.Name == "PointerArithmeticAndComparison").Body;
        Assert.Contains("next - 1", arithmetic);
        Assert.Contains("q - p", arithmetic);
        Assert.DoesNotContain("unsafe", arithmetic);
        if (assemblyPath == NewUnsafePath)
        {
            AssertNoErrors(
                RecompileNewRules(
                    "static unsafe long M(int* p, int* q)",
                    arithmetic),
                arithmetic);
        }

#if !DEBUG
        AssertExactCompileBack(assemblyPath, typeName, "PointerIncrement");
        AssertExactCompileBack(assemblyPath, typeName, "PointerArithmeticAndComparison");
#endif
    }

    [Fact]
    public void Rung6PointerStore_PreservesOriginalAddressAcrossArgumentStore()
    {
        var output = RaisedCfg(nameof(CfgSampleClass.PointerStoreUsesOriginalAddress));

        Assert.Contains("ptr =", output);
        Assert.Contains("*S_", output);
        Assert.DoesNotContain("*ptr =", output);
    }

    [Fact]
    public void Rung6LoweredPointerStore_PreservesOriginalAddressAcrossArgumentStore()
    {
        var output = LoweredCfg(nameof(CfgSampleClass.PointerStoreUsesOriginalAddress));

        Assert.Contains("ptr =", output);
        Assert.Contains("*S_", output);
        Assert.DoesNotContain("*ptr =", output);
    }

    [Fact]
    public void Rung6UnspellableVolatileAndPinnedShapes_DegradeHonestly()
    {
        var volatileLoad = VolatileIndirectRead(isVolatile: true);
        Assert.Equal(DecompilationFidelity.Partial, volatileLoad.Fidelity);
        Assert.Equal(DiagnosticIds.VolatileIndirectAccess, Assert.Single(FidelityRemarks.Collect(volatileLoad)).Code);

        var plainLoad = VolatileIndirectRead(isVolatile: false);
        Assert.Equal(DecompilationFidelity.Full, plainLoad.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(plainLoad));

        var unraisedPinned = PinnedLocalReferencedWithoutFixed();
        Assert.Equal(DecompilationFidelity.Partial, unraisedPinned.Fidelity);

        var raisedPinned = PinnedLocalOwnedByFixed();
        Assert.Equal(DecompilationFidelity.Full, raisedPinned.Fidelity);
        var raisedPinnedOutput = CSharpPrinter.Print(raisedPinned).Output;
        Assert.Contains("fixed (int* V_0 = ", raisedPinnedOutput);
        Assert.DoesNotContain("pinned", raisedPinnedOutput);
    }

    [Fact]
    public void Rung6FixedBufferAndStringPinningRecover()
    {
        AssertFixedBufferResidual(NewUnsafePath, FixedBufferType);
        AssertFixedBufferResidual(LegacyUnsafePath, LegacyFixedBufferType);

        AssertStringPinningResidual(NewUnsafePath, StringPinningType);
        AssertStringPinningResidual(LegacyUnsafePath, LegacyStringPinningType);
    }

    static void AssertFixedBufferResidual(string assemblyPath, string typeName)
    {
        var member = LoadRaisedMembers(assemblyPath, typeName)
            .Single(m => m.Name == "Sum");
        Assert.Equal(DecompilationFidelity.Full, member.Function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(member.Function));
        Assert.Contains("Data[i]", member.Body);
        Assert.DoesNotContain("FixedElementField", member.Body);
    }

    static void AssertStringPinningResidual(string assemblyPath, string typeName)
    {
        var member = LoadRaisedMembers(assemblyPath, typeName)
            .Single(m => m.Name == "FixedStringFirstChar");
        Assert.Equal(DecompilationFidelity.Full, member.Function.Fidelity);
        Assert.Contains("fixed (char* ", member.Body);
        Assert.Contains(" = value)", member.Body);
        Assert.DoesNotContain("pinned", member.Body);
        string methodHeader = assemblyPath == NewUnsafePath
            ? "static int M(string value)"
            : "static unsafe int M(string value)";
        var diagnostics = assemblyPath == NewUnsafePath
            ? RecompileNewRules(methodHeader, member.Body)
            : RecompileLegacyRules(methodHeader, member.Body);
        AssertNoErrors(diagnostics, member.Body);
    }

    [Fact]
    public void Rung6StringPinningKeepsPointerAliasInsideFixed()
    {
        var body = CSharpPrinter.PrintRaised(SyntheticStringPin(aliasPointerLocal: true, includeUnpin: false)).Output ?? "";

        Assert.Contains("unsafe", body);
        Assert.Contains("fixed (char* V_", body);
        Assert.Contains(" = value)", body);
        Assert.Contains("return *", body);
        Assert.True(
            body.IndexOf("return *", StringComparison.Ordinal) < body.LastIndexOf('}'),
            "pointer dereference must stay inside the fixed region:\n" + body);
        Assert.False(body.StartsWith("char* V_1;", StringComparison.Ordinal), body);
        AssertNoErrors(RecompileNewRules("static int M(string value)", body), body);
    }

    [Fact]
    public void Rung6StringPinningAbsorbsRoslynUnpinStore()
    {
        var body = CSharpPrinter.PrintRaised(SyntheticStringPin(aliasPointerLocal: false, includeUnpin: true)).Output ?? "";

        Assert.Contains("fixed (char* V_", body);
        Assert.Contains(" = value)", body);
        Assert.DoesNotContain("V_0 =", body);
        Assert.DoesNotContain("pinned", body);
        AssertNoErrors(RecompileNewRules("static int M(string value)", body), body);
    }

    [Fact]
    public void Rung6StringPinningKeepsDerivedPointerAliasInsideFixed()
    {
        var body = CSharpPrinter.PrintRaised(SyntheticStringPin(aliasPointerLocal: true, includeUnpin: false, derivedAlias: true)).Output ?? "";

        Assert.Contains("fixed (char* V_", body);
        Assert.Contains(" = value)", body);
        Assert.Contains("return *", body);
        Assert.True(
            body.IndexOf("return *", StringComparison.Ordinal) < body.LastIndexOf('}'),
            "derived pointer dereference must stay inside the fixed region:\n" + body);
        AssertNoErrors(RecompileNewRules("static int M(string value)", body), body);
    }

    [Fact]
    public void Rung6StringPinningUsesResolvedStackSlotNameInFixedHeader()
    {
        var body = CSharpPrinter.PrintRaised(SyntheticStringPin(aliasPointerLocal: false, includeUnpin: false, collideStackSlotName: true)).Output ?? "";

        Assert.Contains("fixed (char* V_", body);
        Assert.Contains(" = value)", body);
        Assert.DoesNotContain("fixed (char* S_0 = value)", body);
        Assert.DoesNotContain("nuint S_0_1;", body);
        AssertNoErrors(RecompileNewRules("static int M(string value)", body), body);
    }

    [Fact]
    public void Rung6StringPinningSupportsLocalSource()
    {
        var body = CSharpPrinter.PrintRaised(SyntheticStringPin(aliasPointerLocal: false, includeUnpin: false, sourceLocal: true)).Output ?? "";

        Assert.Contains("string V_1 = value;", body);
        Assert.Contains("fixed (char* V_", body);
        Assert.Contains(" = V_1)", body);
        Assert.DoesNotContain("pinned", body);
        AssertNoErrors(RecompileNewRules("static int M(string value)", body), body);
    }

    [Fact]
    public void Rung6StringPinningRaisesNestedPinsInnerFirst()
    {
        var body = CSharpPrinter.PrintRaised(SyntheticNestedStringPins()).Output ?? "";

        Assert.Equal(2, body.Split("fixed (char* V_", StringSplitOptions.None).Length - 1);
        Assert.Contains("return", body);
        AssertNoErrors(RecompileNewRules("static int M(string value)", body), body);
    }

    [Fact]
    public void Rung6StringPinningRaisesPinsInsideNestedBlocks()
    {
        var body = CSharpPrinter.PrintRaised(SyntheticNestedBlockStringPin()).Output ?? "";

        Assert.Contains("if (true)", body);
        Assert.Contains("fixed (char* V_", body);
        Assert.DoesNotContain("pinned", body);
        AssertNoErrors(RecompileNewRules("static int M(string value)", body), body);
    }

    [Fact]
    public void Rung6StringPinningAliasOverwriteFailsClosed()
    {
        var body = CSharpPrinter.PrintRaised(SyntheticStringPin(aliasPointerLocal: true, includeUnpin: false, overwriteAlias: true)).Output ?? "";

        Assert.DoesNotContain("fixed (char* ", body);
        Assert.Contains("pinned ref char", body);
    }

    [Fact]
    public void Rung6StringPinningExternallyTargetedBodyLabelStaysLowered()
    {
        var function = SyntheticStringPin(
            aliasPointerLocal: false,
            includeUnpin: false,
            externalBodyLabel: true);

        new FixedStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Fixed>());
        Assert.Single(function.Descendants.OfType<LabelAnchor>());
        function.CheckInvariant();
    }

    [Fact]
    public void Rung6StringPinningTargetedUnpinLabelStaysLowered()
    {
        var function = SyntheticStringPin(
            aliasPointerLocal: false,
            includeUnpin: true,
            targetUnpinLabel: true);

        new FixedStatementPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<Fixed>());
        Assert.Contains(function.Descendants.OfType<Branch>(), branch => branch.TargetOffset == 100);
        Assert.Contains(
            function.Descendants,
            node => node.OwnsSourceLabel && node.SourceOffset == 100);
        function.CheckInvariant();
    }

    [Fact]
    public void Rung6StackallocInitializerResiduals_RecoverFully()
    {
        AssertStackallocInitializerResiduals(NewUnsafePath, StackallocInitializerType);
        AssertStackallocInitializerResiduals(LegacyUnsafePath, LegacyStackallocInitializerType);
    }

    static void AssertStackallocInitializerResiduals(string assemblyPath, string typeName)
    {
        var members = LoadRaisedMembers(assemblyPath, typeName);
        foreach (var name in new[] { "StackallocPointerInitializer", "StackallocSpanInitializer" })
        {
            var member = members.Single(m => m.Name == name);
            Assert.Equal(DecompilationFidelity.Full, member.Function.Fidelity);
            Assert.Contains("stackalloc int[] { 1, 2, 3 }", member.Body);
            Assert.DoesNotContain("CopyBlock", member.Body);
        }
    }

    [Fact]
    public void Rung6StackallocInitializerNegatives_DegradeHonestly()
    {
        AssertStackallocInitializerNegatives(NewUnsafePath, "ILInspector.Decompiler.Fixtures.NewUnsafe.StackallocInitializerNegatives");
        AssertStackallocInitializerNegatives(LegacyUnsafePath, "ILInspector.Decompiler.Fixtures.LegacyUnsafe.StackallocInitializerNegatives");
    }

    static void AssertStackallocInitializerNegatives(string assemblyPath, string typeName)
    {
        var members = LoadRaisedMembers(assemblyPath, typeName);
        foreach (var name in new[] { "CoalescedSpanLocal", "SourceAuthoredCopyBlock" })
        {
            var member = members.Single(m => m.Name == name);
            // They should not be recovered as stackalloc array initializers!
            Assert.DoesNotContain("stackalloc byte[] {", member.Body);
            Assert.DoesNotContain("stackalloc int[] {", member.Body);

            if (name == "CoalescedSpanLocal")
            {
                Assert.Contains("/* unsupported cpblk */", member.Body);
                Assert.Equal(DecompilationFidelity.Partial, member.Function.Fidelity);
            }
            else
            {
                Assert.Contains("Unsafe.CopyBlock", member.Body);
                Assert.Equal(DecompilationFidelity.Full, member.Function.Fidelity);
            }

            string methodHeader = name switch
            {
                "CoalescedSpanLocal" => "static unsafe int CoalescedSpanLocal()",
                "SourceAuthoredCopyBlock" => "static unsafe void SourceAuthoredCopyBlock(byte* dest, byte* src)",
                _ => $"static unsafe void {name}()"
            };
            var diagnostics = assemblyPath == NewUnsafePath
                ? RecompileNewRules(methodHeader, member.Body)
                : RecompileLegacyRules(methodHeader, member.Body);
            AssertNoErrors(diagnostics, member.Body);
        }
    }

    [Fact]
    public void Rung6StackallocInitializerNegatives_SyntheticMismatchedSize_Degrades()
    {
        var function = SyntheticStackallocInitializer(sizeMismatch: true);
        var body = CSharpPrinter.PrintRaised(function).Output ?? "";
        Assert.Contains("/* unsupported cpblk */", body);
        Assert.DoesNotContain("stackalloc int[] {", body);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    static IrFunction SyntheticStackallocInitializer(bool sizeMismatch)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var intPtr = TypeRef.Pointer(intType);

        var stackAlloc = new StackAllocate(new Constant(sizeMismatch ? 16 : 12, intType));
        var storeSlot = new StoreStackSlot(0, stackAlloc);

        var loadDest = new LoadStackSlot(0, intPtr);
        var rvaData = new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0 };
        var loadField = new LoadFieldAddress(new FieldRef(TypeRef.CoreLib("Synthetic", "Blob"), "data", intType), null) { FieldRvaData = rvaData };

        var copyBlock = new CopyBlock(loadDest, loadField, new Constant(12, intType));
        var finalUsage = new StoreLocal(1, intPtr, new LoadStackSlot(0, intPtr));

        var block = new Block(0);
        block.Add(storeSlot);
        block.Add(copyBlock);
        block.Add(finalUsage);

        var container = new BlockContainer();
        container.Add(block);

        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "StackallocInitializer"),
            new MethodSignature(intPtr, [], HasThis: false, GenericParameterCount: 0),
            System.Collections.Immutable.ImmutableArray.Create(intType, intPtr),
            container)
        {
            UsesUpdatedMemorySafetyRules = true
        };
    }

    static IrFunction SyntheticStringPin(
        bool aliasPointerLocal,
        bool includeUnpin,
        bool derivedAlias = false,
        bool collideStackSlotName = false,
        bool sourceLocal = false,
        bool overwriteAlias = false,
        bool externalBodyLabel = false,
        bool targetUnpinLabel = false)
    {
        var charType = TypeRef.CoreLib("System", "Char");
        var intType = TypeRef.CoreLib("System", "Int32");
        var nativeUInt = TypeRef.CoreLib("System", "UIntPtr");
        var stringType = TypeRef.CoreLib("System", "String");
        var charPointer = TypeRef.Pointer(charType);
        var pinnedCharRef = TypeRef.Pinned(TypeRef.ByRef(charType));
        var locals = ImmutableArray.Create(pinnedCharRef);
        int sourceLocalIndex = -1;
        if (aliasPointerLocal)
            locals = locals.Add(charPointer);
        if (sourceLocal)
        {
            sourceLocalIndex = locals.Length;
            locals = locals.Add(stringType);
        }
        var getPinnableReference = new MethodRef(
            stringType,
            "GetPinnableReference",
            TypeRef.ByRef(charType),
            [],
            HasThis: true);

        IrExpression SourceRead()
            => sourceLocal ? new LoadLocal(sourceLocalIndex, stringType) : new LoadArgument(0, "value", stringType);

        var thenArm = new Block(1);
        thenArm.Add(new StoreStackSlot(0, new ILInspector.Decompiler.Pipeline.Convert(nativeUInt, isChecked: false, isUnsigned: false, new Constant(0, intType))));

        var elseArm = new Block(2);
        elseArm.Add(new StoreLocal(0, pinnedCharRef, new Call(getPinnableReference, isVirtual: false, [SourceRead()])));
        elseArm.Add(new StoreStackSlot(0, new ILInspector.Decompiler.Pipeline.Convert(nativeUInt, isChecked: false, isUnsigned: false, new LoadLocal(0, pinnedCharRef))));

        var block = new Block(0);
        if (externalBodyLabel)
            block.Add(new Branch(100));
        if (sourceLocal)
            block.Add(new StoreLocal(sourceLocalIndex, stringType, new LoadArgument(0, "value", stringType)));
        block.Add(new IfStatement(new LogicalNot(SourceRead()), thenArm, elseArm));
        if (externalBodyLabel)
        {
            var anchor = new LabelAnchor();
            anchor.SetSourceOffset(100);
            block.Add(anchor);
        }
        if (targetUnpinLabel)
        {
            var jumpArm = new Block(3);
            jumpArm.Add(new Branch(100));
            block.Add(new IfStatement(new LogicalNot(SourceRead()), jumpArm, null));
        }
        if (aliasPointerLocal)
        {
            var basePointer = new ILInspector.Decompiler.Pipeline.Convert(charPointer, isChecked: false, isUnsigned: false, new LoadStackSlot(0, nativeUInt));
            var aliasValue = derivedAlias
                ? new ILInspector.Decompiler.Pipeline.Convert(
                    charPointer,
                    isChecked: false,
                    isUnsigned: false,
                    new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, basePointer, new Constant(1, intType)))
                : basePointer;
            block.Add(new StoreLocal(1, charPointer, aliasValue));
            if (overwriteAlias)
                block.Add(new StoreLocal(1, charPointer, new ILInspector.Decompiler.Pipeline.Convert(charPointer, isChecked: false, isUnsigned: false, new Constant(0, intType))));
            block.Add(new Return(new LoadIndirect(charType, new LoadLocal(1, charPointer))));
        }
        else
        {
            block.Add(new Return(new LoadIndirect(charType, new LoadStackSlot(0, nativeUInt))));
        }
        if (includeUnpin)
        {
            var unpin = new StoreLocal(
                0,
                pinnedCharRef,
                new ILInspector.Decompiler.Pipeline.Convert(
                    nativeUInt,
                    isChecked: false,
                    isUnsigned: false,
                    new Constant(0, intType)));
            if (targetUnpinLabel)
                unpin.SetSourceOffset(100);
            block.Add(unpin);
        }

        var container = new BlockContainer();
        container.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "StringPin"),
            new MethodSignature(intType, [new Parameter("value", stringType)], HasThis: false, GenericParameterCount: 0),
            locals,
            container)
        {
            UsesUpdatedMemorySafetyRules = true,
        };
        if (collideStackSlotName)
            function.LocalNames = ImmutableArray.Create<string?>("S_0");
        return function;
    }

    static IrFunction SyntheticNestedBlockStringPin()
    {
        var inner = SyntheticStringPin(aliasPointerLocal: false, includeUnpin: false);
        var nestedStatements = inner.Body.Blocks[0].Children.ToList();
        foreach (var child in nestedStatements)
            child.Detach();
        var outerThen = new Block(1);
        foreach (var child in nestedStatements)
            outerThen.Add(child);

        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var stringType = TypeRef.CoreLib("System", "String");
        var block = new Block(0);
        block.Add(new IfStatement(new Constant(true, boolType), outerThen, null));
        block.Add(new Return(new Constant(0, intType)));
        var container = new BlockContainer();
        container.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "StringPin"),
            new MethodSignature(intType, [new Parameter("value", stringType)], HasThis: false, GenericParameterCount: 0),
            inner.Locals,
            container)
        {
            UsesUpdatedMemorySafetyRules = true,
        };
    }

    static IrFunction SyntheticNestedStringPins()
    {
        var charType = TypeRef.CoreLib("System", "Char");
        var intType = TypeRef.CoreLib("System", "Int32");
        var nativeUInt = TypeRef.CoreLib("System", "UIntPtr");
        var stringType = TypeRef.CoreLib("System", "String");
        var pinnedCharRef = TypeRef.Pinned(TypeRef.ByRef(charType));
        var getPinnableReference = new MethodRef(
            stringType,
            "GetPinnableReference",
            TypeRef.ByRef(charType),
            [],
            HasThis: true);

        static IfStatement Guard(
            int pinnedLocal,
            int pointerSlot,
            TypeRef intType,
            TypeRef nativeUInt,
            TypeRef stringType,
            TypeRef pinnedCharRef,
            MethodRef getPinnableReference)
        {
            var thenArm = new Block(1);
            thenArm.Add(new StoreStackSlot(pointerSlot, new ILInspector.Decompiler.Pipeline.Convert(nativeUInt, isChecked: false, isUnsigned: false, new Constant(0, intType))));
            var elseArm = new Block(2);
            elseArm.Add(new StoreLocal(pinnedLocal, pinnedCharRef, new Call(getPinnableReference, isVirtual: false, [new LoadArgument(0, "value", stringType)])));
            elseArm.Add(new StoreStackSlot(pointerSlot, new ILInspector.Decompiler.Pipeline.Convert(nativeUInt, isChecked: false, isUnsigned: false, new LoadLocal(pinnedLocal, pinnedCharRef))));
            return new IfStatement(new LogicalNot(new LoadArgument(0, "value", stringType)), thenArm, elseArm);
        }

        var block = new Block(0);
        block.Add(Guard(0, 0, intType, nativeUInt, stringType, pinnedCharRef, getPinnableReference));
        block.Add(Guard(1, 1, intType, nativeUInt, stringType, pinnedCharRef, getPinnableReference));
        block.Add(new Return(new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadIndirect(charType, new LoadStackSlot(0, nativeUInt)),
            new LoadIndirect(charType, new LoadStackSlot(1, nativeUInt)))));

        var container = new BlockContainer();
        container.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "StringPin"),
            new MethodSignature(intType, [new Parameter("value", stringType)], HasThis: false, GenericParameterCount: 0),
            ImmutableArray.Create(pinnedCharRef, pinnedCharRef),
            container)
        {
            UsesUpdatedMemorySafetyRules = true,
        };
    }

    static void AssertExactCompileBack(string assemblyPath, string typeName, string methodName)
    {
        var result = Assert.Single(
            FidelityCheck.Evaluate(assemblyPath),
            r => r.Type == typeName && r.Method == methodName);

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

    static string RaisedCfg(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method)).Output ?? "";
    }

    static string LoweredCfg(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return CSharpPrinter.PrintLowered(function!).Output ?? "";
    }

    static List<(string Name, IrFunction Function, DecompilerResult Result, string Body)> LoadRaisedMembers(
        string assemblyPath,
        string fixtureType)
    {
        var members = new List<(string Name, IrFunction Function, DecompilerResult Result, string Body)>();
        using var source = MetadataSource.Open(assemblyPath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (typeName != fixtureType)
                continue;

            var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
            members.Add((methodName, function, result, result.Output ?? ""));
        }
        return members;
    }

    static string FirstUnsafeBlockBody(string output)
    {
        int keyword = output.IndexOf("unsafe", StringComparison.Ordinal);
        Assert.True(keyword >= 0, "no unsafe block in output:\n" + output);
        int open = output.IndexOf('{', keyword);
        Assert.True(open >= 0);
        int depth = 0;
        for (int i = open; i < output.Length; i++)
        {
            if (output[i] == '{') depth++;
            else if (output[i] == '}' && --depth == 0)
                return output[(open + 1)..i];
        }
        throw new Xunit.Sdk.XunitException("unbalanced unsafe block:\n" + output);
    }

    static ImmutableArray<Diagnostic> RecompileNewRules(
        string methodHeader,
        string body,
        params MetadataReference[] extraReferences)
        => Recompile(methodHeader, body, "", useUpdatedMemorySafetyRules: true, extraReferences);

    static ImmutableArray<Diagnostic> RecompileNewRules(
        string methodHeader,
        string body,
        string extraDeclarations,
        params MetadataReference[] extraReferences)
        => Recompile(methodHeader, body, extraDeclarations, useUpdatedMemorySafetyRules: true, extraReferences);

    static ImmutableArray<Diagnostic> RecompileLegacyRules(
        string methodHeader,
        string body,
        params MetadataReference[] extraReferences)
        => Recompile(methodHeader, body, "", useUpdatedMemorySafetyRules: false, extraReferences);

    static ImmutableArray<Diagnostic> RecompileLegacyRules(
        string methodHeader,
        string body,
        string extraDeclarations,
        params MetadataReference[] extraReferences)
        => Recompile(methodHeader, body, extraDeclarations, useUpdatedMemorySafetyRules: false, extraReferences);

    static ImmutableArray<Diagnostic> Recompile(
        string methodHeader,
        string body,
        string extraDeclarations,
        bool useUpdatedMemorySafetyRules,
        params MetadataReference[] extraReferences)
    {
        string source = $$"""
            using System;
            using ILInspector.Decompiler.Fixtures.NewUnsafe;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            {{extraDeclarations}}
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        if (useUpdatedMemorySafetyRules)
        {
            parseOptions = parseOptions.WithFeatures(
                [new KeyValuePair<string, string>("updated-memory-safety-rules", "true")]);
        }
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        return CSharpCompilation.Create(
                "__gate",
                [tree],
                [.. RuntimeReferences(), .. extraReferences],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .GetDiagnostics();
    }

    static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics, string body)
    {
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToArray();
        Assert.True(
            errors.Length == 0,
            "decompiled row 6 output must recompile cleanly, got:\n  "
                + string.Join("\n  ", errors) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => RoslynTestReferences.TrustedPlatform;

    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef IntPointer = TypeRef.Pointer(Int32);
    static readonly TypeRef PinnedRefInt = TypeRef.Pinned(TypeRef.ByRef(Int32));

    static (string Updated, string Legacy) PrintRulePair(Func<IrFunction> createFunction)
    {
        var updatedFunction = createFunction();
        updatedFunction.UsesUpdatedMemorySafetyRules = true;
        var legacyFunction = createFunction();
        legacyFunction.UsesUpdatedMemorySafetyRules = false;
        return (
            CSharpPrinter.Print(updatedFunction).Output!,
            CSharpPrinter.Print(legacyFunction).Output!);
    }

    static IrFunction VolatileIndirectRead(bool isVolatile)
    {
        var load = new LoadIndirect(Int32, new LoadArgument(0, "p", IntPointer)) { IsVolatile = isVolatile };
        return Function(
            "ReadVolatile",
            Int32,
            [new Parameter("p", IntPointer)],
            [],
            new Return(load));
    }

    static IrFunction PinnedLocalReferencedWithoutFixed() =>
        Function(
            "PinnedResidue",
            Void,
            [],
            [PinnedRefInt],
            new StoreLocal(0, Int32, new Constant(0, Int32)),
            new ExpressionStatement(new LoadLocal(0, Int32)),
            new Return(null));

    static IrFunction PinnedLocalOwnedByFixed()
    {
        var fixedBody = BlockContainer(new ExpressionStatement(new LoadLocal(0, Int32)));
        return Function(
            "PinnedFixed",
            Void,
            [],
            [PinnedRefInt],
            new Fixed(Int32, localIndex: 0, new Constant(0, Int32), fixedBody),
            new Return(null));
    }

    static IrFunction Function(
        string name,
        TypeRef returnType,
        ImmutableArray<Parameter> parameters,
        ImmutableArray<TypeRef> locals,
        params IrNode[] statements) =>
        new(name,
            TypeRef.Definition("Synthetic", "LadderRung6", "NativeCanaries"),
            new MethodSignature(returnType, parameters, HasThis: false, GenericParameterCount: 0),
            locals,
            BlockContainer(statements))
        {
            UsesUpdatedMemorySafetyRules = true
        };

    static BlockContainer BlockContainer(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        foreach (var statement in statements)
            block.Add(statement);
        return container;
    }
}
