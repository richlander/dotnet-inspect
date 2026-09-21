using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using ILInspector.Instructions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Source")]
public sealed partial class SelectedPropertySourceTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.SelectedPropertySamples";
    const string FieldHelperQualifier = "global::ILInspector.Decompiler.Fixtures.FieldKeyword.";

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

    [Theory]
    [InlineData("SelectedAutoPropertySamples", "ComputedCount", "field + 1", false, false)]
    [InlineData("SelectedFieldPropertySamples", "Count", "field + 1", false, false)]
    [InlineData("SelectedFieldPropertySamples", "SharedCount", "field + 2", true, false)]
    [InlineData("SelectedFieldPropertySamples", "RepeatedCount", "field + field", false, false)]
    [InlineData("SelectedFieldPropertySamples", "CheckedCount", "checked", false, false)]
    [InlineData("SelectedFieldPropertySamples", "Label", "field ?? \"this.Label / field\"", false, false)]
    [InlineData("SelectedFieldPropertySamples", "event", "field + 3", false, false)]
    [InlineData("SelectedFieldPropertySamples", "BranchedCount", "field", false, false)]
    [InlineData("SelectedFieldPropertySamples", "AttributedCount", "[DebuggerStepThrough]", false, false)]
    [InlineData("GenericFieldPropertySamples`1", "Value", "field", false, false)]
    [InlineData("GenericFieldPropertySamples`1", "SharedCount", "field + 2", true, false)]
    [InlineData("ReadonlyFieldPropertySamples", "Count", "field + 1", false, true)]
    [InlineData("DerivedFieldPropertySamples", "Limit", "override int Limit", false, false)]
    public void ComputedGetterPreservesItsFieldAndComputation(
        string typeName, string propertyName, string expected, bool isStatic, bool isReadOnly)
    {
        foreach (bool updated in new[] { false, true })
        {
            string path = FixturePath(updated);
            var (type, accessor) = Select(path,
                $"ILInspector.Decompiler.Fixtures.{typeName}", propertyName, "get");
            var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
            Assert.Contains(expected, result.Text);
            Assert.DoesNotContain($"get_{propertyName}(", result.Text);
            var compilation = AssertCompiles(MemberBodyProducer.Project(type, path, pdbPath: null).Output!, path);
            var projectedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
                compilation.Assembly.GetTypeByMetadataName(type.FullName));
            var storage = Assert.Single(projectedType.GetMembers().OfType<IFieldSymbol>());
            Assert.Equal(isReadOnly, storage.IsReadOnly);
            Assert.Equal(isStatic, storage.IsStatic);
            var property = Assert.IsAssignableFrom<IPropertySymbol>(storage.AssociatedSymbol);
            Assert.Null(property.SetMethod);
            if (propertyName is "Count" or "ComputedCount" or "SharedCount"
                or "RepeatedCount" or "CheckedCount" or "event")
                AssertGetterInstructionsMatch(compilation, path, accessor);
        }
    }

    [Fact]
    public void PublishedDocoptGetterKeepsItsFieldAndNullFallback()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "FieldGetter", "DocoptNet.dll");
        var (type, accessor) = Select(path, "DocoptNet.Internals.ReadOnlyList`1", "List", "get");
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains("List => field ?? Array.Empty<T>();", result.Text);
        Assert.DoesNotContain("get_List(", result.Text);
        Assert.DoesNotContain("this.List", result.Text);
    }

    [Theory]
    [InlineData("FieldKeywordGetterSamples", "Value", FieldHelperQualifier + "field.Keep(field)")]
    [InlineData("FieldKeywordGetterSamples", "Count", FieldHelperQualifier + "field.Keep(field)")]
    [InlineData("FieldKeywordGetterSamples", "StaticCount", FieldHelperQualifier + "field.Keep(field)")]
    [InlineData("GenericFieldKeywordGetterSamples`1", "Count", FieldHelperQualifier + "field<T>.Keep(field)")]
    [InlineData("TypeParameterFieldKeywordGetterSamples`1", "Count", "@field.Keep(field)")]
    public void FieldKeywordTypeQualifierKeepsItsStaticCallTarget(
        string typeName, string propertyName, string call)
    {
        foreach (bool updated in new[] { false, true })
        {
            string path = FixturePath(updated);
            var (type, accessor) = Select(path,
                $"ILInspector.Decompiler.Fixtures.FieldKeyword.{typeName}", propertyName, "get");
            var member = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, member.Status);
            Assert.Contains(call, member.Text);
            Assert.DoesNotContain($"get_{propertyName}(", member.Text);
            string listing = MemberBodyProducer.Project(type, path, pdbPath: null).Output!;
            AssertGetterInstructionsMatch(AssertCompiles(listing, path), path, accessor);
        }
    }

    static void AssertGetterInstructionsMatch(
        CSharpCompilation compilation, string path, ApiMember accessor,
        bool normalizeFrameworkFacades = false)
    {
        using var original = new PEReader(File.OpenRead(path));
        var originalReader = original.GetMetadataReader();
        var originalMethod = originalReader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(accessor.MetadataToken!.Value));
        using var image = new MemoryStream();
        Assert.True(compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
        image.Position = 0;
        using var projected = new PEReader(image);
        var projectedReader = projected.GetMetadataReader();
        var projectedHandle = Assert.Single(projectedReader.MethodDefinitions,
            handle => projectedReader.GetString(projectedReader.GetMethodDefinition(handle).Name) == accessor.Name);
        var projectedMethod = projectedReader.GetMethodDefinition(projectedHandle);
        var originalInstructions = MethodInstructions.Decode(original.GetMethodBody(originalMethod.RelativeVirtualAddress));
        var projectedInstructions = MethodInstructions.Decode(projected.GetMethodBody(projectedMethod.RelativeVirtualAddress));
        Assert.True(originalInstructions.IsComplete);
        Assert.True(projectedInstructions.IsComplete);
        Assert.Equal(originalInstructions.Instructions.Select(instruction => instruction.OpCode),
            projectedInstructions.Instructions.Select(instruction => instruction.OpCode));
        Assert.Equal(originalInstructions.Instructions
                .Where(instruction => instruction.Operand is not
                    (OperandKind.InlineField or OperandKind.InlineMethod or OperandKind.InlineType))
                .Select(instruction => instruction.OperandValue),
            projectedInstructions.Instructions
                .Where(instruction => instruction.Operand is not
                    (OperandKind.InlineField or OperandKind.InlineMethod or OperandKind.InlineType))
                .Select(instruction => instruction.OperandValue));
        // The unchanged projection references helpers that were local to the input assembly.
        string inputAssembly = $"[{originalReader.GetString(originalReader.GetAssemblyDefinition().Name)}]";
        string Normalize(string operand)
        {
            operand = operand.Replace(inputAssembly, "", StringComparison.Ordinal);
            return normalizeFrameworkFacades
                ? operand.Replace("['netstandard']", "[System.Private.CoreLib]", StringComparison.Ordinal)
                    .Replace("[System.Runtime]", "[System.Private.CoreLib]", StringComparison.Ordinal)
                : operand;
        }
        Assert.Equal(originalInstructions.Instructions
                .Where(instruction => instruction.Operand == OperandKind.InlineType)
                .Select(instruction => CanonicalIL.ResolveType(originalReader, (int)instruction.OperandValue)
                    .Replace(inputAssembly, "", StringComparison.Ordinal)),
            projectedInstructions.Instructions
                .Where(instruction => instruction.Operand == OperandKind.InlineType)
                .Select(instruction => CanonicalIL.ResolveType(projectedReader, (int)instruction.OperandValue)
                    .Replace(inputAssembly, "", StringComparison.Ordinal)));
        Assert.Equal(originalInstructions.Instructions
                .Where(instruction => instruction.Operand == OperandKind.InlineMethod)
                .Select(instruction => Normalize(CanonicalIL.ResolveMethod(originalReader, (int)instruction.OperandValue))),
            projectedInstructions.Instructions
                .Where(instruction => instruction.Operand == OperandKind.InlineMethod)
                .Select(instruction => Normalize(CanonicalIL.ResolveMethod(projectedReader, (int)instruction.OperandValue))));
    }

    [Theory]
    [InlineData("ChangingCount")]
    [InlineData("LazyLabel")]
    [InlineData("MutableCount")]
    [InlineData("InitialCount")]
    [InlineData("DescribedCount")]
    [InlineData("NestedCount")]
    [InlineData("ProtectedCount")]
    public void UnsupportedFieldGetterRetainsMethodForm(string propertyName)
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path,
            "ILInspector.Decompiler.Fixtures.SelectedFieldPropertySamples", propertyName, "get");
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains($"get_{propertyName}(", result.Text);
    }

    [Fact]
    public void FieldNamedPdbLocalRetainsMethodForm()
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path,
            "ILInspector.Decompiler.Fixtures.SelectedFieldPropertySamples", "ShadowedCount", "get");
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, Path.ChangeExtension(path, ".pdb"));
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains("get_ShadowedCount()", result.Text);
    }

    [Theory]
    [InlineData("own-receiver", true)]
    [InlineData("other-receiver", false)]
    [InlineData("address", false)]
    [InlineData("volatile", false)]
    [InlineData("additional-field", false)]
    [InlineData("readonly", false)]
    public void FieldGetterRequiresOnlyOrdinaryReadsOfItsOwnStorage(string shape, bool expectedProperty)
    {
        string path = Path.Combine(Path.GetTempPath(), $"field-read-{Guid.NewGuid():N}.dll");
        try
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName("FieldRead"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("FieldRead");
            var declaringType = module.DefineType("FieldRead", TypeAttributes.Public);
            var field = declaringType.DefineField(
                "<Count>k__BackingField", typeof(int),
                FieldAttributes.Private | (shape == "readonly" ? FieldAttributes.InitOnly : 0));
            field.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!, []));
            var getter = declaringType.DefineMethod(
                "get_Count", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                typeof(int), Type.EmptyTypes);
            var body = getter.GetILGenerator();
            body.Emit(shape == "other-receiver" ? OpCodes.Ldnull : OpCodes.Ldarg_0);
            if (shape == "volatile")
                body.Emit(OpCodes.Volatile);
            body.Emit(shape == "address" ? OpCodes.Ldflda : OpCodes.Ldfld, field);
            if (shape == "address")
                body.Emit(OpCodes.Ldind_I4);
            if (shape == "additional-field")
            {
                var extra = declaringType.DefineField("_offset", typeof(int), FieldAttributes.Private);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, extra);
            }
            else
                body.Emit(OpCodes.Ldc_I4_1);
            body.Emit(OpCodes.Add);
            body.Emit(OpCodes.Ret);
            declaringType.DefineProperty("Count", PropertyAttributes.None, typeof(int), Type.EmptyTypes)
                .SetGetMethod(getter);
            declaringType.CreateType();
            assembly.Save(path);

            var (type, accessor) = Select(path, "FieldRead", "Count", "get");
            var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
            Assert.Equal(expectedProperty,
                result.Text!.Contains("public int Count => field + 1;", StringComparison.Ordinal));
            if (expectedProperty)
                AssertGetterInstructionsMatch(
                    AssertCompiles(MemberBodyProducer.Project(type, path, pdbPath: null).Output!), path, accessor);
            else
                Assert.Contains("get_Count()", result.Text);
        }
        finally
        {
            File.Delete(path);
        }
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

    [Theory]
    [InlineData("ExplicitAutoPropertySamples", "int IAutoPropertySample.Count { get; }")]
    [InlineData("ExplicitFieldPropertySamples", "int IAutoPropertySample.Count => field + 1;")]
    public void ExplicitAutomaticGetterKeepsItsInterfaceBinding(string typeName, string expected)
    {
        string path = FixturePath(false);
        var type = Extract(path, $"ILInspector.Decompiler.Fixtures.{typeName}");
        var property = Assert.Single(type.Members, member =>
            member.Kind == "property" && member.GetterToken is not null);
        var accessor = Assert.Single(ApiMemberAccessors.Create(property, type));
        type.Members = [accessor];
        var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Contains(expected, result.Text);
        var compilation = AssertCompiles(
            MemberBodyProducer.Project(type, path, pdbPath: null).Output!, path);
        var projectedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.Assembly.GetTypeByMetadataName(type.FullName));
        var projectedProperty = Assert.Single(projectedType.GetMembers().OfType<IPropertySymbol>());
        Assert.Single(projectedProperty.ExplicitInterfaceImplementations);
        Assert.Null(projectedProperty.SetMethod);
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, false, true)]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, true, true)]
    [InlineData(false, true, false, true, true)]
    public void AutomaticGetterRequiresItsOwnReadonlyGenericStorage(
        bool foreignInstantiation, bool readonlyStorage, bool expectedAutomatic, bool sameNameNeighbor,
        bool computed = false)
    {
        string path = Path.Combine(Path.GetTempPath(), $"selected-storage-{Guid.NewGuid():N}.dll");
        try
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName("SelectedStorage"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("SelectedStorage");
            var declaringType = module.DefineType("SelectedStorage", TypeAttributes.Public);
            var parameter = declaringType.DefineGenericParameters("T")[0];
            var marker = new CustomAttributeBuilder(
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!, []);
            if (sameNameNeighbor)
            {
                var otherField = declaringType.DefineField(
                    "<Count>k__BackingField", typeof(long), FieldAttributes.Private | FieldAttributes.Static
                        | (readonlyStorage ? 0 : FieldAttributes.InitOnly));
                otherField.SetCustomAttribute(marker);
            }
            var field = declaringType.DefineField(
                "<Count>k__BackingField", typeof(int), FieldAttributes.Private | FieldAttributes.Static
                    | (readonlyStorage ? FieldAttributes.InitOnly : 0));
            field.SetCustomAttribute(marker);
            var getter = declaringType.DefineMethod(
                "get_Count", MethodAttributes.Public | MethodAttributes.Static
                    | MethodAttributes.SpecialName | MethodAttributes.HideBySig, typeof(int), Type.EmptyTypes);
            getter.SetCustomAttribute(marker);
            var body = getter.GetILGenerator();
            body.Emit(OpCodes.Ldsfld, TypeBuilder.GetField(
                declaringType.MakeGenericType(foreignInstantiation ? typeof(int) : parameter), field));
            if (computed)
            {
                body.Emit(OpCodes.Ldc_I4_1);
                body.Emit(OpCodes.Add);
            }
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
            Assert.Equal(expectedAutomatic, result.Text!.Contains(
                computed ? "=> field + 1;" : "{ get; }", StringComparison.Ordinal));
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

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public void AutomaticGetterDeclinesModifiedStorage(bool required, bool nested, bool computed = false)
    {
        string path = Path.Combine(Path.GetTempPath(), $"modified-storage-{Guid.NewGuid():N}.dll");
        try
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName("ModifiedStorage"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("ModifiedStorage");
            var declaringType = module.DefineType("ModifiedStorage", TypeAttributes.Public);
            Type storageType = nested ? typeof(int[]) : typeof(int);
            Type modifier = required ? typeof(System.Runtime.CompilerServices.IsVolatile)
                : typeof(System.Runtime.CompilerServices.IsConst);
            var field = declaringType.DefineField(
                "<Count>k__BackingField", storageType,
                required ? [modifier] : null, required ? null : [modifier],
                FieldAttributes.Private | FieldAttributes.Static
                    | (computed ? 0 : FieldAttributes.InitOnly));
            var marker = new CustomAttributeBuilder(
                typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!, []);
            field.SetCustomAttribute(marker);
            var getter = declaringType.DefineMethod(
                "get_Count", MethodAttributes.Public | MethodAttributes.Static
                    | MethodAttributes.SpecialName | MethodAttributes.HideBySig, storageType, Type.EmptyTypes);
            getter.SetCustomAttribute(marker);
            getter.GetILGenerator().Emit(OpCodes.Ldsfld, field);
            if (computed)
            {
                getter.GetILGenerator().Emit(OpCodes.Dup);
                getter.GetILGenerator().Emit(OpCodes.Pop);
            }
            getter.GetILGenerator().Emit(OpCodes.Ret);
            declaringType.DefineProperty("Count", PropertyAttributes.None, storageType, Type.EmptyTypes)
                .SetGetMethod(getter);
            declaringType.CreateType();
            assembly.Save(path);

            if (nested)
            {
                // Reflection.Emit exposes top-level modifiers; place this one on the array element.
                byte[] image = File.ReadAllBytes(path);
                using var pe = new PEReader(new MemoryStream(image));
                var reader = pe.GetMetadataReader();
                var signatureHandle = reader.GetFieldDefinition(Assert.Single(reader.FieldDefinitions)).Signature;
                byte[] signature = reader.GetBlobBytes(signatureHandle);
                Assert.Equal((byte)SignatureTypeCode.SZArray, signature[^2]);
                Assert.Equal((byte)SignatureTypeCode.Int32, signature[^1]);
                int offset = pe.PEHeaders.MetadataStartOffset
                    + reader.GetHeapMetadataOffset(HeapIndex.Blob) + MetadataTokens.GetHeapOffset(signatureHandle);
                Assert.Equal(signature.Length, image[offset]);
                image[offset + 2] = (byte)SignatureTypeCode.SZArray;
                signature.AsSpan(1, signature.Length - 3).CopyTo(image.AsSpan(offset + 3));
                File.WriteAllBytes(path, image);
            }

            var type = Extract(path, "ModifiedStorage");
            var property = Assert.Single(type.Members, member => member.Kind == "property");
            var accessor = Assert.Single(ApiMemberAccessors.Create(property, type));
            type.Members = [accessor];
            var result = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null);
            Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
            Assert.Contains("get_Count()", result.Text);
            Assert.DoesNotContain("{ get; }", result.Text);
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
