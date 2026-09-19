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

    static IReadOnlyList<DecodedInstruction> ReadGetterInstructions(PEReader pe, TypeDefinition type, string propertyName)
    {
        var reader = pe.GetMetadataReader();
        var method = reader.GetMethodDefinition(Assert.Single(type.GetMethods(),
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == $"get_{propertyName}"));
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
        // The published netstandard witness is rebuilt against the current core-library reference.
        return operand is string canonical
            ? canonical.Replace("['netstandard']", "[System.Private.CoreLib]", StringComparison.Ordinal)
            : operand;
    }
}
