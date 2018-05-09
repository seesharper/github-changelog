#! "netcoreapp2.0"
#load "BuildContext.csx"
#load "nuget:Dotnet.Build, 0.3.1"
#load "../src/GitHub-ChangeLog.csx"

using static FileUtils;
using static ChangeLog;
using static ReleaseManagement;
using System.Runtime.CompilerServices;


DotNet.Test(pathToUnitTests);

CreateNugetScriptPackage();

if (BuildEnvironment.IsSecure)
{
    await CreateReleaseNotes();

    if (Git.Default.IsTagCommit())
    {
        await Publish();
    }
}

async Task Publish()
{
    Git.Default.RequreCleanWorkingTree();
    await ReleaseManagerFor(owner, projectName, BuildEnvironment.GitHubAccessToken)
    .CreateRelease(Git.Default.GetLatestTag(), pathToReleaseNotes, Array.Empty<ReleaseAsset>());
    NuGet.TryPush(nuGetArtifactsFolder);
}

private async Task CreateReleaseNotes()
{
    Logger.Log("Creating release notes");
    var generator = ChangeLogFrom(owner, projectName, BuildEnvironment.GitHubAccessToken).SinceLatestTag();
    if (!Git.Default.IsTagCommit())
    {
        generator = generator.IncludeUnreleased();
    }
    await generator.Generate(pathToReleaseNotes, FormattingOptions.Default.WithPullRequestBody());
}

void CreateNugetScriptPackage()
{
    using (var packageBuildFolder = new DisposableFolder())
    {
        var contentFolder = CreateDirectory(packageBuildFolder.Path, "contentFiles", "csx", "any");
        Copy(Path.Combine(sourceFolder, "GitHub-ChangeLog.csx"), Path.Combine(contentFolder, "main.csx"));
        Copy(Path.Combine(sourceFolder, "GraphQL.csx"), Path.Combine(contentFolder, "GraphQL.csx"));
        Copy(Path.Combine(root, "GitHub-ChangeLog.nuspec"), Path.Combine(packageBuildFolder.Path, "GitHub-ChangeLog.nuspec"));
        NuGet.Pack(packageBuildFolder.Path, nuGetArtifactsFolder);
    }
}