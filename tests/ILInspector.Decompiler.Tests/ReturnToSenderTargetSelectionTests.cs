using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
    public void AdoptsOrdinaryAndExplicitAccessorDeclarations()
    {
        string assemblyPath =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                using System;

                public interface IValueContract
                {
                    int Value { get; }
                    int this[int index] { get; set; }
                    event Action Changed;
                }

                public sealed class ExplicitAccessorFixture : IValueContract
                {
                    public int OrdinaryValue => 7;

                    int IValueContract.Value => 8;

                    int IValueContract.this[int index]
                    {
                        get => index;
                        set { }
                    }

                    event Action IValueContract.Changed
                    {
                        add { }
                        remove { }
                    }

                    public static int Good() => 1;
                }
                """, assemblyName: "ExactAccessor");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [assemblyPath],
                    cap: int.MaxValue);

            Assert.Contains(
                selection.Targets,
                target => target.Method == "Good");
            FidelityCheck.CompileBackTarget[] accessors =
            [
                .. selection.Targets.Where(
                    target => target.Method.Contains(
                            "get_",
                            StringComparison.Ordinal)
                        || target.Method.Contains(
                            "set_",
                            StringComparison.Ordinal)
                        || target.Method.Contains(
                            "add_",
                            StringComparison.Ordinal)
                        || target.Method.Contains(
                            "remove_",
                            StringComparison.Ordinal)),
            ];
            Assert.Equal(6, accessors.Length);
            Assert.All(
                accessors,
                target =>
                {
                    var exact = Assert.IsType<
                        FidelityCheck.ReturnToSenderDeclarationSelection
                            .ExactAccessor>(target.Declaration);
                    Assert.Equal(
                        target.Address,
                        exact.Request.Coordinate.Method);
                    CSharpAcceptedAccessorBinding selected =
                        Assert.Single(
                            exact.Request.Accessors,
                            accessor => accessor.Occurrence.Method.Method
                                == target.Address);
                    Assert.Equal(
                        CSharpAccessorBodyPolicy.SelectedBody,
                        selected.BodyPolicy);
                    Assert.All(
                        exact.Request.Accessors.Where(
                            accessor => accessor != selected),
                        accessor => Assert.Equal(
                            CSharpAccessorBodyPolicy.SiblingStub,
                            accessor.BodyPolicy));
                });
            Assert.Equal(
                2,
                accessors.Count(target =>
                    ((FidelityCheck.ReturnToSenderDeclarationSelection
                        .ExactAccessor)target.Declaration!).Request.Kind
                    == CSharpAccessorDeclarationKind.Property));
            Assert.Equal(
                2,
                accessors.Count(target =>
                    ((FidelityCheck.ReturnToSenderDeclarationSelection
                        .ExactAccessor)target.Declaration!).Request.Kind
                    == CSharpAccessorDeclarationKind.Indexer));
            Assert.Equal(
                2,
                accessors.Count(target =>
                    ((FidelityCheck.ReturnToSenderDeclarationSelection
                        .ExactAccessor)target.Declaration!).Request.Kind
                    == CSharpAccessorDeclarationKind.Event));
            Assert.Null(
                ((FidelityCheck.ReturnToSenderDeclarationSelection
                    .ExactAccessor)Assert.Single(
                        accessors,
                        target => target.Method == "get_OrdinaryValue")
                    .Declaration!).Request.ExplicitInterface);
            Assert.All(
                accessors.Where(
                    target => target.Method != "get_OrdinaryValue"),
                target => Assert.NotNull(
                    ((FidelityCheck.ReturnToSenderDeclarationSelection
                        .ExactAccessor)target.Declaration!)
                    .Request.ExplicitInterface));
            Assert.DoesNotContain(
                selection.Exclusions,
                exclusion => exclusion.Producer
                    == FidelityCheck.ReturnToSenderDeclarationProducer
                        .ExactAccessorDeclaration);
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
    public void PreservesTypedEventRaiserRefusal(string raiserName)
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
                    .ExactAccessorDeclarationUnrepresentable,
                raiser.Reason);
            Assert.Equal(
                FidelityCheck.ReturnToSenderDeclarationProducer
                    .ExactAccessorDeclaration,
                raiser.Producer);
            var refusal = Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult
                    .Unrepresentable>(raiser.AccessorOutcome);
            Assert.Equal(
                CSharpAccessorDeclarationRefusalReason
                    .UnsupportedSemanticOccurrence,
                refusal.Reason);
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

    [Fact]
    public void AppliesCapAfterExactAccessorEligibility()
    {
        string refusedAssembly =
            CreateGeneratedEventRaiserFixture(
                "raise_Changed",
                isStaticType: true,
                includeGoodMethod: false);
        string eligibleAssembly =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public static class EligibleAccessorFixture
                {
                    public static int Value => 2;
                }
                """, assemblyName: "EligibleExactAccessor");
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [refusedAssembly, eligibleAssembly],
                    cap: 1);

            FidelityCheck.CompileBackTarget selected =
                Assert.Single(selection.Targets);
            Assert.Equal(eligibleAssembly, selected.AssemblyPath);
            Assert.Equal("get_Value", selected.Method);
            Assert.IsType<
                FidelityCheck.ReturnToSenderDeclarationSelection
                    .ExactAccessor>(selected.Declaration);
            Assert.Equal(1, selection.EligibleCount);

            FidelityCheck.ReturnToSenderTargetExclusion[] exclusions =
            [
                .. selection.Exclusions.Where(
                    exclusion => exclusion.AssemblyPath
                        == refusedAssembly),
            ];
            Assert.Equal(3, exclusions.Length);
            Assert.All(
                exclusions,
                exclusion =>
                {
                    Assert.Equal(
                        FidelityCheck.ReturnToSenderTargetExclusionReason
                            .ExactAccessorDeclarationUnrepresentable,
                        exclusion.Reason);
                    Assert.Equal(
                        FidelityCheck.ReturnToSenderDeclarationProducer
                            .ExactAccessorDeclaration,
                        exclusion.Producer);
                    var refusal = Assert.IsType<
                        CSharpAccessorDeclarationRepresentabilityResult
                            .Unrepresentable>(
                                exclusion.AccessorOutcome);
                    Assert.Equal(
                        CSharpAccessorDeclarationRefusalReason
                            .UnsupportedSemanticOccurrence,
                        refusal.Reason);
                });
            FidelityCheck.ReturnToSenderTargetExclusion raiser =
                Assert.Single(
                    exclusions,
                    exclusion => exclusion.Method == "raise_Changed");
            Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult
                    .Unrepresentable>(raiser.AccessorOutcome);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                refusedAssembly);
            FidelityCheckGeneratedFilterTests.DeleteFixture(
                eligibleAssembly);
        }
    }

    [Fact]
    public void PreservesTypedIncompleteAccessorUnavailability()
    {
        string assemblyPath =
            CreateGeneratedEventRaiserFixture(
                "unused",
                isStaticType: true,
                includeRemoveAccessor: false,
                includeRaiser: false,
                includeGoodMethod: false);
        try
        {
            FidelityCheck.ReturnToSenderTargetSelection selection =
                FidelityCheck.SelectReturnToSenderTargetPlan(
                    [assemblyPath],
                    cap: int.MaxValue);

            Assert.Empty(selection.Targets);
            FidelityCheck.ReturnToSenderTargetExclusion exclusion =
                Assert.Single(selection.Exclusions);
            Assert.Equal("add_Changed", exclusion.Method);
            Assert.Equal(
                FidelityCheck.ReturnToSenderTargetExclusionReason
                    .ExactAccessorDeclarationUnavailable,
                exclusion.Reason);
            Assert.Equal(
                FidelityCheck.ReturnToSenderDeclarationProducer
                    .ExactAccessorDeclaration,
                exclusion.Producer);
            var unavailable = Assert.IsType<
                CSharpAccessorDeclarationRepresentabilityResult.Unavailable>(
                    exclusion.AccessorOutcome);
            Assert.Equal(
                CSharpAccessorDeclarationUnavailableReason
                    .AccessorDeclarationRejected,
                unavailable.Reason);
        }
        finally
        {
            FidelityCheckGeneratedFilterTests.DeleteFixture(assemblyPath);
        }
    }

    [Fact]
    public void LegacyMemberIndexExcludesCompilerGeneratedHelpers()
    {
        string assemblyPath =
            FidelityCheckGeneratedFilterTests.CompileFixture("""
                public static class GeneratedHelperFixture
                {
                    public static int Good(int value)
                    {
                        return Local(value);

                        static int Local(int input) => input + 1;
                    }
                }
                """);
        try
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            MetadataReader reader = pe.GetMetadataReader();
            TypeDefinition type = reader.GetTypeDefinition(Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(
                    reader.GetTypeDefinition(handle).Name)
                    == "GeneratedHelperFixture"));
            MethodDefinitionHandle generated = Assert.Single(
                type.GetMethods(),
                handle => reader.GetString(
                        reader.GetMethodDefinition(handle).Name)
                    .StartsWith("<Good>g__Local", StringComparison.Ordinal));
            using var source = MetadataSource.Open(assemblyPath);

            Assert.Null(FidelityCheck.TryRenderTargetMember(
                pe,
                source,
                generated,
                targeted: false,
                isPrimaryConstructor: false));
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
                + $"{exclusion.ExactOutcome?.GetType().Name}:"
                + $"{exclusion.AccessorOutcome?.GetType().Name}")
            .ToArray();

    static string CreateGeneratedEventRaiserFixture(
        string raiserName,
        bool isStaticType = false,
        bool includeRemoveAccessor = true,
        bool includeRaiser = true,
        bool includeGoodMethod = true)
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
            TypeAttributes.Public
                | TypeAttributes.Class
                | (isStaticType
                    ? TypeAttributes.Abstract | TypeAttributes.Sealed
                    : 0));

        if (includeGoodMethod)
        {
            MethodBuilder good = type.DefineMethod(
                "Good",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                Type.EmptyTypes);
            ILGenerator goodBody = good.GetILGenerator();
            goodBody.Emit(OpCodes.Ldc_I4_1);
            goodBody.Emit(OpCodes.Ret);
        }

        EventBuilder changed = type.DefineEvent(
            "Changed",
            EventAttributes.None,
            typeof(Action));
        MethodBuilder add = DefineEventMethod(
            type,
            "add_Changed",
            [typeof(Action)],
            isStaticType);
        changed.SetAddOnMethod(add);
        if (includeRemoveAccessor)
        {
            MethodBuilder remove = DefineEventMethod(
                type,
                "remove_Changed",
                [typeof(Action)],
                isStaticType);
            changed.SetRemoveOnMethod(remove);
        }
        if (includeRaiser)
        {
            MethodBuilder raise = DefineEventMethod(
                type,
                raiserName,
                Type.EmptyTypes,
                isStaticType);
            raise.SetCustomAttribute(
                new CustomAttributeBuilder(
                    typeof(CompilerGeneratedAttribute).GetConstructor(
                        Type.EmptyTypes)!,
                    []));
            changed.SetRaiseMethod(raise);
        }

        type.CreateType();
        assembly.Save(path);
        return path;
    }

    static MethodBuilder DefineEventMethod(
        TypeBuilder type,
        string name,
        Type[] parameterTypes,
        bool isStatic = false)
    {
        MethodBuilder method = type.DefineMethod(
            name,
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig
                | (isStatic ? MethodAttributes.Static : 0),
            typeof(void),
            parameterTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return method;
    }
}
