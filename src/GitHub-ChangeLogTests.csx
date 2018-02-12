#! "netcoreapp2.0"
#r "nuget:FluentAssertions, 4.19.4"
#load "nuget:ScriptUnit, 0.1.4"

#load "GitHub-ChangeLog.csx"

using static ScriptUnit;
using static ChangeLog;
using FluentAssertions;

//await AddTestsFrom<ChangeLogTests>().AddFilter(m => m.Name == "ShouldGenerateOnlyForMatchingTags").Execute();
await AddTestsFrom<ChangeLogTests>().Execute();

public class ChangeLogTests
{
    public async Task ShouldOutputDefaultHeader()
    {
        ChangeLogSummary summary = null; ;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .Generate(Console.Out);

        summary.Header.Should().Be("Change Log");
    }

    public async Task ShouldOutputCustomHeader()
    {
        ChangeLogSummary summary = null; ;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithHeader("Release Notes")
            .WithFormatter((w, s) => summary = s)
            .Generate(Console.Out);

        summary.Header.Should().Be("Release Notes");
    }

    public async Task ShouldPutMergedPullRequestInDefaultPullRequestGroup()
    {
        ChangeLogSummary summary = null;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .Generate(Console.Out);

        var releaseNote = summary.ReleaseNotes.Single(r => r.Header.Title == "0.1.0");
        releaseNote.Groups.Single().Name.Should().Be("Merged Pull Requests");
    }

    public async Task ShouldPutMergedPullRequestInCustomGroup()
    {
        ChangeLogSummary summary = null; ;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .PullRequestsMatching(pr => pr.Labels.Contains("bug")).GroupsInto("Fixed Bugs")
            .Generate(Console.Out);

        var releaseNote = summary.ReleaseNotes.Single(r => r.Header.Title == "0.1.0");

        releaseNote.Groups.Single().Name.Should().Be("Fixed Bugs");
    }

    public async Task ShouldPutClosedIssueInDefaultGroup()
    {
        ChangeLogSummary summary = null;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .Generate(Console.Out);

        var releaseNote = summary.ReleaseNotes.Single(r => r.Header.Title == "0.2.0");
        releaseNote.Groups.Should().Contain(g => g.Name == "Closed Issues");
    }

    public async Task ShouldPutClosedIssueInCustomGroup()
    {
        ChangeLogSummary summary = null;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .IssuesMatching(i => true).GroupsInto("Custom Group")
            .Generate(Console.Out);

        var releaseNote = summary.ReleaseNotes.Single(r => r.Header.Title == "0.2.0");
        releaseNote.Groups.Should().Contain(g => g.Name == "Custom Group");
    }

    public async Task ShouldIncludeUnreleased()
    {

        ChangeLogSummary summary = null; ;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .SinceTag("0.2.0")
            .IncludeUnreleased()
            .Generate(Console.Out);

    }

    public async Task ShouldGenerateChangeLogSinceLatestTag()
    {
        ChangeLogSummary summary = null; ;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .WithTagsMatching("^0.*")
            .SinceLatestTag()            
            .Generate(Console.Out);

        summary.ReleaseNotes.Single().Header.Title.Should().Be("0.2.0");
    }

    public async Task ShouldGenerateChangeLogSinceTag()
    {
        ChangeLogSummary summary = null;;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper","changelog-fixture", accessToken)
            .WithFormatter((w,s) => summary = s)
            .SinceTag("0.2.0")            
            .Generate(Console.Out);
        summary.ReleaseNotes.Should().Contain(note => note.Header.Title == "0.2.0");
        summary.ReleaseNotes.Should().NotContain(note => note.Header.Title == "0.1.0");
    }

    public async Task ShouldGenerateFullChangeLog()
    {
        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .IncludeUnreleased()
            .Generate(Console.Out);
    }

    public async Task ShouldOutputChangeLogToFile()
    {
        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        using (var disposableFolder = new DisposableFolder())
        {
            var fileName = Path.Combine(disposableFolder.Path, "CHANGELOG.md");
            await ChangeLogFrom("seesharper","changelog-fixture", accessToken).Generate(fileName);
            string content = File.ReadAllText(fileName);
            content.Should().NotBeEmpty();
        }
    }


    public async Task ShouldPutMergedPullRequestIntoMatchingGroup()
    {
        ChangeLogSummary summary = null;;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper","changelog-fixture", accessToken)
            .WithFormatter((w,s) => summary = s)
            .PullRequestsMatching(p => p.Labels.Contains("enhancement")).GroupsInto("Implemented Features")
            .SinceTag("0.2.0")            
            .Generate(Console.Out);
        var releaseNote = summary.ReleaseNotes.Single(r => r.Header.Title == "0.2.0");
        releaseNote.Groups.Should().Contain(cig => cig.Name == "Implemented Features");
    }
   
    public async Task ShouldGenerateOnlyForMatchingTags()
    {
        ChangeLogSummary summary = null; ;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .WithTagsMatching("major.*")
            .Generate(Console.Out);
        var releaseNote = summary.ReleaseNotes.Single(r => r.Header.Title == "major1.0.0");

        releaseNote.Groups.SelectMany(cig => cig.ClosedIssues).Count().Should().Be(2);
    }

    public async Task ShouldContainCompareUrl()
    {
        ChangeLogSummary summary = null;

        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .WithTagsMatching("^0.*")  
            .IncludeUnreleased()              
            .Generate(Console.Out);

        summary.ReleaseNotes.Count().Should().Be(3);
        summary.ReleaseNotes.Single(rn => rn.Header.Title == "Unreleased")
            .Header.CompareUrl.Should().Be("https://github.com/seesharper/changelog-fixture/compare/0.2.0...e63cba2fc8ee417fc64e7cce64d3c71996a59ca2");
        summary.ReleaseNotes.Single(rn => rn.Header.Title == "0.2.0")
            .Header.CompareUrl.Should().Be("https://github.com/seesharper/changelog-fixture/compare/0.1.0...0.2.0");            
        summary.ReleaseNotes.Single(rn => rn.Header.Title == "0.1.0")
            .Header.CompareUrl.Should().Be("https://github.com/seesharper/changelog-fixture/compare/edb1d1b32f91cf30466e9111d7f9a26083032491...0.1.0");                    
    }

    public async Task ShouldContainTagUrl()
    {
        ChangeLogSummary summary = null;
        
        var accessToken = System.Environment.GetEnvironmentVariable("GITHUB_REPO_TOKEN");
        await ChangeLogFrom("seesharper", "changelog-fixture", accessToken)
            .WithFormatter((w, s) => summary = s)
            .WithTagsMatching("^0.*")  
            .IncludeUnreleased()              
            .Generate(Console.Out);
        summary.ReleaseNotes.Single(rn => rn.Header.Title == "Unreleased")
            .Header.Url.Should().Be("https://github.com/seesharper/changelog-fixture/tree/HEAD");
        summary.ReleaseNotes.Single(rn => rn.Header.Title == "0.2.0")
            .Header.Url.Should().Be("https://github.com/seesharper/changelog-fixture/tree/0.2.0");                
        summary.ReleaseNotes.Single(rn => rn.Header.Title == "0.1.0")
            .Header.Url.Should().Be("https://github.com/seesharper/changelog-fixture/tree/0.1.0");                
    }       
}