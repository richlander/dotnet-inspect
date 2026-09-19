using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Source")]
public sealed class SelectedPropertySourceTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.SelectedPropertySamples";

    [Theory]
    [InlineData("Capacity", "get", "public virtual int Capacity")]
    [InlineData("Capacity", "set", "public virtual int Capacity")]
    [InlineData("Count", "set", "private int Count")]
    [InlineData("InitialCount", "set", "public int InitialCount")]
    [InlineData("Item", "get", "public int Item")]
    [InlineData("event", "get", "public int @event")]
    [InlineData("SharedCount", "get", "public static int SharedCount")]
    [InlineData("Storage", "get", "public ref int Storage")]
    public void SelectedAccessorHasPropertyEnvelopeAndCompiles(
        string propertyName, string role, string expected)
    {
        foreach (bool updated in new[] { false, true })
        {
            string path = FixturePath(updated);
            var (type, accessor) = Select(path, FixtureType, propertyName, role);
            var member = MemberBodyProducer.ProduceMember(
                type, accessor, path, pdbPath: null,
                attributeMode: MemberRenderAttributeMode.CompilationRequired);
            Assert.Equal(MemberBodyProductionStatus.Complete, member.Status);
            Assert.Contains(expected, member.Text);
            Assert.DoesNotContain($"get_{propertyName}(", member.Text);
            Assert.DoesNotContain($"set_{propertyName}(", member.Text);
            if (role == "set")
            {
                Assert.Contains(propertyName == "InitialCount" ? "init =>" : "set =>", member.Text);
                Assert.DoesNotContain("get =>", member.Text);
            }
            else
                Assert.DoesNotContain("set =>", member.Text);

            string listing = MemberBodyProducer.Project(type, path, pdbPath: null).Output!;
            Assert.Contains(member.Text!.Trim(), listing);
            AssertCompiles(listing);
        }
    }

    [Theory]
    [InlineData("DerivedPropertySamples", "Capacity", "public override int Capacity")]
    [InlineData("ReadonlyPropertySamples", "Count", "public readonly int Count")]
    public void SelectedGetterPreservesPhysicalModifiers(
        string typeName, string propertyName, string expected)
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path, $"ILInspector.Decompiler.Fixtures.{typeName}", propertyName, "get");
        var result = MemberBodyProducer.ProduceMember(
            type, accessor, path, pdbPath: null,
            attributeMode: MemberRenderAttributeMode.CompilationRequired);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains(expected, result.Text);
    }

    [Theory]
    [InlineData("Count", "set", "protected override void set_Count(int value)")]
    [InlineData("Offset", "get", "protected override int get_Offset()")]
    public void NarrowedOverrideAccessorsRetainMethodForm(
        string propertyName, string role, string expected)
    {
        foreach (bool updated in new[] { false, true })
        {
            string path = FixturePath(updated);
            var (type, accessor) = Select(path,
                "ILInspector.Decompiler.Fixtures.NarrowedOverridePropertySamples", propertyName, role);
            var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
            Assert.Contains(expected, result.Text);
        }
    }

    [Theory]
    [InlineData("NarrowedOverridePropertySamples", "Count", "get", "public override int Count")]
    [InlineData("NarrowedOverridePropertySamples", "Offset", "set", "public override int Offset")]
    [InlineData("NarrowedPropertySamples", "Count", "set", "protected virtual int Count")]
    [InlineData("NarrowedPropertySamples", "Offset", "get", "protected virtual int Offset")]
    public void RepresentableOverrideAndNarrowedAccessorsCompile(
        string typeName, string propertyName, string role, string expected)
    {
        foreach (bool updated in new[] { false, true })
        {
            string path = FixturePath(updated);
            var (type, accessor) = Select(path,
                $"ILInspector.Decompiler.Fixtures.{typeName}", propertyName, role);
            var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
            Assert.Contains(expected, result.Text);
            Assert.DoesNotContain($"{role}_{propertyName}(", result.Text);
            string listing = MemberBodyProducer.Project(type, path, pdbPath: null).Output!;
            Assert.Contains(result.Text!.Trim(), listing);
            AssertCompiles(listing, path);
        }
    }

    [Fact]
    public void AttributesStayOnTheSelectedAccessor()
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path, FixtureType, "Label", "get");
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains("public string? Label\n    {\n", result.Text);
        Assert.Contains("[DebuggerStepThrough]", result.Text);
        Assert.Contains("MaybeNull", result.Text);
        Assert.True(result.Text!.IndexOf("[DebuggerStepThrough]", StringComparison.Ordinal)
            > result.Text.IndexOf("public string? Label", StringComparison.Ordinal));
        Assert.DoesNotContain("set =>", result.Text);
        AssertCompiles(MemberBodyProducer.Project(type, path, pdbPath: null).Output!);
    }

    [Theory]
    [InlineData("Count", "get", "public static virtual int Count", true, true)]
    [InlineData("Capacity", "set", "public static virtual int Capacity", true, true)]
    [InlineData("FixedCount", "get", "public static int FixedCount", true, false)]
    [InlineData("InstanceCount", "get", "public virtual int InstanceCount", false, true)]
    [InlineData("SealedCount", "get", "public sealed int SealedCount", false, false)]
    [InlineData("SealedCapacity", "set", "public sealed int SealedCapacity", false, false)]
    [InlineData("PrivateCount", "get", "private int PrivateCount", false, false)]
    public void InterfaceAccessorCompilesWithItsDispatchSemantics(
        string propertyName, string role, string expected, bool isStatic, bool isVirtual)
    {
        const string typeName = "ILInspector.Decompiler.Fixtures.IStaticPropertySamples";
        foreach (bool updated in new[] { false, true })
        {
            string path = FixturePath(updated);
            var (type, accessor) = Select(path, typeName, propertyName, role);
            var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
            Assert.Contains(expected, result.Text);
            string listing = MemberBodyProducer.Project(type, path, pdbPath: null).Output!;
            Assert.Contains(result.Text!.Trim(), listing);
            var compilation = AssertCompiles(listing);
            var projectedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
                compilation.Assembly.GetTypeByMetadataName(typeName));
            var property = Assert.IsAssignableFrom<IPropertySymbol>(
                Assert.Single(projectedType.GetMembers(propertyName)));
            Assert.Equal(isStatic, property.IsStatic);
            Assert.Equal(isVirtual, property.IsVirtual);
            Assert.Equal(role == "get", property.GetMethod is not null);
            Assert.Equal(role == "set", property.SetMethod is not null);
        }
    }

    [Theory]
    [InlineData("AutoCount", "get", "get_AutoCount()")]
    [InlineData("AutoCount", "set", "set_AutoCount(int value)")]
    [InlineData("ByIndex", "get", "get_ByIndex(int index)")]
    public void BackingStorageAndIndexersRetainMethodForm(
        string propertyName, string role, string expected)
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path, FixtureType, propertyName, role);
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains(expected, result.Text);
    }

    [Theory]
    [InlineData("SelectedAutoPropertySamples", "Count", "public int Count { get; }")]
    [InlineData("SelectedAutoPropertySamples", "SharedCount", "public static int SharedCount { get; }")]
    [InlineData("SelectedAutoPropertySamples", "Limit", "public virtual int Limit { get; }")]
    [InlineData("SelectedAutoPropertySamples", "event", "public int @event { get; }")]
    [InlineData("DerivedAutoPropertySamples", "Limit", "public override int Limit { get; }")]
    [InlineData("GenericAutoPropertySamples`1", "Item", "public T? Item { get; }")]
    [InlineData("GenericAutoPropertySamples`1", "SharedCount", "public static int SharedCount { get; }")]
    [InlineData("StructAutoPropertySamples", "Count", "public readonly int Count { get; }")]
    public void GetterOnlyAutoPropertyPreservesBackingStorage(
        string typeName, string propertyName, string expected)
    {
        foreach (bool updated in new[] { false, true })
        {
            string path = FixturePath(updated);
            var (type, accessor) = Select(path,
                $"ILInspector.Decompiler.Fixtures.{typeName}", propertyName, "get");
            var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
            Assert.Contains(expected, result.Text);
            Assert.DoesNotContain("=>", result.Text);
            string listing = MemberBodyProducer.Project(type, path, pdbPath: null).Output!;
            var compilation = AssertCompiles(listing, path);
            var projectedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
                compilation.Assembly.GetTypeByMetadataName(type.FullName));
            var property = Assert.IsAssignableFrom<IPropertySymbol>(
                Assert.Single(projectedType.GetMembers(propertyName)));
            Assert.NotNull(property.GetMethod);
            Assert.Null(property.SetMethod);
            var storage = Assert.Single(projectedType.GetMembers().OfType<IFieldSymbol>());
            Assert.Same(property, storage.AssociatedSymbol);
            Assert.True(storage.IsReadOnly);
            Assert.Equal(property.IsStatic, storage.IsStatic);
        }
    }

    [Theory]
    [InlineData("MutableCount", "get")]
    [InlineData("MutableCount", "set")]
    [InlineData("InitialCount", "get")]
    [InlineData("InitialCount", "set")]
    [InlineData("ComputedCount", "get")]
    [InlineData("DescribedCount", "get")]
    [InlineData("DebugCount", "get")]
    public void UnsupportedBackingStorageRetainsMethodForm(string propertyName, string role)
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path,
            "ILInspector.Decompiler.Fixtures.SelectedAutoPropertySamples", propertyName, role);
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains($"{role}_{propertyName}(", result.Text);
        Assert.DoesNotContain("{ get; }", result.Text);
    }

    [Fact]
    public void AutomaticGetterRetainsAccessorAttributes()
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path,
            "ILInspector.Decompiler.Fixtures.SelectedAutoPropertySamples", "Label", "get");
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains("[DebuggerStepThrough]", result.Text);
        Assert.Contains("MaybeNull", result.Text);
        Assert.Contains("get;", result.Text);
        Assert.DoesNotContain("return this.", result.Text);
        AssertCompiles(MemberBodyProducer.Project(type, path, pdbPath: null).Output!, path);
    }

    [Fact]
    public void ExplicitAutomaticGetterKeepsItsInterfaceBinding()
    {
        string path = FixturePath(false);
        var type = Extract(path, "ILInspector.Decompiler.Fixtures.ExplicitAutoPropertySamples");
        var property = Assert.Single(type.Members, member =>
            member.Kind == "property" && member.GetterToken is not null);
        var accessor = Assert.Single(ApiMemberAccessors.Create(property, type));
        type.Members = [accessor];
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains("int IAutoPropertySample.Count { get; }", result.Text);
        var compilation = AssertCompiles(
            MemberBodyProducer.Project(type, path, pdbPath: null).Output!, path);
        var projectedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.Assembly.GetTypeByMetadataName(type.FullName));
        var projectedProperty = Assert.Single(projectedType.GetMembers().OfType<IPropertySymbol>());
        Assert.Single(projectedProperty.ExplicitInterfaceImplementations);
        Assert.Null(projectedProperty.SetMethod);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void AutomaticGetterRequiresItsOwnReadonlyGenericStorage(
        bool foreignInstantiation, bool readonlyStorage, bool expectedAutomatic)
    {
        string path = Path.Combine(Path.GetTempPath(), $"selected-storage-{Guid.NewGuid():N}.dll");
        try
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName("SelectedStorage"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("SelectedStorage");
            var declaringType = module.DefineType("SelectedStorage", TypeAttributes.Public);
            var parameter = declaringType.DefineGenericParameters("T")[0];
            var field = declaringType.DefineField(
                "<Count>k__BackingField", typeof(int), FieldAttributes.Private | FieldAttributes.Static
                    | (readonlyStorage ? FieldAttributes.InitOnly : 0));
            var marker = new CustomAttributeBuilder(
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!, []);
            field.SetCustomAttribute(marker);
            var getter = declaringType.DefineMethod(
                "get_Count", MethodAttributes.Public | MethodAttributes.Static
                    | MethodAttributes.SpecialName | MethodAttributes.HideBySig, typeof(int), Type.EmptyTypes);
            getter.SetCustomAttribute(marker);
            var body = getter.GetILGenerator();
            body.Emit(OpCodes.Ldsfld, TypeBuilder.GetField(
                declaringType.MakeGenericType(foreignInstantiation ? typeof(int) : parameter), field));
            body.Emit(OpCodes.Ret);
            declaringType.DefineProperty("Count", PropertyAttributes.None, typeof(int), Type.EmptyTypes)
                .SetGetMethod(getter);
            declaringType.CreateType();
            assembly.Save(path);

            var type = Extract(path, "SelectedStorage");
            var property = Assert.Single(type.Members, member => member.Kind == "property");
            var accessor = Assert.Single(ApiMemberAccessors.Create(property, type));
            type.Members = [accessor];
            var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
            Assert.Equal(expectedAutomatic, result.Text!.Contains("{ get; }", StringComparison.Ordinal));
            if (expectedAutomatic)
                AssertCompiles(MemberBodyProducer.Project(type, path, pdbPath: null).Output!);
            else
                Assert.Contains("get_Count()", result.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void RuntimeNullabilityAttributeRecoversItsAutoProperty()
    {
        string path = typeof(int).Assembly.Location;
        var (type, accessor) = Select(path,
            "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute", "ParameterName", "get");
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains("public string ParameterName { get; }", result.Text);
        Assert.DoesNotContain("this.ParameterName", result.Text);
        AssertCompiles(MemberBodyProducer.Project(type, path, pdbPath: null).Output!);
    }

    [Theory]
    [InlineData("get_CapacityLookalike")]
    [InlineData("set_CapacityLookalike")]
    public void AccessorLikeMethodsStayMethods(string methodName)
    {
        string path = FixturePath(false);
        var type = Extract(path, FixtureType);
        var member = Assert.Single(type.Members, candidate => candidate.Name == methodName);
        var result = MemberBodyProducer.ProduceMember(type, member, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains(methodName + "(", result.Text);
    }

    [Fact]
    public void RuntimeSqlBytesUsesAPropertyDeclaration()
    {
        string path = typeof(System.Data.SqlTypes.SqlBytes).Assembly.Location;
        var (type, accessor) = Select(path, "System.Data.SqlTypes.SqlBytes", "MaxLength", "get");
        var result = MemberBodyProducer.ProduceMember(
            type, accessor, path, pdbPath: null,
            attributeMode: MemberRenderAttributeMode.CompilationRequired);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains("public long MaxLength => _state switch", result.Text);
        Assert.DoesNotContain("get_MaxLength()", result.Text);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void RuntimeBinaryNumberPreservesStaticVirtualPropertyModifier()
    {
        string path = typeof(int).Assembly.Location;
        var (type, accessor) = Select(path, "System.Numerics.IBinaryNumber`1", "AllBitsSet", "get");
        var result = MemberBodyProducer.ProduceMember(
            type, accessor, path, pdbPath: null,
            attributeMode: MemberRenderAttributeMode.CompilationRequired);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains("public static virtual TSelf AllBitsSet", result.Text);
        Assert.DoesNotContain("override", result.Text);
    }

    static (ApiType Type, ApiMember Accessor) Select(
        string path, string typeName, string propertyName, string role)
    {
        var type = Extract(path, typeName);
        var property = Assert.Single(type.Members,
            member => member.Kind == "property" && member.Name == propertyName);
        var accessor = Assert.Single(ApiMemberAccessors.Create(property, type),
            member => member.MethodSemantics == (role == "get"
                ? ApiMethodSemanticsKind.PropertyGetter : ApiMethodSemanticsKind.PropertySetter));
        type.Members = [accessor];
        return (type, accessor);
    }

    static ApiType Extract(string path, string typeName)
    {
        using var pe = new PEReader(File.OpenRead(path));
        return Assert.Single(ApiSurfaceExtractor.Extract(pe, includeAll: true).Types,
            type => type.FullName == typeName);
    }

    static string FixturePath(bool updated)
        => (updated ? FixtureCatalog.DecompilerUnsafeNew : FixtureCatalog.DecompilerUnsafeLegacy).AssemblyPath();

    static CSharpCompilation AssertCompiles(string listing, string? referencePath = null)
    {
        IEnumerable<MetadataReference> references = RoslynTestReferences.TrustedPlatform;
        if (referencePath is not null)
            references = references.Where(reference =>
                Path.GetFileName(reference.Display) != FixtureCatalog.DecompilerUnsafeLegacy.AssemblyFileName
                && Path.GetFileName(reference.Display) != FixtureCatalog.DecompilerUnsafeNew.AssemblyFileName)
                .Append(MetadataReference.CreateFromFile(referencePath));
        var compilation = CSharpCompilation.Create(
            "SelectedPropertyProjection",
            [CSharpSyntaxTree.ParseText(listing, new CSharpParseOptions(LanguageVersion.Preview),
                cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        using var output = new MemoryStream();
        var result = compilation.Emit(output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, $"{listing}\n{string.Join("\n", result.Diagnostics)}");
        return compilation;
    }
}
