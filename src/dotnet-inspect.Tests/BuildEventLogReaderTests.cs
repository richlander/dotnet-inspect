using DotnetInspector.BuildEvents;
using DotnetInspector.BuildEvents.Events;

namespace DotnetInspector.Tests;

public class BuildEventLogReaderTests
{
    [Fact]
    public void ReadLine_WithUnknownEvent_PreservesRawPayload()
    {
        IBuildEvent buildEvent = BuildEventLogReader.ReadLine("""
            {"schemaVersion":0,"kind":"future.event","sequenceNumber":5,"timestamp":"2026-06-10T04:00:04Z","threadId":1,"context":{"submissionId":0,"nodeId":1,"evaluationId":-1,"projectInstanceId":10,"projectContextId":100,"targetId":7,"taskId":8},"payload":{"note":"preserved"}}
            """);

        var unknown = Assert.IsType<UnknownBuildEvent>(buildEvent);
        Assert.Equal("future.event", unknown.Kind);
        Assert.Contains("\"note\":\"preserved\"", unknown.RawPayloadJson);
    }

    [Fact]
    public void ReadLine_WithKnownEvent_IgnoresUnknownFields()
    {
        IBuildEvent buildEvent = BuildEventLogReader.ReadLine("""
            {"schemaVersion":0,"kind":"build.finished","sequenceNumber":7,"timestamp":"2026-06-10T04:00:06Z","threadId":1,"context":{"submissionId":-1,"nodeId":-2,"evaluationId":-1,"projectInstanceId":-1,"projectContextId":-2,"targetId":-1,"taskId":-1},"payload":{"succeeded":true,"message":"Build succeeded","futurePayloadField":"ignored"},"futureEnvelopeField":true}
            """);

        var finished = Assert.IsAssignableFrom<IBuildEvent<BuildFinishedPayload>>(buildEvent);
        Assert.True(finished.Payload.Succeeded);
        Assert.Equal("Build succeeded", finished.Payload.Message);
    }

    [Fact]
    public void Load_WithMixedEvents_IndexesKnownEventsAndPreservesUnknownEvents()
    {
        string path = Path.Combine(Path.GetTempPath(), $"build-events-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, """
            {"schemaVersion":0,"kind":"project.started","sequenceNumber":1,"timestamp":"2026-06-10T04:00:01Z","threadId":1,"context":{"submissionId":0,"nodeId":1,"evaluationId":1,"projectInstanceId":10,"projectContextId":100,"targetId":-1,"taskId":-1},"payload":{"projectId":1,"projectFile":"/repo/App.csproj","targetNames":"Build","toolsVersion":null,"dimensions":{"targetFramework":"net10.0","targetFrameworks":null,"runtimeIdentifier":null,"runtimeIdentifiers":null,"configuration":"Debug","platform":null,"selfContained":null,"hasAnyDimension":true},"hasParentContext":false,"parentContext":{"submissionId":-1,"nodeId":-2,"evaluationId":-1,"projectInstanceId":-1,"projectContextId":-2,"targetId":-1,"taskId":-1}}}
            {"schemaVersion":0,"kind":"diagnostic","sequenceNumber":2,"timestamp":"2026-06-10T04:00:05Z","threadId":1,"context":{"submissionId":0,"nodeId":1,"evaluationId":-1,"projectInstanceId":10,"projectContextId":100,"targetId":7,"taskId":8},"payload":{"severity":"warning","subcategory":null,"code":"CS0168","file":"Program.cs","projectFile":"/repo/App.csproj","lineNumber":3,"columnNumber":9,"endLineNumber":3,"endColumnNumber":10,"message":"Unused variable","helpKeyword":"CS0168"}}
            {"schemaVersion":0,"kind":"future.event","sequenceNumber":3,"timestamp":"2026-06-10T04:00:06Z","threadId":1,"context":{"submissionId":0,"nodeId":1,"evaluationId":-1,"projectInstanceId":10,"projectContextId":100,"targetId":7,"taskId":8},"payload":{"note":"preserved"}}
            """);

        try
        {
            BuildEventLog log = BuildEventLog.Load(path);

            Assert.Single(log.Projects);
            Assert.Single(log.Diagnostics);
            Assert.Single(log.UnknownEvents);
            Assert.True(log.ProjectByContext.ContainsKey(new BuildContextKey(0, 10, 100)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
