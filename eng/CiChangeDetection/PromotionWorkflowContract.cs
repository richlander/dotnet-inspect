using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class PromotionWorkflowContract
{
    internal static void AssertMutations(string repository)
    {
        string promotionPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "promote-inspect-web.yml");
        string stagingPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "deploy-inspect-web.yml");
        string promotionWorkflow = File.ReadAllText(promotionPath);
        string stagingWorkflow = File.ReadAllText(stagingPath);
        ValidatePromotion(promotionWorkflow);
        ValidateStaging(stagingWorkflow);

        const string trustedCheckout =
            """
                steps:
                  - uses: actions/checkout@v6

                  - name: Setup .NET
            """;
        const string candidateCheckout =
            """
                steps:
                  - uses: actions/checkout@v6
                    with:
                      ref: ${{ needs.resolve.outputs.sha }}

                  - name: Setup .NET
            """;
        string mutatedPromotion = promotionWorkflow.Replace(
            trustedCheckout,
            candidateCheckout,
            StringComparison.Ordinal);
        if (mutatedPromotion == promotionWorkflow)
            throw new InvalidOperationException("Promotion checkout mutation did not apply.");
        ExpectFailure(
            () => ValidatePromotion(mutatedPromotion),
            "Promotion workflow contract accepted candidate-controlled production checkout.");

        const string stagingDownload =
            """
                steps:
                  - name: Download staged site artifact
            """;
        const string stagingCheckout =
            """
                steps:
                  - uses: actions/checkout@v6

                  - name: Download staged site artifact
            """;
        string mutatedStaging = stagingWorkflow.Replace(
            stagingDownload,
            stagingCheckout,
            StringComparison.Ordinal);
        if (mutatedStaging == stagingWorkflow)
            throw new InvalidOperationException("Staging checkout mutation did not apply.");
        ExpectFailure(
            () => ValidateStaging(mutatedStaging),
            "Staging workflow contract accepted candidate code in the deployment job.");
    }

    private static void ValidatePromotion(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one promotion workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "promotion workflow root");
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "promotion workflow");
        YamlMappingNode deploy = GetRequiredMapping(jobs, "deploy", "promotion jobs");
        RequireScalarValue(deploy, "needs", "resolve", "jobs.deploy");
        RequireScalarValue(deploy, "runs-on", "ubuntu-26.04", "jobs.deploy");

        YamlMappingNode environment =
            GetRequiredMapping(deploy, "environment", "jobs.deploy");
        RequireScalarValue(
            environment,
            "name",
            "inspect-web-production",
            "jobs.deploy.environment");

        YamlSequenceNode steps = GetRequiredSequence(deploy, "steps", "jobs.deploy");
        if (steps.Children.Count != 6)
        {
            throw new InvalidOperationException(
                "Production deployment must contain checkout, setup, revalidation, " +
                "artifact download, artifact verification, and deploy steps.");
        }

        YamlMappingNode checkout = RequireStep(steps, 0, null);
        RequireExactKeys(checkout, ["uses"], "jobs.deploy checkout");
        RequireScalarValue(
            checkout,
            "uses",
            "actions/checkout@v6",
            "jobs.deploy checkout");

        RequireStep(steps, 1, "Setup .NET");
        YamlMappingNode revalidate =
            RequireStep(steps, 2, "Revalidate staged site");
        RequireScalarValue(revalidate, "shell", "bash", "revalidation step");
        string revalidationCommand = GetRequiredScalar(
            revalidate,
            "run",
            "revalidation step");
        if (!revalidationCommand.Contains(
                "bash eng/validate-inspect-web-promotion.sh",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Production revalidation must execute the trusted promotion validator.");
        }

        YamlMappingNode download =
            RequireStep(steps, 3, "Download staged site artifact");
        RequireScalarValue(
            download,
            "uses",
            "actions/download-artifact@v8",
            "artifact download step");
        YamlMappingNode downloadWith =
            GetRequiredMapping(download, "with", "artifact download step");
        RequireScalarValue(
            downloadWith,
            "artifact-ids",
            "${{ needs.resolve.outputs.artifact_id }}",
            "artifact download step.with");
        RequireScalarValue(
            downloadWith,
            "digest-mismatch",
            "error",
            "artifact download step.with");

        YamlMappingNode verify =
            RequireStep(steps, 4, "Verify staged site artifact");
        RequireScalarValue(
            verify,
            "run",
            "test -f artifacts/inspect-web-publish/wwwroot/index.html",
            "artifact verification step");

        YamlMappingNode deployStep =
            RequireStep(steps, 5, "Deploy to production");
        RequireScalarValue(
            deployStep,
            "uses",
            "Azure/static-web-apps-deploy@4d27395796ac319302594769cfe812bd207490b1",
            "production deploy step");
    }

    private static void ValidateStaging(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one staging workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "staging workflow root");
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "staging workflow");
        YamlMappingNode build = GetRequiredMapping(jobs, "build", "staging jobs");
        RequireAbsent(build, "environment", "jobs.build");
        YamlMappingNode buildOutputs =
            GetRequiredMapping(build, "outputs", "jobs.build");
        RequireScalarValue(
            buildOutputs,
            "artifact_id",
            "${{ steps.site.outputs.artifact-id }}",
            "jobs.build.outputs");
        YamlSequenceNode buildSteps = GetRequiredSequence(build, "steps", "jobs.build");
        YamlMappingNode upload = RequireNamedStep(
            buildSteps,
            "Upload staged site artifact",
            "jobs.build");
        RequireScalarValue(upload, "id", "site", "staging artifact upload step");
        RequireScalarValue(
            upload,
            "uses",
            "actions/upload-artifact@v7",
            "staging artifact upload step");

        YamlMappingNode deploy = GetRequiredMapping(jobs, "deploy", "staging jobs");
        RequireScalarValue(deploy, "needs", "build", "jobs.deploy");
        YamlMappingNode environment =
            GetRequiredMapping(deploy, "environment", "jobs.deploy");
        RequireScalarValue(
            environment,
            "name",
            "inspect-web-staging",
            "jobs.deploy.environment");
        YamlSequenceNode deploySteps =
            GetRequiredSequence(deploy, "steps", "jobs.deploy");
        if (deploySteps.Children.Count != 3)
        {
            throw new InvalidOperationException(
                "Staging deployment must contain only artifact download, " +
                "artifact verification, and deploy steps.");
        }

        YamlMappingNode download =
            RequireStep(deploySteps, 0, "Download staged site artifact");
        RequireScalarValue(
            download,
            "uses",
            "actions/download-artifact@v8",
            "staging artifact download step");
        YamlMappingNode downloadWith =
            GetRequiredMapping(download, "with", "staging artifact download step");
        RequireScalarValue(
            downloadWith,
            "artifact-ids",
            "${{ needs.build.outputs.artifact_id }}",
            "staging artifact download step.with");
        RequireScalarValue(
            downloadWith,
            "digest-mismatch",
            "error",
            "staging artifact download step.with");

        YamlMappingNode verify =
            RequireStep(deploySteps, 1, "Verify staged site artifact");
        RequireScalarValue(
            verify,
            "run",
            "test -f artifacts/inspect-web-publish/wwwroot/index.html",
            "staging artifact verification step");

        YamlMappingNode deployStep =
            RequireStep(deploySteps, 2, "Deploy to staging");
        RequireScalarValue(
            deployStep,
            "uses",
            "Azure/static-web-apps-deploy@4d27395796ac319302594769cfe812bd207490b1",
            "staging deploy step");
    }

    private static YamlMappingNode RequireNamedStep(
        YamlSequenceNode steps,
        string name,
        string context)
    {
        YamlMappingNode[] matches = steps.Children
            .Select((node, index) => RequireMapping(node, $"{context} step {index}"))
            .Where(step => GetOptionalScalar(step, "name") == name)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"{context} contains {matches.Length} '{name}' steps; expected one.");
        }
        return matches[0];
    }

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static YamlMappingNode RequireStep(
        YamlSequenceNode steps,
        int index,
        string? name)
    {
        YamlMappingNode step = RequireMapping(
            steps.Children[index],
            $"jobs.deploy step {index}");
        if (name is not null)
            RequireScalarValue(step, "name", name, $"jobs.deploy step {index}");
        return step;
    }
}
