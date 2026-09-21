using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using DotnetInspector.RoundTripCompilation;
using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public partial class ReturnToSenderPrototypeTests
{
    [Theory]
    [InlineData("SelectedAutoPropertySamples", "ComputedCount", false)]
    [InlineData("SelectedAutoPropertySamples", "ComputedCount", true)]
    [InlineData("SelectedAutoPropertySamples", "Count", false)]
    [InlineData("SelectedAutoPropertySamples", "Count", true)]
    [InlineData("SelectedAutoPropertySamples", "SharedCount", false)]
    [InlineData("SelectedAutoPropertySamples", "DescribedCount", false)]
    [InlineData("SelectedAutoPropertySamples", "DebugCount", false)]
    [InlineData("SelectedFieldPropertySamples", "Count", false)]
    [InlineData("SelectedFieldPropertySamples", "SharedCount", false)]
    [InlineData("SelectedFieldPropertySamples", "RepeatedCount", false)]
    [InlineData("SelectedFieldPropertySamples", "CheckedCount", false)]
    [InlineData("SelectedFieldPropertySamples", "Label", false)]
    [InlineData("SelectedFieldPropertySamples", "event", false)]
    [InlineData("SelectedFieldPropertySamples", "BranchedCount", false)]
    [InlineData("GenericFieldPropertySamples`1", "SharedCount", false)]
    [InlineData("ReadonlyFieldPropertySamples", "Count", false)]
    [InlineData("ReadonlyFieldPropertySamples", "Count", true)]
    [InlineData("FieldKeyword.FieldKeywordGetterSamples", "Value", false)]
    [InlineData("FieldKeyword.FieldKeywordGetterSamples", "Count", false)]
    [InlineData("FieldKeyword.FieldKeywordGetterSamples", "StaticCount", false)]
    [InlineData("FieldKeyword.GenericFieldKeywordGetterSamples`1", "Count", false)]
    [InlineData("FieldKeyword.TypeParameterFieldKeywordGetterSamples`1", "Count", false)]
    public async Task NativeGetterRetainsItsStorageAndComputation(
        string typeName, string propertyName, bool full)
    {
        string path = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
        string fullType = $"ILInspector.Decompiler.Fixtures.{typeName}";
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            path,
            [new ReturnToSender.RequestedTarget(fullType, $"get_{propertyName}", 0)],
            RoundTripScope.Cluster,
            full ? RoundTripBodyPolicy.Full : RoundTripBodyPolicy.Selected,
            applyCompileBackFloor: false));

        AssertNativeGetterStorage(path, typeName.Split('.').Last(), propertyName, result);
        bool automatic = typeName == "SelectedAutoPropertySamples" && propertyName != "ComputedCount";
        if (automatic)
            Assert.Contains($"{propertyName} {{ get; }}", result.Source);
        else
            Assert.Contains("field", result.TargetBody);
        if (propertyName == "ComputedCount")
            Assert.Contains("field + 1", result.TargetBody);
    }

    [Fact]
    public async Task PublishedDocoptNativeGetterRetainsItsStorageAndNullFallback()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "FieldGetter", "DocoptNet.dll");
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            path,
            [new ReturnToSender.RequestedTarget("DocoptNet.Internals.ReadOnlyList`1", "get_List", 0)],
            applyCompileBackFloor: false));

        AssertNativeGetterStorage(path, "ReadOnlyList`1", "List", result);
        Assert.Contains("return field ?? Array.Empty<T>();", result.TargetBody);
    }

    [Theory]
    [InlineData("SelectedUnsafeAutoPropertySamples", "Pointer", false)]
    [InlineData("SelectedUnsafeAutoPropertySamples", "Pointer", true)]
    [InlineData("SelectedUnsafeAutoPropertySamples", "SharedPointer", false)]
    [InlineData("SelectedUnsafeAutoPropertySamples", "FunctionPointer", false)]
    [InlineData("SelectedUnsafeAutoPropertySamples", "FunctionPointer", true)]
    [InlineData("SelectedUnsafeAutoPropertySamples", "SharedFunctionPointer", false)]
    [InlineData("SelectedLayoutAutoPropertySamples", "Count", false)]
    [InlineData("SelectedLayoutAutoPropertySamples", "Count", true)]
    public async Task NativeAutomaticGetterPreservesBodyWhenSelectedDeclarationDeclines(
        string typeName, string propertyName, bool full)
    {
        string path = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            path,
            [new ReturnToSender.RequestedTarget(
                $"ILInspector.Decompiler.Fixtures.{typeName}", $"get_{propertyName}", 0)],
            RoundTripScope.Cluster,
            full ? RoundTripBodyPolicy.Full : RoundTripBodyPolicy.Selected,
            applyCompileBackFloor: false));

        AssertNativeGetterStorage(path, typeName, propertyName, result);
        Assert.Contains($"{propertyName} {{ get; }}", result.Source);
    }

    [Theory]
    [InlineData("ConstructorGetterList`1", "Items")]
    [InlineData("ConstructorGetterCounter", "Value")]
    [InlineData("ConstructorGetterComputed", "Value")]
    [InlineData("ConstructorGetterParameterName", "Value")]
    [InlineData("ConstructorGetterKeywordParameter", "Value")]
    [InlineData("ConstructorGetterOptional", "Value")]
    [InlineData("ConstructorGetterReturnAttribute", "Value")]
    [InlineData("ConstructorGetterPropertyAttribute", "Value")]
    public async Task NativeGetterRetainsInitialization(string typeName, string propertyName)
    {
        string path = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            path,
            [new ReturnToSender.RequestedTarget(
                $"ILInspector.Decompiler.Fixtures.{typeName}", $"get_{propertyName}", 0)],
            RoundTripScope.Cluster, RoundTripBodyPolicy.Selected, applyCompileBackFloor: false));

        AssertNativeGetterStorage(path, typeName, propertyName, result);
        AssertNativeConstructorStorage(path, typeName, propertyName, result);
        Assert.Contains($"struct {typeName.Split('`')[0]}", result.Source);
        Assert.DoesNotContain($"public {typeName.Split('`')[0]}(", result.Source);
        Assert.Contains("} = ", result.Source);
        if (typeName == "ConstructorGetterOptional")
            Assert.Contains("(int value = 7)", result.Source);
    }

    [Fact]
    public async Task PublishedDocoptNativeGetterRetainsInitialization()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "FieldGetter", "DocoptNet.dll");
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            path,
            [new ReturnToSender.RequestedTarget("DocoptNet.Internals.ReadOnlyList`1", "get_List", 0)],
            RoundTripScope.Cluster, RoundTripBodyPolicy.Selected, applyCompileBackFloor: false));

        AssertNativeGetterStorage(path, "ReadOnlyList`1", "List", result);
        AssertNativeConstructorStorage(path, "ReadOnlyList`1", "List", result);
        Assert.Contains("struct ReadOnlyList<T>(IList<T> list)", result.Source);
        Assert.Contains("} = list;", result.Source);
        Assert.DoesNotContain("this.List = list;", result.Source);
        Assert.DoesNotContain("public ReadOnlyList(", result.Source);
    }

    [Theory]
    [InlineData("ConstructorGetterPrivate", "Value")]
    [InlineData("ConstructorGetterAttributed", "Value")]
    [InlineData("ConstructorGetterImplementation", "Value")]
    [InlineData("ConstructorGetterTypeName`1", "Items")]
    [InlineData("ConstructorGetterTypeParameter`1", "Value")]
    [InlineData("ConstructorGetterReturnAttributeCollision", "Value")]
    public async Task NativeGetterRetainsExplicitConstructorWhenInitializerFormDeclines(
        string typeName, string propertyName)
    {
        string path = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            path,
            [new ReturnToSender.RequestedTarget(
                $"ILInspector.Decompiler.Fixtures.{typeName}", $"get_{propertyName}", 0)],
            RoundTripScope.Cluster, RoundTripBodyPolicy.Selected, applyCompileBackFloor: false));

        AssertNativeGetterStorage(path, typeName, propertyName, result);
        AssertNativeConstructorStorage(path, typeName, propertyName, result);
        Assert.Contains($"public {typeName.Split('`')[0]}(", result.Source);
        Assert.DoesNotContain("} = ", result.Source);
    }

    [Theory]
    [InlineData("ConstructorGetterCalculated")]
    [InlineData("ConstructorGetterConditional")]
    [InlineData("ConstructorGetterOverloads")]
    [InlineData("ConstructorGetterOtherStorage")]
    [InlineData("ConstructorGetterUnusedParameter")]
    public async Task NativeGetterDeclinesUnprovenInitialization(string typeName)
    {
        string path = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            path,
            [new ReturnToSender.RequestedTarget(
                $"ILInspector.Decompiler.Fixtures.{typeName}", "get_Value", 0)],
            RoundTripScope.Cluster, RoundTripBodyPolicy.Selected, applyCompileBackFloor: false));
        AssertNativeGetterStorage(path, typeName, "Value", result);
        using var rebuilt = new PEReader(new MemoryStream(result.DonorPe!));
        var reader = rebuilt.GetMetadataReader();
        var type = FindGetterType(reader, typeName);
        Assert.DoesNotContain(type.GetMethods(),
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == ".ctor");
    }

    [Theory]
    [InlineData("ConstructorGetterExplicitAutomatic")]
    [InlineData("ConstructorGetterExplicitComputed")]
    public async Task NativeExplicitGetterDeclinesInitialization(string typeName)
    {
        string path = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
        var result = Assert.Single(await ReturnToSender.CompileBackTargets(
            path,
            [new ReturnToSender.RequestedTarget(
                $"ILInspector.Decompiler.Fixtures.{typeName}",
                "ILInspector.Decompiler.Fixtures.IConstructorGetterValue.get_Value", 0)],
            RoundTripScope.Cluster, RoundTripBodyPolicy.Selected, applyCompileBackFloor: false));

        AssertNativeGetterStorage(path, typeName,
            "ILInspector.Decompiler.Fixtures.IConstructorGetterValue.Value", result);
        using var rebuilt = new PEReader(new MemoryStream(result.DonorPe!));
        var reader = rebuilt.GetMetadataReader();
        var type = FindGetterType(reader, typeName);
        Assert.DoesNotContain(type.GetMethods(),
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == ".ctor");
    }

    static void AssertNativeConstructorStorage(
        string path, string typeName, string propertyName, ReturnToSender.Result result)
    {
        using var original = new PEReader(File.OpenRead(path));
        using var rebuilt = new PEReader(new MemoryStream(result.DonorPe!));
        var originalReader = original.GetMetadataReader();
        var rebuiltReader = rebuilt.GetMetadataReader();
        var originalType = FindGetterType(originalReader, typeName);
        var rebuiltType = FindGetterType(rebuiltReader, typeName);
        var before = ReadGetterInstructions(original, originalType, propertyName, ".ctor");
        var after = ReadGetterInstructions(rebuilt, rebuiltType, propertyName, ".ctor");
        Assert.Equal(before.Select(instruction => instruction.OpCode),
            after.Select(instruction => instruction.OpCode));
        Assert.Equal(before.Select(instruction => GetterOperand(originalReader, instruction)),
            after.Select(instruction => GetterOperand(rebuiltReader, instruction)));

        var store = Assert.Single(after, instruction => instruction.OpCode == ILOpCode.Stfld);
        var reads = ReadGetterInstructions(rebuilt, rebuiltType, propertyName)
            .Where(instruction => instruction.OpCode == ILOpCode.Ldfld);
        Assert.NotEmpty(reads);
        Assert.All(reads, load =>
            Assert.Equal(GetterOperand(rebuiltReader, store), GetterOperand(rebuiltReader, load)));
    }

    static void AssertNativeGetterStorage(
        string path, string typeName, string propertyName, ReturnToSender.Result result)
    {
        Assert.False(result.UsedCompileBackFloor);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
        Assert.Equal(result.OriginalOpcodes, result.RecompiledOpcodes);
        Assert.NotNull(result.DonorPe);
        using var original = new PEReader(File.OpenRead(path));
        using var rebuilt = new PEReader(new MemoryStream(result.DonorPe));
        var originalReader = original.GetMetadataReader();
        var rebuiltReader = rebuilt.GetMetadataReader();
        var originalType = FindGetterType(originalReader, typeName);
        var rebuiltType = FindGetterType(rebuiltReader, typeName);
        var originalField = FindGetterField(originalReader, originalType, propertyName);
        var rebuiltField = FindGetterField(rebuiltReader, rebuiltType, propertyName);
        Assert.Equal(originalField.Attributes, rebuiltField.Attributes);
        var originalInstructions = ReadGetterInstructions(original, originalType, propertyName);
        var rebuiltInstructions = ReadGetterInstructions(rebuilt, rebuiltType, propertyName);
        Assert.Equal(
            originalInstructions.Select(instruction => instruction.OpCode),
            rebuiltInstructions.Select(instruction => instruction.OpCode));
        Assert.Equal(
            originalInstructions.Select(instruction => GetterOperand(originalReader, instruction)),
            rebuiltInstructions.Select(instruction => GetterOperand(rebuiltReader, instruction)));
        foreach (var (before, after) in originalInstructions.Zip(rebuiltInstructions))
            Assert.Equal<int>(before.BranchTargets, after.BranchTargets);
    }

    static TypeDefinition FindGetterType(MetadataReader reader, string name)
        => reader.GetTypeDefinition(Assert.Single(reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == name));

    static FieldDefinition FindGetterField(MetadataReader reader, TypeDefinition type, string propertyName)
        => reader.GetFieldDefinition(Assert.Single(type.GetFields(),
            handle => reader.GetString(reader.GetFieldDefinition(handle).Name) == $"<{propertyName}>k__BackingField"));

    static IReadOnlyList<DecodedInstruction> ReadGetterInstructions(
        PEReader pe, TypeDefinition type, string propertyName, string? methodName = null)
    {
        var reader = pe.GetMetadataReader();
        var methodHandle = methodName is null
            ? reader.GetPropertyDefinition(Assert.Single(type.GetProperties(),
                handle => reader.GetString(reader.GetPropertyDefinition(handle).Name) == propertyName))
                .GetAccessors().Getter
            : Assert.Single(type.GetMethods(),
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName);
        var method = reader.GetMethodDefinition(methodHandle);
        var decoded = MethodInstructions.Decode(pe.GetMethodBody(method.RelativeVirtualAddress));
        Assert.True(decoded.IsComplete);
        return decoded.Instructions;
    }

    static object GetterOperand(MetadataReader reader, DecodedInstruction instruction)
    {
        object operand = instruction.Operand switch
        {
            OperandKind.InlineField => CanonicalIL.ResolveField(reader, (int)instruction.OperandValue),
            OperandKind.InlineMethod => CanonicalIL.ResolveMethod(reader, (int)instruction.OperandValue),
            OperandKind.InlineType => CanonicalIL.ResolveType(reader, (int)instruction.OperandValue),
            OperandKind.InlineString => reader.GetUserString(
                System.Reflection.Metadata.Ecma335.MetadataTokens.UserStringHandle((int)instruction.OperandValue & 0x00ffffff)),
            _ => instruction.OperandValue,
        };
        // The pinned framework facades are rebuilt against the current core-library reference.
        return operand is string canonical
            ? canonical.Replace("['netstandard']", "[System.Private.CoreLib]", StringComparison.Ordinal)
                .Replace("[System.Runtime]", "[System.Private.CoreLib]", StringComparison.Ordinal)
            : operand;
    }
}
