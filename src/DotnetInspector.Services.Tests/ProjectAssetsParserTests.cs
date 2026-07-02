using System;
using System.IO;
using Xunit;

namespace DotnetInspector.Services.Tests
{
    public class ProjectAssetsParserTests
    {
        [Fact]
        public void Parse_PrioritizesLongFormTfm()
        {
            var json = @"
            {
              ""version"": 3,
              ""targets"": {
                "".NETCoreApp,Version=v8.0"": { },
                ""net472"": { }
              },
              ""libraries"": { }
            }";
            
            var path = Path.GetTempFileName();
            File.WriteAllText(path, json);
            try
            {
                // Action to log
                string log = "";
                var result = ProjectAssetsParser.Parse(path, null, msg => log += msg);
                Assert.Contains("Using target framework: .NETCoreApp,Version=v8.0", log);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
