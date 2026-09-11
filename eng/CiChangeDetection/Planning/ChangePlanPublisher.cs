using System.Text;

namespace CiChangeDetection.Planning;

/// <summary>
/// Publishes one planning result. The plan appears on standard output only
/// after its scoped evidence has been written and its serialized bytes have
/// been validated by re-parsing them through the strict plan reader.
/// </summary>
internal static class ChangePlanPublisher
{
    /// <summary>
    /// Writes scoped evidence and then the single compact plan line.
    /// </summary>
    /// <param name="result">The planning result to publish.</param>
    /// <param name="evidenceDirectory">The explicit evidence directory.</param>
    /// <param name="standardOutput">The plan output writer.</param>
    /// <returns>The serialized plan bytes.</returns>
    internal static byte[] Publish(
        PlanningResult result,
        string evidenceDirectory,
        TextWriter standardOutput)
    {
        byte[] serialized = ChangePlanSerializer.Serialize(result.Plan);

        // Re-parse before publishing: a plan that the strict reader would
        // reject must never reach a consumer.
        ChangePlan reparsed = ChangePlanSerializer.Deserialize(serialized);
        if (!ChangePlanSerializer.Serialize(reparsed).AsSpan()
            .SequenceEqual(serialized))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "the serialized plan did not round-trip");
        }

        if (result.HasTlaScope)
        {
            WriteScope(
                evidenceDirectory,
                PlanScopeDescriptor.TlaArtifact,
                result.TlaScopeBytes);
        }

        standardOutput.Write(Encoding.UTF8.GetString(serialized));
        standardOutput.Write('\n');
        standardOutput.Flush();
        return serialized;
    }

    /// <summary>
    /// Gets the scope file path this invocation owns inside the evidence
    /// directory.
    /// </summary>
    /// <param name="evidenceDirectory">The explicit evidence directory.</param>
    /// <param name="artifact">The artifact name.</param>
    /// <returns>The scope file path.</returns>
    internal static string ScopePath(
        string evidenceDirectory,
        string artifact) =>
        Path.Combine(evidenceDirectory, artifact);

    /// <summary>
    /// Removes any scope file this invocation would have owned, so a refusal
    /// cannot leave a consumer a plausible but unnamed corpus.
    /// </summary>
    /// <param name="evidenceDirectory">The explicit evidence directory.</param>
    internal static void RemoveScopes(string evidenceDirectory)
    {
        try
        {
            File.Delete(ScopePath(
                evidenceDirectory,
                PlanScopeDescriptor.TlaArtifact));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static void PrepareEvidenceDirectory(string evidenceDirectory)
    {
        try
        {
            File.Delete(ScopePath(
                evidenceDirectory,
                PlanScopeDescriptor.TlaArtifact));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanPublication,
                "could not clear prior scope evidence");
        }
    }

    private static void WriteScope(
        string evidenceDirectory,
        string artifact,
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > ChangePlanner.MaximumScopeBytes)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.ScopeOverflow,
                "the scope file exceeded its byte ceiling");
        }

        string target = ScopePath(evidenceDirectory, artifact);
        string staged = $"{target}.staged";
        try
        {
            File.WriteAllBytes(staged, bytes);
            File.Move(staged, target, overwrite: true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(staged);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw new PlanRefusalException(
                PlanRefusalCategory.PlanPublication,
                "could not write the scope evidence file");
        }
    }
}
