// Adversarial cover for AwaitRecoveryPass. Each negative fixture is a method
// call that resembles the runtime-async `AsyncHelpers.Await` lowering but differs
// by exactly one of the matcher's discriminators (namespace, declaring type name,
// declaring assembly, instance-ness). None may be raised to `await`; together
// they pin the matcher so a future loosening that drops a discriminator fails
// loudly. Pair these with the positive ordering fixtures in CfgSampleClass
// (AwaitThree / AwaitInArguments / AwaitAcrossVoidCall) which prove multi-await
// order is preserved across every statement-emitting spill site.

// The look-alike below deliberately redeclares System.Runtime.CompilerServices
// .AsyncHelpers in this assembly to isolate the assembly discriminator; the
// local type shadows the corelib one (CS0436) on purpose.
#pragma warning disable CS0436

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests
{
    public class AwaitAdversarialTests
    {
        static IrFunction Raised(string methodName, Type type)
        {
            using var source = MetadataSource.Open(type.Assembly.Location);
            var function = IrImporter.Import(source, type.FullName!, methodName);
            Assert.NotNull(function);
            IrPasses.Run(function!);
            function.CheckInvariant();
            return function!;
        }

        static void AssertNoAwaitButCallSurvives(IrFunction function)
        {
            Assert.Empty(function.Descendants.OfType<AwaitExpression>());
            // The look-alike call must remain a call (named `Await`) in the
            // output — proving the shape was recognized as a call site, just not
            // as an await.
            var output = CSharpPrinter.Print(function).Output;
            Assert.NotNull(output);
            Assert.Contains("Await", output);        // the method call, capital A
            Assert.DoesNotContain("await ", output);  // never the await keyword
        }

        // Discriminator: declaring assembly. Same namespace, same type name, same
        // arity, same static-ness as the real helper — only the assembly differs.
        [Fact]
        public void SameNamespaceTypeAndArity_DifferentAssembly_IsNotRaised()
            => AssertNoAwaitButCallSurvives(Raised(nameof(AwaitLookAlikes.ViaShadowedCorelibType), typeof(AwaitLookAlikes)));

        // Discriminator: declaring type name (right namespace, wrong type).
        [Fact]
        public void RightNamespaceWrongTypeName_IsNotRaised()
            => AssertNoAwaitButCallSurvives(Raised(nameof(AwaitLookAlikes.ViaWrongTypeName), typeof(AwaitLookAlikes)));

        // Discriminator: declaring namespace (right type name, wrong namespace).
        [Fact]
        public void RightTypeNameWrongNamespace_IsNotRaised()
            => AssertNoAwaitButCallSurvives(Raised(nameof(AwaitLookAlikes.ViaWrongNamespace), typeof(AwaitLookAlikes)));

        // Discriminator: instance-ness. The real helper is static; an instance
        // `Await` of the same name/arity must not raise.
        [Fact]
        public void InstanceAwaitMethod_IsNotRaised()
            => AssertNoAwaitButCallSurvives(Raised(nameof(AwaitLookAlikes.ViaInstanceMethod), typeof(AwaitLookAlikes)));
    }

    internal static class AwaitLookAlikes
    {
        // Resolves to the shadowing AsyncHelpers below (this assembly), not corelib.
        public static int ViaShadowedCorelibType(int x)
            => System.Runtime.CompilerServices.AsyncHelpers.Await(x);

        public static int ViaWrongTypeName(int x)
            => System.Runtime.CompilerServices.AwaitHelper.Await(x);

        public static int ViaWrongNamespace(int x)
            => AdversarialAwait.AsyncHelpers.Await(x);

        public static int ViaInstanceMethod(int x)
            => new InstanceAwaiter().Await(x);
    }

    internal sealed class InstanceAwaiter
    {
        public int Await<T>(T value) => 0;
    }
}

namespace System.Runtime.CompilerServices
{
    // Shadows the corelib AsyncHelpers to isolate the assembly discriminator.
    internal static class AsyncHelpers
    {
        public static T Await<T>(T value) => value;
    }

    // Right namespace, wrong type name.
    internal static class AwaitHelper
    {
        public static T Await<T>(T value) => value;
    }
}

namespace AdversarialAwait
{
    // Right type name, wrong namespace.
    internal static class AsyncHelpers
    {
        public static T Await<T>(T value) => value;
    }
}
