using System.Collections.Immutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class CrossAssemblyMethodFactsTests
{
    [Fact]
    public void CrossAssemblyByRefMemberRef_RecoversParameterRefKinds()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseOut), "WriteOut", ArgumentRefKind.Out);
        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseRef), "Mutate", ArgumentRefKind.Ref);
        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseIn), "Read", ArgumentRefKind.In);
    }

    [Fact]
    public void VersionDriftedSiblingAssembly_RecoversParameterRefKinds()
    {
        using var fixture = CrossAssemblyFixture.Create(versionDrift: true);
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseExternalOut), "WriteExternalOut", ArgumentRefKind.Out);
        AssertCallRefKind(source, nameof(CrossAssemblyFixtureMethods.UseExternalRef), "MutateExternal", ArgumentRefKind.Ref);
        Assert.Contains(
            "ByRefLibrary.WriteExternalOut(out V_0);",
            Print(source, CrossAssemblyFixtureMethods.UseExternalOut));
    }

    [Fact]
    public void PlatformForwardedByRefMemberRef_RecoversParameterRefKinds()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, null, TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseUri), "TryCreate");
        Assert.Equal(ParameterRefKindFacts.Known, call.Callee.ParameterRefKindsFacts);
        Assert.Collection(
            call.Callee.ParameterRefKinds,
            kind => Assert.Equal(ArgumentRefKind.Value, kind),
            kind => Assert.Equal(ArgumentRefKind.Value, kind),
            kind => Assert.Equal(ArgumentRefKind.Out, kind));
    }

    [Fact]
    public void CrossAssemblyGeneratedMemberRef_RecoversCompilerGeneratedFacts()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseGenerated), "Run");

        Assert.Equal(MetadataFactState.Yes, call.Callee.DeclaringTypeCompilerGenerated);
        Assert.Equal(MetadataFactState.Yes, call.Callee.CompilerGenerated);
    }

    [Fact]
    public void CrossAssemblyDelegateConstructor_RecoversDelegateTypeFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        var newObject = SingleNewObject(source, nameof(CrossAssemblyFixtureMethods.UseExternalDelegate));

        Assert.Equal(MetadataFactState.Yes, newObject.Constructor.DeclaringTypeIsDelegate);
    }

    [Fact]
    public void CrossAssemblyOperatorMemberRef_RecoversOperatorFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseRealOperator), "op_Addition");

        Assert.Equal(MetadataFactState.Yes, call.Callee.IsOperator);
    }

    [Fact]
    public void CrossAssemblyOperatorNameLookalike_RecoversNotOperatorFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var addition = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition), "op_Addition");
        var conversion = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeImplicit), "op_Implicit");

        Assert.Equal(MetadataFactState.No, addition.Callee.IsOperator);
        Assert.Equal(MetadataFactState.No, conversion.Callee.IsOperator);
    }

    [Fact]
    public void CrossAssemblyOperatorNameLookalike_RendersMethodCall()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        string addition = Print(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition));
        string conversion = Print(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeImplicit));

        Assert.Contains(".op_Addition(left, right)", addition);
        Assert.DoesNotContain("left + right", addition);
        Assert.Contains(".op_Implicit(value)", conversion);
        Assert.DoesNotContain("return (int)value;", conversion);
    }

    [Fact]
    public void CrossAssemblyOperatorMemberRef_RendersOperator()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        string output = Print(source, nameof(CrossAssemblyFixtureMethods.UseRealOperator));

        Assert.Contains("left + right", output);
        Assert.DoesNotContain("op_Addition", output);
    }

    [Fact]
    public void CrossAssemblyPropertyAccessorMemberRef_RecoversAccessorKind()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseProperty), "get_Count");

        Assert.Equal(AccessorKind.PropertyGet, call.Callee.AccessorKind);
    }

    [Fact]
    public void CrossAssemblyPropertyAccessorMemberRef_RendersPropertyAccess()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var function = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseProperty));
        var result = CSharpPrinter.PrintRaised(function, out _);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("library.Count", result.Output);
        Assert.DoesNotContain("get_Count", result.Output);
    }

    [Fact]
    public void CrossAssemblyDynamicReturns_PreserveReferenceIdentity()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var property = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseDynamicProperty), "get_DynamicValue");
        var method = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseDynamicMethod), "GetDynamicValue");
        var byRefMethod = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseByRefDynamicMethod), "GetDynamicReference");
        var byRefObjectMethod = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseByRefObjectMethod), "GetObjectReference");
        Assert.Equal(MetadataFactState.Yes, property.Callee.ReturnIsDynamic);
        Assert.Equal(MetadataFactState.Yes, method.Callee.ReturnIsDynamic);
        Assert.Equal(MetadataFactState.Yes, byRefMethod.Callee.ReturnIsDynamic);
        Assert.Equal(MetadataFactState.No, byRefObjectMethod.Callee.ReturnIsDynamic);

        Assert.Contains("(object)library.DynamicValue == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicProperty));
        Assert.Contains("(object)library.GetDynamicValue() == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicMethod));
        Assert.Contains("(object)(library.GetDynamicReference()) == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefDynamicMethod));
        Assert.Contains("(object)(library.DynamicReference) == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefDynamicProperty));
        Assert.Contains("(object)(library.Reference) == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseGenericByRefDynamicProperty));
        Assert.DoesNotContain("(object)", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefObjectMethod));
    }

    [Fact]
    public void CrossAssemblyDynamicFields_PreserveReferenceIdentity()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        var direct = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseDynamicField), "DynamicField");
        var generic = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseGenericDynamicField), "Value");
        var plainObject = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseObjectField), "ObjectField");
        var byRefDynamic = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseByRefDynamicField), "DynamicField");
        var byRefObject = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseByRefObjectField), "ObjectField");

        Assert.Equal(MetadataFactState.Yes, direct.Field.DynamicFact);
        Assert.Equal(MetadataFactState.Unknown, generic.Field.DynamicFact);
        Assert.Equal(MetadataFactState.No, plainObject.Field.DynamicFact);
        Assert.Equal(MetadataFactState.Yes, byRefDynamic.Field.DynamicFact);
        Assert.Equal(MetadataFactState.No, byRefObject.Field.DynamicFact);
        Assert.Contains("(object)library.DynamicField == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicField));
        Assert.Contains("(object)library.Value == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseGenericDynamicField));
        Assert.Contains("return library.ObjectField == right;", PrintRaised(source, CrossAssemblyFixtureMethods.UseObjectField));
        Assert.Contains("(object)(library.DynamicField) == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefDynamicField));
        Assert.DoesNotContain("(object)", PrintRaised(source, CrossAssemblyFixtureMethods.UseByRefObjectField));
    }

    [Fact]
    public void MissingCrossAssemblyDynamicFacts_DeclineConservatively()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(
            fixture.ConsumerPath,
            null,
            TestAssemblyReferenceResolvers.None);

        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseDynamicMethod), "GetDynamicValue");
        Assert.Equal(MetadataFactState.Unknown, call.Callee.ReturnIsDynamic);
        Assert.Contains("(object)library.GetDynamicValue() == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicMethod));
    }

    [Fact]
    public void MissingCrossAssemblyFieldFacts_DeclineConservatively()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(
            fixture.ConsumerPath,
            null,
            TestAssemblyReferenceResolvers.None);

        var field = SingleField(source, nameof(CrossAssemblyFixtureMethods.UseDynamicField), "DynamicField");
        Assert.Equal(MetadataFactState.Unknown, field.Field.DynamicFact);
        Assert.Contains("(object)library.DynamicField == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseDynamicField));
        Assert.Contains("(object)new ExternalReference() == (object)right", PrintRaised(source, CrossAssemblyFixtureMethods.UseExternalNewObject));
    }

    [Fact]
    public void CrossAssemblyInlineArrayHelper_RecoversInlineArrayTypeArgumentFact()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);
        var call = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseExternalInlineArray), "InlineArrayAsSpan");

        Assert.Equal(MetadataFactState.Yes, call.Callee.DeclaringTypeCompilerGenerated);
        Assert.Equal(MetadataFactState.Yes, call.Callee.TypeArguments[0].DeclaredInlineArray);
        Assert.True(MemberIdentity.IsInlineArraySpanConversionHelper(call, out var arrayType));
        Assert.Equal(MetadataFactState.Yes, arrayType.DeclaredInlineArray);
    }

    [Fact]
    public void MissingCrossAssemblyMetadata_KeepsFactsUnknown()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, null, TestAssemblyReferenceResolvers.None);

        var byRef = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOut), "WriteOut");
        Assert.Equal(ParameterRefKindFacts.Unknown, byRef.Callee.ParameterRefKindsFacts);
        Assert.Empty(byRef.Callee.ParameterRefKinds);

        var generated = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseGenerated), "Run");
        Assert.Equal(MetadataFactState.Unknown, generated.Callee.DeclaringTypeCompilerGenerated);
        Assert.Equal(MetadataFactState.Unknown, generated.Callee.CompilerGenerated);

        var externalDelegate = SingleNewObject(source, nameof(CrossAssemblyFixtureMethods.UseExternalDelegate));
        Assert.Equal(MetadataFactState.Unknown, externalDelegate.Constructor.DeclaringTypeIsDelegate);

        var operatorLike = SingleCall(source, nameof(CrossAssemblyFixtureMethods.UseOperatorLikeAddition), "op_Addition");
        Assert.Equal(MetadataFactState.Unknown, operatorLike.Callee.IsOperator);
    }

    [Fact]
    public void MissingCrossAssemblyAccessorMetadata_KeepsAccessorFactUnknown()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, null, TestAssemblyReferenceResolvers.None);

        var function = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseProperty));
        var call = Assert.Single(function.Descendants.OfType<Call>(), c => c.Callee.Name == "get_Count");

        Assert.Equal(AccessorKind.Unknown, call.Callee.AccessorKind);
        Assert.True(call.Callee.IsSpecialNameInferred);
    }

    [Fact]
    public void CrossAssemblyRefStruct_RecoveredIntoByRefLikeTypes()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath);

        // GPT review round 5 (#3124): a `ref struct` defined in a REFERENCED
        // assembly resolves to a ValueType shape but carries no same-assembly
        // by-ref-like fact, so the value-type-arm gate would raise `T t` over it —
        // invalid C# (CS8121). The [IsByRefLike] fact is now resolved through the
        // cross-assembly resolver and recovered into ByRefLikeTypes; the
        // same-assembly value struct (ExternalNumber) stays out.
        var refStructUser = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseExternalRefStruct));
        Assert.Contains(refStructUser.ByRefLikeTypes, t => t.Name == "ExternalRefStruct");

        var structUser = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseExternalStruct));
        Assert.DoesNotContain(structUser.ByRefLikeTypes, t => t.Name == "ExternalNumber");
    }

    [Fact]
    public void MissingCrossAssemblyRefStructMetadata_KeepsByRefLikeUnknown()
    {
        using var fixture = CrossAssemblyFixture.Create();
        using var source = MetadataSource.Open(fixture.ConsumerPath, null, TestAssemblyReferenceResolvers.None);

        // With the defining assembly outside the reference closure the fact cannot
        // be resolved, so the referenced ref struct is absent from ByRefLikeTypes —
        // fail visible, not a wrong-positive.
        var refStructUser = ImportFunction(source, nameof(CrossAssemblyFixtureMethods.UseExternalRefStruct));
        Assert.DoesNotContain(refStructUser.ByRefLikeTypes, t => t.Name == "ExternalRefStruct");
    }

    static string Print(MetadataSource source, string methodName)
    {
        var function = ImportFunction(source, methodName);
        return CSharpPrinter.Print(function).Output ?? "";
    }

    static string PrintRaised(MetadataSource source, string methodName)
    {
        var function = ImportFunction(source, methodName);
        IrPasses.Run(function);
        return CSharpPrinter.Print(function).Output ?? "";
    }

    static void AssertCallRefKind(MetadataSource source, string methodName, string calleeName, ArgumentRefKind expected)
    {
        var call = SingleCall(source, methodName, calleeName);
        Assert.Equal(ParameterRefKindFacts.Known, call.Callee.ParameterRefKindsFacts);
        Assert.Equal(expected, Assert.Single(call.Callee.ParameterRefKinds));
    }

    static Call SingleCall(MetadataSource source, string methodName, string calleeName)
    {
        var function = ImportFunction(source, methodName);
        return Assert.Single(function.Descendants.OfType<Call>(), c => c.Callee.Name == calleeName);
    }

    static NewObject SingleNewObject(MetadataSource source, string methodName)
    {
        var function = ImportFunction(source, methodName);
        return Assert.Single(function.Descendants.OfType<NewObject>());
    }

    static LoadField SingleField(MetadataSource source, string methodName, string fieldName)
    {
        var function = ImportFunction(source, methodName);
        return Assert.Single(function.Descendants.OfType<LoadField>(), field => field.Field.Name == fieldName);
    }

    static IrFunction ImportFunction(MetadataSource source, string methodName)
    {
        var function = IrImporter.Import(source, "ExternalFacts.Consumer", methodName);
        Assert.NotNull(function);
        function.CheckInvariant();
        return function!;
    }

    sealed class CrossAssemblyFixture : IDisposable
    {
        readonly string _directory;

        CrossAssemblyFixture(string directory, string consumerPath)
        {
            _directory = directory;
            ConsumerPath = consumerPath;
        }

        public string ConsumerPath { get; }

        public static CrossAssemblyFixture Create(bool versionDrift = false)
        {
            var directory = Directory.CreateTempSubdirectory("dotnet-inspect-method-facts-").FullName;
            try
            {
                const string librarySource = """
                    using System.Reflection;
                    using System.Runtime.CompilerServices;

                    [assembly: AssemblyVersion("1.0.0.0")]

                    namespace ExternalFacts;

                    public static class ByRefLibrary
                    {
                        public static void WriteOut(out int value) => value = 42;
                        public static void Mutate(ref int value) => value++;
                        public static void Read(in int value) { _ = value; }
                        public static void WriteExternalOut(out ExternalReference value) => value = new();
                        public static void MutateExternal(ref ExternalReference value) => value = new();
                    }

                    public delegate int ExternalDelegate(int value);

                    public static class DelegateLibrary
                    {
                        public static int DelegateTarget(int value) => value + 1;
                    }

                    public static class OperatorLikeLibrary
                    {
                        public static int op_Addition(int left, int right) => left - right;
                        public static int op_Implicit(int value) => value + 1;
                    }

                    public sealed class PropertyLibrary
                    {
                        public PropertyLibrary(int count) => Count = count;
                        public int Count { get; }
                    }

                    public sealed class DynamicLibrary
                    {
                        readonly ExternalNumber _value = new(1);
                        dynamic _reference = new ExternalNumber(4);
                        object _objectReference = new ExternalNumber(5);
                        public dynamic DynamicField = new ExternalNumber(2);
                        public object ObjectField = new ExternalNumber(3);
                        public dynamic DynamicValue => _value;
                        public dynamic GetDynamicValue() => _value;
                        public ref dynamic GetDynamicReference() => ref _reference;
                        public ref dynamic DynamicReference => ref _reference;
                        public ref object GetObjectReference() => ref _objectReference;
                    }

                    public sealed class GenericDynamicLibrary<T>
                    {
                        public T Value = default!;
                        T _reference = default!;
                        public ref T Reference => ref _reference;
                    }

                    public ref struct RefFieldLibrary
                    {
                        public ref dynamic DynamicField;
                        public ref object ObjectField;

                        public RefFieldLibrary(ref dynamic dynamicField, ref object objectField)
                        {
                            DynamicField = ref dynamicField;
                            ObjectField = ref objectField;
                        }
                    }

                    public sealed class ExternalReference
                    {
                        public static bool operator ==(ExternalReference left, object right) => true;
                        public static bool operator !=(ExternalReference left, object right) => false;
                        public override bool Equals(object? obj) => false;
                        public override int GetHashCode() => 0;
                    }

                    public readonly struct ExternalNumber
                    {
                        public ExternalNumber(int value) => Value = value;
                        public int Value { get; }
                        public static ExternalNumber operator +(ExternalNumber left, ExternalNumber right)
                            => new(left.Value + right.Value);
                    }

                    public ref struct ExternalRefStruct
                    {
                        public int Value;
                    }

                    [CompilerGenerated]
                    public static class Generated__DisplayClass0_0
                    {
                        [CompilerGenerated]
                        public static int Run(int value) => value + 1;
                    }

                    [InlineArray(4)]
                    public struct ExternalInline4
                    {
                        private int _element0;
                    }
                    """;
                string libraryPath = Emit(
                    directory,
                    "ExternalFacts.Library",
                    librarySource);
                string consumerPath = Emit(
                    directory,
                    "ExternalFacts.Consumer",
                    """
                    namespace ExternalFacts;

                    public static class Consumer
                    {
                        public static int UseOut()
                        {
                            ByRefLibrary.WriteOut(out var value);
                            return value;
                        }

                        public static int UseRef()
                        {
                            int value = 1;
                            ByRefLibrary.Mutate(ref value);
                            return value;
                        }

                        public static void UseIn()
                        {
                            int value = 1;
                            ByRefLibrary.Read(in value);
                        }

                        public static ExternalReference UseExternalOut()
                        {
                            ByRefLibrary.WriteExternalOut(out var value);
                            return value;
                        }

                        public static ExternalReference UseExternalRef(ExternalReference value)
                        {
                            ByRefLibrary.MutateExternal(ref value);
                            return value;
                        }

                        public static int UseGenerated(int value)
                            => Generated__DisplayClass0_0.Run(value);

                        public static ExternalDelegate UseExternalDelegate()
                            => DelegateLibrary.DelegateTarget;

                        public static int UseOperatorLikeAddition(int left, int right)
                            => OperatorLikeLibrary.op_Addition(left, right);

                        public static int UseOperatorLikeImplicit(int value)
                            => OperatorLikeLibrary.op_Implicit(value);

                        public static ExternalNumber UseRealOperator(ExternalNumber left, ExternalNumber right)
                            => left + right;

                        public static int UseExternalRefStruct()
                        {
                            ExternalRefStruct value = default;
                            value.Value = 3;
                            return value.Value;
                        }

                        public static int UseExternalStruct()
                        {
                            ExternalNumber value = new(3);
                            return value.Value;
                        }

                        public static int UseProperty(PropertyLibrary library)
                            => library.Count;

                        public static bool UseDynamicProperty(DynamicLibrary library, object right)
                            => (object)library.DynamicValue == right;

                        public static bool UseDynamicMethod(DynamicLibrary library, object right)
                            => (object)library.GetDynamicValue() == right;

                        public static bool UseByRefDynamicMethod(DynamicLibrary library, object right)
                            => (object)library.GetDynamicReference() == right;

                        public static bool UseByRefDynamicProperty(DynamicLibrary library, object right)
                            => (object)library.DynamicReference == right;

                        public static bool UseByRefObjectMethod(DynamicLibrary library, object right)
                            => (object)library.GetObjectReference() == right;

                        public static bool UseDynamicField(DynamicLibrary library, object right)
                            => (object)library.DynamicField == right;

                        public static bool UseObjectField(DynamicLibrary library, object right)
                            => library.ObjectField == right;

                        public static bool UseByRefDynamicField(ref RefFieldLibrary library, object right)
                            => (object)library.DynamicField == right;

                        public static bool UseByRefObjectField(ref RefFieldLibrary library, object right)
                            => (object)library.ObjectField == right;

                        public static bool UseGenericDynamicField(GenericDynamicLibrary<dynamic> library, object right)
                            => (object)library.Value == right;

                        public static bool UseGenericByRefDynamicProperty(GenericDynamicLibrary<dynamic> library, object right)
                            => (object)library.Reference == right;

                        public static bool UseExternalNewObject(object right)
                            => (object)new ExternalReference() == right;

                        public static bool UseUri(string value)
                            => System.Uri.TryCreate(value, System.UriKind.Absolute, out var uri) && uri is not null;

                        public static int UseExternalInlineArray(ExternalInline4 buffer, int index)
                        {
                            System.Span<int> span = buffer;
                            return span[index];
                        }
                    }
                    """,
                    [MetadataReference.CreateFromFile(libraryPath)]);
                if (versionDrift)
                {
                    Emit(
                        directory,
                        "ExternalFacts.Library",
                        librarySource.Replace(
                            """AssemblyVersion("1.0.0.0")""",
                            """AssemblyVersion("2.0.0.0")""",
                            StringComparison.Ordinal));
                }
                return new CrossAssemblyFixture(directory, consumerPath);
            }
            catch
            {
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        static string Emit(string directory, string assemblyName, string source, IEnumerable<MetadataReference>? additionalReferences = null)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var references = ImmutableArray.CreateBuilder<MetadataReference>();
            references.AddRange(RuntimeReferences());
            if (additionalReferences is not null)
                references.AddRange(additionalReferences);

            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source, parseOptions)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

            string path = Path.Combine(directory, assemblyName + ".dll");
            var result = compilation.Emit(path);
            Assert.True(
                result.Success,
                "fixture compilation failed:\n" + string.Join("\n", result.Diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}")));
            return path;
        }

        static ImmutableArray<MetadataReference> RuntimeReferences()
            => RoslynTestReferences.TrustedPlatform;

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    static class CrossAssemblyFixtureMethods
    {
        public const string UseOut = nameof(UseOut);
        public const string UseRef = nameof(UseRef);
        public const string UseIn = nameof(UseIn);
        public const string UseExternalOut = nameof(UseExternalOut);
        public const string UseExternalRef = nameof(UseExternalRef);
        public const string UseGenerated = nameof(UseGenerated);
        public const string UseExternalDelegate = nameof(UseExternalDelegate);
        public const string UseOperatorLikeAddition = nameof(UseOperatorLikeAddition);
        public const string UseOperatorLikeImplicit = nameof(UseOperatorLikeImplicit);
        public const string UseRealOperator = nameof(UseRealOperator);
        public const string UseExternalRefStruct = nameof(UseExternalRefStruct);
        public const string UseExternalStruct = nameof(UseExternalStruct);
        public const string UseProperty = nameof(UseProperty);
        public const string UseDynamicProperty = nameof(UseDynamicProperty);
        public const string UseDynamicMethod = nameof(UseDynamicMethod);
        public const string UseByRefDynamicMethod = nameof(UseByRefDynamicMethod);
        public const string UseByRefDynamicProperty = nameof(UseByRefDynamicProperty);
        public const string UseByRefObjectMethod = nameof(UseByRefObjectMethod);
        public const string UseDynamicField = nameof(UseDynamicField);
        public const string UseObjectField = nameof(UseObjectField);
        public const string UseByRefDynamicField = nameof(UseByRefDynamicField);
        public const string UseByRefObjectField = nameof(UseByRefObjectField);
        public const string UseGenericDynamicField = nameof(UseGenericDynamicField);
        public const string UseGenericByRefDynamicProperty = nameof(UseGenericByRefDynamicProperty);
        public const string UseExternalNewObject = nameof(UseExternalNewObject);
        public const string UseUri = nameof(UseUri);
        public const string UseExternalInlineArray = nameof(UseExternalInlineArray);
    }
}
