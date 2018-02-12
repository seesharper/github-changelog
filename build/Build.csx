#! "netcoreapp2.0"
#load "nuget:Dotnet.Build, 0.2.1"
#load "../src/GitHub-ChangeLog.csx"
#r "nuget:System.Net.Http, 4.3.3"
#r "nuget:Newtonsoft.Json, 10.0.3"

using static FileUtils;
using static ChangeLog;
using System.Runtime.CompilerServices;

var scriptFolder = GetScriptFolder();
var tempFolder = CreateDirectory(scriptFolder, "tmp");
var pathToNuGetArtifacts = CreateDirectory(Path.Combine(scriptFolder), "Artifacts", "NuGet");
var pathToGitHubArtifacts = CreateDirectory(Path.Combine(scriptFolder), "Artifacts", "GitHub");

if (BuildEnvironment.IsSecure)
{
    var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
    var generator = ChangeLogFrom("seesharper", "github-changelog", accessToken);
    if (!Git.Default.IsTagCommit())
    {
        generator = generator.IncludeUnreleased();
    }
    await generator.Generate(Path.Combine(pathToGitHubArtifacts, "ReleaseNotes.md"));    
}


var contentFolder = CreateDirectory(tempFolder, "contentFiles", "csx", "any");

Copy(Path.Combine(scriptFolder, "..", "src", "GitHub-ChangeLog.csx"), Path.Combine(contentFolder, "main.csx"));
Copy(Path.Combine(scriptFolder, "..", "src", "GraphQL.csx"), Path.Combine(contentFolder, "GraphQL.csx"));
Copy(Path.Combine(scriptFolder, "GitHub-ChangeLog.nuspec"), Path.Combine(tempFolder, "GitHub-ChangeLog.nuspec"));

string pathToUnitTests = Path.Combine(scriptFolder, "..", "src", "GitHub-ChangeLogTests.csx");
DotNet.Test(pathToUnitTests);

NuGet.Pack(tempFolder, pathToNuGetArtifacts);




