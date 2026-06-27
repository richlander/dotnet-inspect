using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using LegacyUnsafe = ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;
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
public class LadderRung6GateTests
{
    static readonly string NewUnsafePath = typeof(NewUnsafe).Assembly.Location;
    static readonly string LegacyUnsafePath = typeof(LegacyUnsafe).Assembly.Location;
    static readonly string NewUnsafeType = typeof(NewUnsafe).FullName!;
    static readonly string LegacyUnsafeType = typeof(LegacyUnsafe).FullName!;
    static readonly string ByRefFixturePath = typeof(LadderRung4.CSharp7LocalSyntax).Assembly.Location;
    static readonly string ByRefFixtureType = typeof(LadderRung4.CSharp7LocalSyntax).FullName!;

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
        Assert.Contains("UnsafeFixtures.Risky()", FirstUnsafeBlockBody(NewBody("CallRisky")));
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
        Assert.Contains("*((int*)((byte*)values + 4))", eventData);
        Assert.DoesNotContain("*(values + 4)", eventData);

        var pinned = NewBody("SumPinned");
        Assert.Contains("fixed (int* p = ", pinned);
        Assert.Contains("sum +=", FirstUnsafeBlockBody(pinned));
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

        var derefDiagnostics = RecompileNewRules("static int M(int value)", derefBody);
        var passAddressDiagnostics = RecompileNewRules(
            "static int M(int value)",
            passAddressBody,
            MetadataReference.CreateFromFile(NewUnsafePath));

        AssertNoErrors(derefDiagnostics, derefBody);
        AssertNoErrors(passAddressDiagnostics, passAddressBody);
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

    static void AssertExactCompileBack(string assemblyPath, string typeName, string methodName)
    {
        var result = Assert.Single(
            FidelityCheck.Evaluate(assemblyPath),
            r => r.Type == typeName && r.Method == methodName);

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
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
    {
        string source = $$"""
            using System;
            using ILInspector.Decompiler.Fixtures.NewUnsafe;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("updated-memory-safety-rules", "true")]);
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
    {
        var trustedPlatformAssemblies =
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var path in trustedPlatformAssemblies)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return references.ToImmutable();
    }

    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef IntPointer = TypeRef.Pointer(Int32);
    static readonly TypeRef PinnedRefInt = TypeRef.Pinned(TypeRef.ByRef(Int32));

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
            BlockContainer(statements));

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
