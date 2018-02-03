# github-changelog

A C# script that generates changelog from GitHub repositories.

The changelog is generated based on tags in the target repository where each tag represents a release.

`github-changelog` will look for merged pull requests and closed issues between tags. 



## Usage

```c#
using static ChangeLog;

await ChangeLogFrom("seesharper","changelog-fixture", accessToken).Generate(Console.Out);     
```



## Options

### Header

The default change log header is "Change Log"

```c#
.WithHeader("Release Notes")
```

### Group Names

The default behavior is to put merged pull requests in a group named `Merged PullRequest` and closed issues in a group named `Closed issues`.

We can group pull requests and issues into custom groups using the `PullRequestsMatching` and `IssuesMatching` methods. 

For instance we could put pull requests with the `bug` label into a group named `Fixed Bugs` 

```c#
.PullRequestsMatching(pr => pr.Labels.Contains("bug")).GroupsInto("Fixed Bugs")
```

### Unreleased changes

Unreleased changes are closed issues and merged pull requests that is dated after the most recent tag.

```C#
.IncludeUnreleased();
```

### Tag Matching

Git allows us to have multiple tags for a given commit. This means that we might want to have different tag schemes based on for whom we are creating the changelog. 

For instance we might use an internal tag schema like `0.1.0`, `0.2.0` and so on internally and have another scheme for public releases like `v1.0.0`, `v2.0.0`

`github-changelog` allows us to create changelogs for multiple tag schemes using the `WithTagsMatching` method providing a regular expression for which tags are matched.

```
.WithTagsMatching("v.*");
```

### Since

The default behavior is to create the changelog based on all tags starting from the first tag and all the way up to the latest tag. We can choose to only generate the changelog since a given tag using the `SinceTag` method.

```c#
.SinceTag("0.2.0");
```

We can also use this to create a release note that only contains the changes between the two latest tags.

```c#
.SinceLatestTag();
```

> Note: Since means including the given tag and newer tags.



### Formatting

The default is to output the changelog as MarkDown. To provide our own changelog formatting, we can do this using the `WithFormatter` method.

```c#
.WithFormatter((textWriter,summary) => {custom formatting here})
```

