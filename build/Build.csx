#! "netcoreapp2.0"
#load "nuget:Dotnet.Build, 0.1.3"
using static FileUtils;
using System.Runtime.CompilerServices;

var scriptFolder = GetScriptFolder();
var tempFolder = CreateDirectory(scriptFolder,"tmp");
var pathToNuGetArtifacts = CreateDirectory(Path.Combine(scriptFolder), "Artifacts", "NuGet");
var contentFolder = CreateDirectory(tempFolder, "contentFiles", "csx", "any");

Copy(Path.Combine(scriptFolder,"..","src","GitHub-ChangeLog.csx"),Path.Combine(contentFolder,"main.csx"));
Copy(Path.Combine(scriptFolder,"..","src","GraphQL.csx"), Path.Combine(contentFolder,"GraphQL.csx"));
Copy(Path.Combine(scriptFolder,"GitHub-ChangeLog.nuspec"), Path.Combine(tempFolder,"GitHub-ChangeLog.nuspec"));

string pathToUnitTests = Path.Combine(scriptFolder,"..","src","GitHub-ChangeLogTests.csx");
// DotNet.Test(pathToUnitTests);

NuGet.Pack(tempFolder, pathToNuGetArtifacts);




