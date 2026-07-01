namespace Aspire.Hosting.ApplicationModel
{
    public interface IResourceBuilder<T>
    {
        void Add(int value);
    }

    public sealed class TestResource;
}

namespace ILInspector.Analysis.Tests
{
    using Aspire.Hosting.ApplicationModel;
    using Microsoft.Extensions.Hosting;

    public sealed class NonCompositionBuilder
    {
        public void Add(int value)
        {
        }
    }

    public static class CompositionRootOptimizationFixtures
    {
        public static IResourceBuilder<T> WithLoopClosure<T>(
            this IResourceBuilder<T> builder,
            int[] values,
            int seed)
        {
            foreach (var value in values)
            {
                Func<int> projector = () => value + seed;
                builder.Add(projector());
            }

            return builder;
        }

        public static IHostApplicationBuilder ConfigureHost(
            IHostApplicationBuilder builder,
            int[] values,
            int seed)
        {
            foreach (var value in values)
            {
                Func<int> projector = () => value + seed;
                _ = projector();
            }

            return builder;
        }

        internal static IHostApplicationBuilder InternalConfigureHost(
            IHostApplicationBuilder builder,
            int[] values,
            int seed)
        {
            foreach (var value in values)
            {
                Func<int> projector = () => value + seed;
                _ = projector();
            }

            return builder;
        }

        public static int HotLoopClosure(NonCompositionBuilder builder, int[] values, int seed)
        {
            var total = 0;
            foreach (var value in values)
            {
                Func<int> projector = () => value + seed;
                total += projector();
            }

            builder.Add(total);
            return total;
        }
    }
}
