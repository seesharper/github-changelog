#load "nuget:Dotnet.Build, 0.3.1"
using static FileUtils;

var owner = "seesharper";
var projectName = "github-changelog";

var root = FileUtils.GetScriptFolder();

var sourceFolder = Path.Combine(root, "..", "src");

var pathToUnitTests = Path.Combine(sourceFolder, "GitHub-ChangeLogTests.csx");

var artifactsFolder = CreateDirectory(root, "Artifacts");
var gitHubArtifactsFolder = CreateDirectory(artifactsFolder, "GitHub");

var nuGetArtifactsFolder = CreateDirectory(artifactsFolder, "NuGet");
var pathToReleaseNotes = Path.Combine(gitHubArtifactsFolder, "ReleaseNotes.md");