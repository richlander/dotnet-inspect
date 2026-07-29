using DotnetInspector.Options;

namespace DotnetInspector.Output;

/// <summary>
/// Applies a payload projection to an alternate lens: a mode that renders its own payload and
/// returns before the section pipeline runs.
/// </summary>
/// <remarks>
/// <para>
/// Most projection dispatch happens inside the section pipeline, so a mode that produces its own
/// payload and returns early — <c>--versions</c>, <c>--layout</c>, <c>--tfms</c>,
/// <c>--il-offsets</c>, <c>-D</c>/<c>--discover</c> — never reaches it. Each such mode accepted
/// the projection flags and then rendered its own unprojected payload, which
/// <see cref="ProjectionAudit"/> now reports as a bug. This is the dispatch those modes were
/// missing.
/// </para>
/// <para>
/// A lens payload is a flat list of rows, not a section with named columns and printable
/// documents. <c>--count</c> therefore counts the rows, and the shape and print projections are
/// refused rather than approximated: they address a column or a document the lens does not have.
/// The refusal is the answer, so it does not need to mark the projection honored — the audit only
/// inspects successful exits.
/// </para>
/// <para>
/// The lenses that render text rather than a table still yield one structured row per matched
/// file, so they are Vectors and count normally. A lens that rendered a single document would be
/// a competing answer to the question section selection already answers, so printing a document
/// is a projection over a declared section rather than a lens of its own.
/// </para>
/// </remarks>
public static class LensProjection
{
    /// <summary>
    /// Whether any payload projection was requested, and so whether a lens must compute its row
    /// count before rendering.
    /// </summary>
    public static bool IsRequested(IProjectionOptions? options) =>
        options is not null
        && (options.Count || options.Print || options.Value || options.Urls || options.Paths);

    /// <summary>
    /// Answers a projection request against a lens payload of <paramref name="rowCount"/> rows.
    /// </summary>
    /// <param name="options">The requesting command's options, or null when the caller has none.</param>
    /// <param name="lens">The lens flag, named as the user spelled it (e.g. <c>--versions</c>).</param>
    /// <param name="rowCount">The number of rows the lens is about to render.</param>
    /// <param name="exitCode">The exit code to return when this method returns true.</param>
    /// <returns>
    /// True when the request was answered and the caller must return <paramref name="exitCode"/>
    /// without rendering; false when no projection was requested and the caller should render
    /// normally.
    /// </returns>
    public static bool TryProject(
        IProjectionOptions? options,
        string lens,
        int rowCount,
        out int exitCode)
    {
        exitCode = 0;
        if (!IsRequested(options))
            return false;

        if (options!.Count)
        {
            CountOutput.WriteCount(rowCount, options.OutputPath);
            return true;
        }

        var flag = options.Print ? "--print"
            : options.Value ? "--value"
            : options.Urls ? "--urls"
            : "--paths";

        Console.Error.WriteLine(
            $"Error: {flag} is not available with {lens}, which renders its own payload rather " +
            "than a section. Use --count to count that payload.");
        exitCode = 1;
        return true;
    }
}
