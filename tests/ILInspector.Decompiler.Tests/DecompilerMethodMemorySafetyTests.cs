using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using ChainB = ILInspector.Decompiler.Fixtures.UnsafeChainB.LibraryB;
using ChainDerived =
    ILInspector.Decompiler.Fixtures.UnsafeChainB.ContractDerived;
using ChainImplicitDerived =
    ILInspector.Decompiler.Fixtures.UnsafeChainB.ImplicitContractDerived;
using ChainArgumentDerived =
    ILInspector.Decompiler.Fixtures.UnsafeChainB.ContractArgumentDerived;
using ChainPropertyArgumentDerived =
    ILInspector.Decompiler.Fixtures.UnsafeChainB.ContractPropertyArgumentDerived;
using ChainRefArgumentDerived =
    ILInspector.Decompiler.Fixtures.UnsafeChainB.ContractRefArgumentDerived;
using ChainThis =
    ILInspector.Decompiler.Fixtures.UnsafeChainB.ThisContract;
using ChainThisArgument =
    ILInspector.Decompiler.Fixtures.UnsafeChainB.ThisArgumentContract;
using NewFixtures =
    ILInspector.Decompiler.Fixtures.NewUnsafe.AccessorContractFixtures;
using NewMethods =
    ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures;

namespace ILInspector.Decompiler.Tests;

public class DecompilerMethodMemorySafetyTests
{
    [Fact]
    public void SameAssemblyOrdinaryMethodContract_UsesSharedDecision()
    {
        DecompilerResult result = Decompile(
            typeof(NewMethods).Assembly.Location,
            typeof(NewMethods).FullName!,
            nameof(NewMethods.CallRisky));

        Assert.Contains("return unsafe(Risky());", result.Output);
        Assert.DoesNotContain("unsafe\n{", result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData(
        nameof(NewFixtures.ReadProperty),
        "return fixture.Property;",
        true)]
    [InlineData(
        nameof(NewFixtures.SubscribeEvent),
        "fixture.Changed += handler;",
        true)]
    [InlineData(
        nameof(NewFixtures.Create),
        "return unsafe(new AccessorContractFixtures());",
        false)]
    public void SameAssemblyCallableContract_UsesExactMethodDef(
        string method,
        string expected,
        bool expectsBlock)
    {
        DecompilerResult result = Decompile(
            typeof(NewFixtures).Assembly.Location,
            typeof(NewFixtures).FullName!,
            method);

        Assert.Contains(expected, result.Output);
        Assert.Equal(
            expectsBlock,
            result.Output!.Contains("unsafe\n{", StringComparison.Ordinal));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void AccessorSpecificNoContract_DoesNotInheritSiblingContract()
    {
        DecompilerResult result = Decompile(
            typeof(NewFixtures).Assembly.Location,
            typeof(NewFixtures).FullName!,
            nameof(NewFixtures.WriteProperty));

        Assert.Contains("fixture.Property = value;", result.Output);
        Assert.DoesNotContain("unsafe", result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void CrossAssemblyOrdinaryMethodContract_UsesSharedDecision()
    {
        DecompilerResult result = Decompile(
            typeof(ChainB).Assembly.Location,
            typeof(ChainB).FullName!,
            nameof(ChainB.M2));

        Assert.Contains("return unsafe(LibraryA.M1());", result.Output);
        Assert.DoesNotContain("unsafe\n{", result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData(
        nameof(ChainB.ReadContractProperty),
        "return LibraryA.ContractProperty;",
        true)]
    [InlineData(
        nameof(ChainB.SubscribeContractEvent),
        "LibraryA.ContractEvent += handler;",
        true)]
    [InlineData(
        nameof(ChainB.CreateContractObject),
        "return unsafe(new ContractObject());",
        false)]
    public void CrossAssemblyCallableContract_UsesExactResolvedMethodDef(
        string method,
        string expected,
        bool expectsBlock)
    {
        DecompilerResult result = Decompile(
            typeof(ChainB).Assembly.Location,
            typeof(ChainB).FullName!,
            method);

        Assert.Contains(expected, result.Output);
        Assert.Equal(
            expectsBlock,
            result.Output!.Contains("unsafe\n{", StringComparison.Ordinal));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData(nameof(ChainB.ReadContractProperty), "static int M()", """
        public static class LibraryA
        {
            public static unsafe int ContractProperty { get => 42; }
        }
        """)]
    [InlineData(nameof(ChainB.SubscribeContractEvent), "static void M(Action handler)", """
        public static class LibraryA
        {
            public static unsafe event Action ContractEvent
            {
                add { }
                remove { }
            }
        }
        """)]
    [InlineData(nameof(ChainB.CreateContractObject), "static ContractObject M()", """
        public sealed class ContractObject
        {
            public unsafe ContractObject() { }
        }
        """)]
    public void CrossAssemblyCallableContract_ProductBodyCompiles(
        string method,
        string methodHeader,
        string declarations)
    {
        DecompilerResult result = Decompile(
            typeof(ChainB).Assembly.Location,
            typeof(ChainB).FullName!,
            method);

        AssertCompiles(methodHeader, result.Output!, declarations);
    }

    [Fact]
    public void CrossAssemblyConstructorInitializer_UsesDeclarationContext()
    {
        string path = typeof(ChainDerived).Assembly.Location;
        ApiType type;
        using (var pe = new PEReader(File.OpenRead(path)))
        {
            type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate => candidate.FullName == typeof(ChainDerived).FullName);
        }

        DecompilerResult result =
            MemberBodyProducer.Project(type, path, pdbPath: null);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("public unsafe ContractDerived()", result.Output);
        Assert.Contains(": base(42)", result.Output);
        Assert.Contains("Value = 42;", result.Output);
        Assert.DoesNotContain("\n    unsafe\n", result.Output);
        AssertCompilesType(
            result.Output!,
            """
            public class ContractBase
            {
                public unsafe ContractBase(int value) => _ = value;
            }
            """);
    }

    [Fact]
    public void SameAssemblyThisConstructorInitializer_UsesDeclarationContext()
    {
        string path = typeof(ChainThis).Assembly.Location;
        ApiType type;
        using (var pe = new PEReader(File.OpenRead(path)))
        {
            type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate => candidate.FullName == typeof(ChainThis).FullName);
        }

        DecompilerResult result =
            MemberBodyProducer.Project(type, path, pdbPath: null);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("public unsafe ThisContract() : this(42)", result.Output);
        Assert.Contains("Value++;", result.Output);
        Assert.DoesNotContain("\n    unsafe\n", result.Output);
        AssertCompilesType(result.Output!, declarations: "");
    }

    [Fact]
    public void CrossAssemblyImplicitConstructorInitializer_KeepsPreambleMapping()
    {
        string path = typeof(ChainImplicitDerived).Assembly.Location;
        ApiType type;
        using (var pe = new PEReader(File.OpenRead(path)))
        {
            type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate =>
                    candidate.FullName == typeof(ChainImplicitDerived).FullName);
        }

        DecompilerResult result =
            MemberBodyProducer.Project(type, path, pdbPath: null);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "public unsafe ImplicitContractDerived()",
            result.Output);
        Assert.DoesNotContain(": base()", result.Output);
        Assert.Contains("Value = 42;", result.Output);
        Assert.DoesNotContain("\n    unsafe\n", result.Output);
        AssertCompilesType(
            result.Output!,
            """
            public class ImplicitContractBase
            {
                public unsafe ImplicitContractBase() { }
            }
            """);
    }

    [Fact]
    public void SafeBaseConstructorInitializer_WrapsUnsafeMethodArgument()
    {
        DecompilerResult result = DecompileType(typeof(ChainArgumentDerived));

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "public ContractArgumentDerived() : base(unsafe(LibraryA.M1()))",
            result.Output);
        Assert.DoesNotContain(
            "public unsafe ContractArgumentDerived()",
            result.Output);
        AssertCompilesType(
            result.Output!,
            SafeArgumentDeclarations);
    }

    [Fact]
    public void SafeThisConstructorInitializer_WrapsUnsafeMethodArgument()
    {
        DecompilerResult result = DecompileType(typeof(ChainThisArgument));

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "public ThisArgumentContract() : this(unsafe(LibraryA.M1()))",
            result.Output);
        Assert.DoesNotContain(
            "public unsafe ThisArgumentContract()",
            result.Output);
        AssertCompilesType(
            result.Output!,
            """
            public static class LibraryA
            {
                public static unsafe int M1() => 42;
            }
            """);
    }

    [Fact]
    public void SafeBaseConstructorInitializer_CastsDirectUnsafePropertyArgument()
    {
        DecompilerResult result =
            DecompileType(typeof(ChainPropertyArgumentDerived));

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "base(unsafe((int)LibraryA.ContractProperty))",
            result.Output);
        AssertCompilesType(
            result.Output!,
            SafeArgumentDeclarations);
    }

    [Fact]
    public void SafeBaseConstructorInitializer_KeepsRefOutsideUnsafeArgument()
    {
        DecompilerResult result =
            DecompileType(typeof(ChainRefArgumentDerived));

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains(
            "base(ref unsafe(LibraryA.ContractRef()))",
            result.Output);
        AssertCompilesType(
            result.Output!,
            """
            public class SafeRefArgumentBase
            {
                public SafeRefArgumentBase(ref int value) { }
            }
            public static class LibraryA
            {
                static int s_value;
                public static unsafe ref int ContractRef() => ref s_value;
            }
            """);
    }

    [Theory]
    [InlineData(MemorySafetyRulesState.Unsupported, false, false)]
    [InlineData(MemorySafetyRulesState.Malformed, false, false)]
    [InlineData(MemorySafetyRulesState.Conflicting, false, false)]
    [InlineData(null, true, false)]
    [InlineData(MemorySafetyRulesState.Updated, false, true)]
    public void InvalidCallableContract_DoesNotInventUnsafeContext(
        MemorySafetyRulesState? rulesState,
        bool rulesUnavailable,
        bool contractUnavailable)
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var pointer = TypeRef.Pointer(int32);
        var owner = TypeRef.Definition(
            "Dependency",
            "Fixtures",
            "Library");
        var method = new MethodRef(
            owner,
            "Read",
            int32,
            [pointer],
            HasThis: false)
        {
            RequiresUnsafeFact = MetadataFactState.Yes,
            MemorySafetyRulesState = rulesState,
            MemorySafetyRulesUnavailable = rulesUnavailable,
            MemorySafetyContractUnavailable = contractUnavailable,
        };
        var block = new Block();
        block.Add(new Return(new Call(
            method,
            isVirtual: false,
            [new Constant(null, pointer)])));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Read",
            owner,
            new MethodSignature(
                int32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.DoesNotContain("unsafe", result.Output);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.False(UnsafeAwaitOperand.MethodRequiresUnsafe(
            method,
            usesUpdatedMemorySafetyRules: true));
    }

    [Fact]
    public void RaisedCollectionCarrier_UsesRetainedCallableContract()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var int32Array = TypeRef.SzArray(int32);
        var builder = ExplicitUpdatedContract(
            TypeRef.Definition(
                "Dependency",
                "Fixtures",
                "CollectionBuilder"),
            "Create",
            int32Array);
        var collection = new CollectionExpression(
            int32,
            int32Array,
            [new Constant(1, int32)],
            [builder]);
        var block = new Block();
        block.Add(new Return(collection));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Create",
            builder.DeclaringType,
            new MethodSignature(
                int32Array,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal("return unsafe([1]);\n", result.Output);
    }

    [Fact]
    public void AwaitBoundary_UsesRetainedCallableContract()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var task = TypeRef.GenericInstance(
            TypeRef.CoreLib("System.Threading.Tasks", "Task`1"),
            [int32]);
        var awaiter = ExplicitUpdatedContract(
            TypeRef.Definition(
                "Dependency",
                "Fixtures",
                "UnsafeAwaiter"),
            "GetResult",
            int32);
        var block = new Block();
        block.Add(new Return(new AwaitExpression(
            new LoadArgument(0, "task", task),
            int32,
            consumedMemberRefs: [awaiter])));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "ReadAsync",
            awaiter.DeclaringType,
            new MethodSignature(
                task,
                [new Parameter("task", task)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        new UnsafeAwaitBoundaryPass().Run(function, PassContext.None);
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "unsafe await boundary");
        Assert.DoesNotContain("unsafe\n{\n    return await", result.Output);
    }

    static MethodRef ExplicitUpdatedContract(
        TypeRef owner,
        string name,
        TypeRef returnType)
        => new(
            owner,
            name,
            returnType,
            [],
            HasThis: false)
        {
            RequiresUnsafe = true,
            RequiresUnsafeFact = MetadataFactState.Yes,
            MemorySafetyRulesState = MemorySafetyRulesState.Updated,
        };

    static DecompilerResult Decompile(
        string assemblyPath,
        string typeFullName,
        string method)
    {
        using var source = MetadataSource.Open(assemblyPath);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, typeFullName, method));
        DecompilerResult result = CSharpPrinter.PrintRaised(function);
        Assert.NotNull(result.Output);
        return result;
    }

    static DecompilerResult DecompileType(Type reflectedType)
    {
        string path = reflectedType.Assembly.Location;
        ApiType type;
        using (var pe = new PEReader(File.OpenRead(path)))
        {
            type = Assert.Single(
                ApiSurfaceExtractor.Extract(pe).Types,
                candidate => candidate.FullName == reflectedType.FullName);
        }
        return MemberBodyProducer.Project(type, path, pdbPath: null);
    }

    const string SafeArgumentDeclarations = """
        public class SafeArgumentBase
        {
            public SafeArgumentBase(int value) { }
        }
        public static class LibraryA
        {
            public static unsafe int M1() => 42;
            public static unsafe int ContractProperty { get => 42; }
        }
        """;

    static void AssertCompiles(
        string methodHeader,
        string body,
        string declarations)
        => AssertCompilesType(
            $$"""
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """,
            declarations);

    static void AssertCompilesType(string typeSource, string declarations)
    {
        string source = $$"""
            using System;
            {{typeSource}}
            {{declarations}}
            """;
        UnsafeEmitterTests.AssertNoWarningsOrErrors(
            UnsafeEmitterTests.CreateUpdatedRulesCompilation(
                "__callable_contract_gate",
                source)
                .GetDiagnostics(),
            source);
    }
}
