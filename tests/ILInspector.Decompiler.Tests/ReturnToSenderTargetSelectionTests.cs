using ILInspector.CSharp;
using ILInspector.DecompilerHarness;

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ILInspector.Decompiler.Tests;

[Collection(ConsoleMutatorCollection.Name)]
public class ReturnToSenderTargetSelectionTests
{
    [Fact]
    public void AppliesCapAfterExactDeclarationEligibility()
    {
        string unavailableAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public interface IDeferredContract
                {
                    int Transform(int value);
                }

                public readonly struct DeferredExplicitFixture : IDeferredContract
                {
                    int IDeferredContract.Transform(int value) => value;
                }
                """, assemblyName: "UnavailableExactDeclaration");
        string eligibleAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public static class EligibleOrdinaryFixture
                {
                    public static int Transform(int value) => value + 1;
                }
                """, assemblyName: "EligibleOrdinaryDeclaration");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [unavailableAssembly, eligibleAssembly],
                    cap: 1);

            FidelityCheck.CompileBackTarget selected =
                Assert.Single(selection.Targets);
            Assert.Equal(eligibleAssembly, selected.AssemblyPath);
            Assert.IsType<
                FidelityCheck.ReturnToSenderDeclarationSelection
                    .OrdinaryMethod>(selected.Declaration);
            Assert.Equal(1, selection.EligibleCount);

            FidelityCheck.ReturnToSenderTargetExclusion exclusion =
                Assert.Single(
                    selection.Exclusions,
                    exclusion => exclusion.Method.EndsWith(
                        ".Transform",
                        StringComparison.Ordinal));
            Assert.Equal(
                FidelityCheck.ReturnToSenderTargetExclusionReason
                    .ExactDeclarationUnavailable,
                exclusion.Reason);
            Assert.Equal(
                FidelityCheck.ReturnToSenderDeclarationProducer
                    .ExactMethodDeclaration,
                exclusion.Producer);
            var unavailable = Assert.IsType<
                CSharpDeclarationRepresentabilityResult.Unavailable>(
                    exclusion.ExactOutcome);
            Assert.Equal(
                CSharpDeclarationUnavailableReason.OutsideInitialBoundary,
                unavailable.Reason);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                unavailableAssembly);
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                eligibleAssembly);
        }
    }

    [Fact]
    public void DefersOrdinaryAndExplicitAccessors()
    {
        string assemblyPath =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                using System;

                public interface IValueContract
                {
                    int Value { get; }
                    event Action Changed;
                }

                public sealed class ExplicitAccessorFixture : IValueContract
                {
                    public int OrdinaryValue => 7;

                    int IValueContract.Value { get; }

                    public event Action Changed = delegate { };

                    public static int Good() => 1;
                }
                """, assemblyName: "DeferredAccessor");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [assemblyPath],
                    cap: int.MaxValue);

            Assert.Contains(
                selection.Targets,
                target => target.Method == "Good");
            Assert.DoesNotContain(
                selection.Targets,
                target => target.Method.Contains(
                        "get_",
                        StringComparison.Ordinal)
                    || target.Method.Contains(
                        "add_",
                        StringComparison.Ordinal)
                    || target.Method.Contains(
                        "remove_",
                        StringComparison.Ordinal));
            FidelityCheck.ReturnToSenderTargetExclusion[] accessors =
            [
                .. selection.Exclusions.Where(
                    exclusion => exclusion.Method.Contains(
                            "get_",
                            StringComparison.Ordinal)
                        || exclusion.Method.Contains(
                            "add_",
                            StringComparison.Ordinal)
                        || exclusion.Method.Contains(
                            "remove_",
                            StringComparison.Ordinal)),
            ];
            Assert.Equal(4, accessors.Length);
            Assert.All(
                accessors,
                exclusion =>
                {
                    Assert.Equal(
                        FidelityCheck.ReturnToSenderTargetExclusionReason
                            .AccessorDeferred,
                        exclusion.Reason);
                    Assert.Null(exclusion.Producer);
                    Assert.Null(exclusion.ExactOutcome);
                });
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void OrdersExclusionsDeterministically()
    {
        string firstAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public interface IExplicitOrder
                {
                    int Alpha(int value);
                    int Beta(int value);
                }

                public readonly struct ExplicitOrderFixture : IExplicitOrder
                {
                    int IExplicitOrder.Alpha(int value) => value;
                    int IExplicitOrder.Beta(int value) => value;
                }
                """, assemblyName: "ExplicitOrder");
        string reorderedAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public interface IExplicitOrder
                {
                    int Alpha(int value);
                    int Beta(int value);
                }

                public readonly struct ExplicitOrderFixture : IExplicitOrder
                {
                    int IExplicitOrder.Beta(int value) => value;
                    int IExplicitOrder.Alpha(int value) => value;
                }
                """, assemblyName: "ExplicitOrder");
        try
        {
            string[] first = ExclusionSnapshot(
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [firstAssembly],
                    cap: int.MaxValue));
            string[] reordered = ExclusionSnapshot(
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [reorderedAssembly],
                    cap: int.MaxValue));

            Assert.Equal(first, reordered);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(firstAssembly);
            FidelityCheckGeneratedFilterTests.DeleteFixture(reorderedAssembly);
        }
    }

    [Theory]
    [InlineData("raise_Changed")]
    [InlineData("<raise_Changed>")]
    public void DefersCompilerGeneratedEventRaiser(string raiserName)
    {
        string assemblyPath = CreateGeneratedEventRaiserFixture(raiserName);
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [assemblyPath],
                    cap: int.MaxValue);

            FidelityCheck.ReturnToSenderTargetExclusion raiser =
                Assert.Single(
                    selection.Exclusions,
                    exclusion => exclusion.Method == raiserName);
            Assert.Equal(
                FidelityCheck.ReturnToSenderTargetExclusionReason
                    .AccessorDeferred,
                raiser.Reason);
            Assert.Contains(
                selection.Targets,
                target => target.Method == "Good");
            Assert.Equal(5, selection.DeclarationCandidateCount);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(assemblyPath);
        }
    }

    static string[] ExclusionSnapshot(
        FidelityCheck.ReturnToSenderTargetSelection selection)
        => selection.Exclusions
            .Select(exclusion =>
                $"{exclusion.Type}::{exclusion.Method}{exclusion.Signature}:"
                + $"{exclusion.Reason}:{exclusion.Producer}:"
                + $"{exclusion.ExactOutcome?.GetType().Name}")
            .ToArray();

    static string CreateGeneratedEventRaiserFixture(string raiserName)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"rts-generated-raiser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "GeneratedEventRaiser.dll");

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("GeneratedEventRaiser"),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule("GeneratedEventRaiser");
        TypeBuilder type = module.DefineType(
            "GeneratedEventRaiserFixture",
            TypeAttributes.Public | TypeAttributes.Class);

        MethodBuilder good = type.DefineMethod(
            "Good",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        ILGenerator goodBody = good.GetILGenerator();
        goodBody.Emit(OpCodes.Ldc_I4_1);
        goodBody.Emit(OpCodes.Ret);

        EventBuilder changed = type.DefineEvent(
            "Changed",
            EventAttributes.None,
            typeof(Action));
        MethodBuilder add = DefineEventMethod(
            type,
            "add_Changed",
            [typeof(Action)]);
        MethodBuilder remove = DefineEventMethod(
            type,
            "remove_Changed",
            [typeof(Action)]);
        MethodBuilder raise = DefineEventMethod(
            type,
            raiserName,
            Type.EmptyTypes);
        raise.SetCustomAttribute(
            new CustomAttributeBuilder(
                typeof(CompilerGeneratedAttribute).GetConstructor(
                    Type.EmptyTypes)!,
                []));
        changed.SetAddOnMethod(add);
        changed.SetRemoveOnMethod(remove);
        changed.SetRaiseMethod(raise);

        type.CreateType();
        assembly.Save(path);
        return path;
    }

    static MethodBuilder DefineEventMethod(
        TypeBuilder type,
        string name,
        Type[] parameterTypes)
    {
        MethodBuilder method = type.DefineMethod(
            name,
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(void),
            parameterTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return method;
    }
}
