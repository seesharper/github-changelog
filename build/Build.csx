#! "netcoreapp2.0"
#load "nuget:Dotnet.Build, 0.1.3"
#load "../src/GitHub-ChangeLog.csx"
#r "nuget:System.Net.Http, 4.3.3"
#r "nuget:Newtonsoft.Json, 10.0.3"

using static FileUtils;
using static ChangeLog;
using System.Runtime.CompilerServices;

var scriptFolder = GetScriptFolder();
var tempFolder = CreateDirectory(scriptFolder,"tmp");
var pathToNuGetArtifacts = CreateDirectory(Path.Combine(scriptFolder), "Artifacts", "NuGet");
var pathToGitHubArtifacts = CreateDirectory(Path.Combine(scriptFolder), "Artifacts", "GitHub");


var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");

using(StreamWriter sw = new StreamWriter(Path.Combine(pathToGitHubArtifacts,"ReleaseNotes.md")))
{
    var generator = ChangeLogFrom("seesharper","github-changelog", accessToken);
    if (!Git.Default.IsTagCommit())
    {
        generator = generator.IncludeUnreleased();
    }
    await generator.Generate(sw);
}


var contentFolder = CreateDirectory(tempFolder, "contentFiles", "csx", "any");

Copy(Path.Combine(scriptFolder,"..","src","GitHub-ChangeLog.csx"),Path.Combine(contentFolder,"main.csx"));
Copy(Path.Combine(scriptFolder,"..","src","GraphQL.csx"), Path.Combine(contentFolder,"GraphQL.csx"));
Copy(Path.Combine(scriptFolder,"GitHub-ChangeLog.nuspec"), Path.Combine(tempFolder,"GitHub-ChangeLog.nuspec"));

string pathToUnitTests = Path.Combine(scriptFolder,"..","src","GitHub-ChangeLogTests.csx");
DotNet.Test(pathToUnitTests);

NuGet.Pack(tempFolder, pathToNuGetArtifacts);




