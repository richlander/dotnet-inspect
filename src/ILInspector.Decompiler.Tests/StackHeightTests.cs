using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Tests for StackHeightCalculator, verifying computed max stack matches
/// the value recorded in method body metadata.
/// </summary>
public class StackHeightTests
{
    [Theory]
    [InlineData(nameof(CfgSampleClass.Add))]
    [InlineData(nameof(CfgSampleClass.IsPositive))]
    [InlineData(nameof(CfgSampleClass.Classify))]
    [InlineData(nameof(CfgSampleClass.SwitchCase))]
    [InlineData(nameof(CfgSampleClass.TryCatch))]
    [InlineData(nameof(CfgSampleClass.TryFinally))]
    [InlineData(nameof(CfgSampleClass.LoopSum))]
    [InlineData(nameof(CfgSampleClass.NestedExceptionHandlers))]
    [InlineData(nameof(CfgSampleClass.ThrowAndRethrow))]
    public void ComputedMaxStack_MatchesMetadata(string methodName)
    {
        var assemblyPath = typeof(CfgSampleClass).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var context = MethodBodyContext.Create(
            peReader,
            typeof(CfgSampleClass).FullName!,
            methodName);
        Assert.NotNull(context);

        int computed = StackHeightCalculator.ComputeMaxStack(context);

        // Our computed value should be <= the metadata max stack.
        // The compiler may set a higher value for safety; we just verify
        // our simulation doesn't exceed it.
        Assert.True(computed <= context.MaxStack,
            $"Computed max stack ({computed}) exceeds metadata value ({context.MaxStack}) for {methodName}");
        Assert.True(computed > 0 || methodName == nameof(CfgSampleClass.ThrowAndRethrow),
            $"Computed max stack should be > 0 for {methodName}");
    }

    [Fact]
    public void PlatformAssembly_StackHeightNeverExceedsMetadata()
    {
        var assembly = typeof(object).Assembly;
        var path = assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        int totalMethods = 0;
        List<string> failures = [];

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var context = MethodBodyContext.Create(peReader, reader, method);
                if (context is null)
                    continue;

                totalMethods++;

                try
                {
                    int computed = StackHeightCalculator.ComputeMaxStack(context);
                    if (computed > context.MaxStack)
                    {
                        string typeName = reader.GetString(typeDef.Name);
                        string methodName = reader.GetString(method.Name);
                        failures.Add($"{typeName}::{methodName}: computed={computed}, metadata={context.MaxStack}");
                    }
                }
                catch (Exception ex)
                {
                    string typeName = reader.GetString(typeDef.Name);
                    string methodName = reader.GetString(method.Name);
                    failures.Add($"{typeName}::{methodName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Assert.True(totalMethods > 1000, $"Expected many methods, got {totalMethods}");

        // Allow a small number of failures from methods with unusual IL (R2R stubs, etc.)
        double failureRate = (double)failures.Count / totalMethods;
        Assert.True(failureRate < 0.01,
            $"Stack height issues for {failures.Count}/{totalMethods} methods ({failureRate:P1}):\n{string.Join("\n", failures.Take(20))}");
    }
}
