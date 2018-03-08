#! "netcoreapp2.0"
#load "GraphQL.csx"

using System.Text.RegularExpressions;
using System.Net.Http;

/// <summary>
/// Serves as a namespace since there are no namespaces in scripts.
/// </summary>
public static class ChangeLog
{
    /// <summary>
    /// Creates a <see cref="ChangeLogGenerator"/> for the given 
    /// <paramref name="owner"/> and <paramref name="repository"/> name. 
    /// </summary>
    /// <param name="owner">The owner of the repository.</param>
    /// <param name="repository">The name of the repository.</param>
    /// <param name="apiKey">The API key/access token used to authenticate with GitHub.</param>
    /// <returns>A <see cref="ChangeLogGenerator"/> used to generate a change log.</returns>
    public static ChangeLogGenerator ChangeLogFrom(string owner, string repository, string apiKey)
    {
        return new ChangeLogGenerator(owner, repository, apiKey);
    }

    /// <summary>
    /// Provides change log generation with various options.   
    /// </summary>
    public class ChangeLogGenerator
    {
        private const string TagsQuery =
@"query ($owner: String!, $name: String!, $endCursor: String) {
  repository(owner: $owner, name: $name) {
    tags:refs(first: 100, refPrefix: ""refs/tags/"", after: $endCursor) {
      nodes {
        name
        commit:target {
          ... on Commit {
            date: committedDate,
            hash: oid,
            url: commitUrl
          }
        }
      }
      pageInfo {
        endCursor
        hasNextPage
      }
    }
  }
}
";
        private const string MergedPullRequestsQuery =
@"query ($owner: String!, $name: String!, $endCursor: String) {
  repository(owner: $owner, name: $name) {
    pullRequests(first: 100,states:MERGED, after: $endCursor) {
     nodes {        
      	number
      	title,  
      	mergedAt,
        url,
      	labels(first:100)
      	{
          nodes
          {
            name
          }
        }
        mergeCommit {
          hash:oid
          date:committedDate,
          url
        },
      	author{
          login,
          url
        }
      }
      pageInfo {
        endCursor
        hasNextPage
      }
    }
  }
}
";
        private const string ClosedIssuesQuery =
@"query ($owner: String!, $name: String!, $endCursor: String) {
  repository(owner: $owner, name: $name) {
    issues(first: 100,states:CLOSED, after: $endCursor) {
     nodes {        
      	number
      	title,  
      	closedAt,
        url,
      	labels(first:100)
      	{
          nodes
          {
            name
          }
        }        
      	author{
          login,
          url
        }
      }
      pageInfo {
        endCursor
        hasNextPage
      }
    }
  }
}";
        private const string LatestCommitQuery =
@"query ($owner: String!, $name: String!) {
  repository(owner: $owner, name: $name) {
    ref(qualifiedName: ""master"") {
     target {
        ... on Commit {
          history(first:1) {
            nodes{
              hash: oid,
              date: committedDate
              url
            }
            totalCount
            pageInfo {
              endCursor              
            }
          }
        }
      }
    }
  }
}";

        // Note: EndCursor  = EndCursor + totalcount 
        private const string FirstCommitQuery =
@"query ($owner: String!, $name: String!, $endCursor: String!) {
  repository(owner: $owner, name: $name) {
    ref(qualifiedName: ""master"") {
      target {
        ... on Commit {
          history(last: 1, before: $endCursor) {
            nodes {
              hash: oid,
              date: committedDate,
              url
            }            
          }
        }
      }
    }
  }
}
";

        private readonly string _owner;
        private readonly string _name;
        private readonly string _apiKey;
        private readonly string _sinceTag;
        private readonly string _defaultIssuesHeader;

        private readonly string _defaultPullRequestsHeader;

        private readonly string _header;

        private readonly string _tagPattern;

        private readonly bool _includeUnreleased;

        private readonly bool _sinceLatestTag;

        private readonly List<Func<ClosedIssue, TargetGroup>> _issueMatchers = new List<Func<ClosedIssue, TargetGroup>>();

        private readonly List<Func<MergedPullRequest, TargetGroup>> _pullRequestMatchers = new List<Func<MergedPullRequest, TargetGroup>>();

        private readonly Action<TextWriter, ChangeLogSummary> _formatter;

        internal ChangeLogGenerator(string owner, string name, string apiKey)
        {
            _owner = owner;
            _name = name;
            _apiKey = apiKey;
            _defaultIssuesHeader = "Closed Issues";
            _defaultPullRequestsHeader = "Merged Pull Requests";
            _header = "Change Log";
            _formatter = AsMarkdown;
            _tagPattern = ".";
        }

        private ChangeLogGenerator(string owner, string name, string apiKey, string defaultIssuesHeader, string defaultPullRequestsHeader, string sinceTag, bool sinceLatestTag, string header, string tagPattern, bool includeUnreleased, Action<TextWriter, ChangeLogSummary> formatter, List<Func<ClosedIssue, TargetGroup>> issueMatchers, List<Func<MergedPullRequest, TargetGroup>> pullRequestMatchers)
        {
            _owner = owner;
            _name = name;
            _apiKey = apiKey;
            _defaultIssuesHeader = defaultIssuesHeader;
            _defaultPullRequestsHeader = defaultPullRequestsHeader;
            _sinceTag = sinceTag;
            _sinceLatestTag = sinceLatestTag;
            _header = header;
            _tagPattern = tagPattern;
            _includeUnreleased = includeUnreleased;
            _formatter = formatter;
            _issueMatchers = issueMatchers;
            _pullRequestMatchers = pullRequestMatchers;
        }

        /// <summary>
        /// Allows issues matching the given <paramref name="predicate"/>
        /// to be placed under a custom header.
        /// </summary>
        /// <param name="predicate">The predicate used to filter issues.</param>
        /// <returns>An <see cref="IssueGrouping"/> used to specify the name and 
        /// ordering of the header.</returns>
        public IssueGrouping IssuesMatching(Func<ClosedIssue, bool> predicate)
            => new IssueGrouping(this, predicate);

        /// <summary>
        /// Allows pull requests matching the given <paramref name="predicate"/>
        /// to be placed under a custom header.
        /// </summary>
        /// <param name="predicate">The predicate used to filter pull requests.</param>
        /// <returns>An <see cref="PullRequestGrouping"/> used to specify the name and 
        /// ordering of the header.</returns>
        public PullRequestGrouping PullRequestsMatching(Func<MergedPullRequest, bool> predicate)
            => new PullRequestGrouping(this, predicate);

        /// <summary>
        /// Creates a new <see cref="ChangeLogGenerator"/> that includes unreleased changes.
        /// </summary>
        /// <returns><see cref="ChangeLogGenerator"/></returns>
        public ChangeLogGenerator IncludeUnreleased()
            => new ChangeLogGenerator(_owner, _name, _apiKey, _defaultIssuesHeader, _defaultPullRequestsHeader,
                _sinceTag, _sinceLatestTag,_header, _tagPattern, true, _formatter, _issueMatchers, _pullRequestMatchers);

        /// <summary>
        /// Creates a new <see cref="ChangeLogGenerator"/> that creates a change log since the given <paramref name="tag"/>.
        /// </summary>
        /// <param name="tag">The name of the tag from where to generate the change log.</param>
        /// <returns><see cref="ChangeLogGenerator"/></returns>
        public ChangeLogGenerator SinceTag(string tag)
            => new ChangeLogGenerator(_owner, _name, _apiKey, _defaultIssuesHeader, _defaultPullRequestsHeader, tag, _sinceLatestTag,
                _header, _tagPattern, _includeUnreleased, _formatter, _issueMatchers, _pullRequestMatchers);

        /// <summary>
        /// Creates a new <see cref="ChangeLogGenerator"/> that create a change log since the latest tag.
        /// </summary>
        /// <returns><see cref="ChangeLogGenerator"/></returns>
        public ChangeLogGenerator SinceLatestTag()
            => new ChangeLogGenerator(_owner, _name, _apiKey, _defaultIssuesHeader, _defaultPullRequestsHeader, _sinceTag, true,
                _header, _tagPattern, _includeUnreleased, _formatter, _issueMatchers, _pullRequestMatchers);

        /// <summary>
        /// Creates a new <see cref="ChangeLogGenerator"/> with a custom header. Default is "Change Log".
        /// </summary>
        /// <param name="customHeader">The new header to be used for the change log.</param>
        /// <returns><see cref="ChangeLogGenerator"/></returns>
        public ChangeLogGenerator WithHeader(string customHeader)
            => new ChangeLogGenerator(_owner, _name, _apiKey, _defaultIssuesHeader, _defaultPullRequestsHeader,
                _sinceTag, _sinceLatestTag, customHeader, _tagPattern, _includeUnreleased, _formatter, _issueMatchers,
                _pullRequestMatchers);

        /// <summary>
        /// Creates a new <see cref="ChangeLogGenerator"/> that generates a change log based on tags matching the given <paramref name="pattern"/>.
        /// </summary>
        /// <param name="pattern">A regular expression used to match tags.</param>
        /// <returns><see cref="ChangeLogGenerator"/></returns>
        public ChangeLogGenerator WithTagsMatching(string pattern)
            => new ChangeLogGenerator(_owner, _name, _apiKey, _defaultIssuesHeader, _defaultPullRequestsHeader,
                _sinceTag, _sinceLatestTag, _header, pattern, _includeUnreleased, _formatter, _issueMatchers, _pullRequestMatchers);

        /// <summary>
        /// Creates a new <see cref="ChangeLogGenerator"/> that generates a change log using a custom formatter.
        /// The default here is <see cref="AsMarkdown"/> method.
        /// </summary>
        /// <param name="changeLogFormatter">A delegate providing the <see cref="ChangeLogSummary"/>
        /// that represents the generated change log.</param>
        /// <returns></returns>
        public ChangeLogGenerator WithFormatter(Action<TextWriter, ChangeLogSummary> changeLogFormatter)
            => new ChangeLogGenerator(_owner, _name, _apiKey, _defaultIssuesHeader, _defaultPullRequestsHeader,
                _sinceTag, _sinceLatestTag, _header, _tagPattern, _includeUnreleased, changeLogFormatter, _issueMatchers,
                _pullRequestMatchers);

        /// <summary>
        /// Generates the change log and writes the generated <see cref="ChangeLogSummary"/> to the given <paramref name="textWriter"/>.
        /// </summary>
        /// <param name="textWriter">The <see cref="TextWriter"/> used to output the <see cref="ChangeLogSummary"/>.</param>
        /// <returns></returns>
        public async Task Generate(TextWriter textWriter)
        {
            var httpClient = new HttpClient { BaseAddress = new Uri("https://api.github.com/graphql") };
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(new System.Net.Http.Headers.ProductHeaderValue(_name)));
            var releaseNoteHeaders = await GetReleaseNoteHeaders(httpClient, _owner, _name);
            var pullrequests = await GetMergedPullRequests(httpClient, _owner, _name);
            var issues = await GetClosedIssues(httpClient, _owner, _name);

            var summary = new ChangeLogSummary(_header,
                releaseNoteHeaders.Select(tagCommit => CreateReleaseNote(tagCommit, issues, pullrequests)).ToArray());

            _formatter(textWriter, summary);
        }

        /// <summary>
        /// Generates the change log and writes the generated <see cref="ChangeLogSummary"/> to the given <paramref name="outputPath"/>.
        /// </summary>
        /// <param name="outputPath">The full path to the output file.</param>
        /// <returns></returns>
        public async Task Generate(string outputPath)
        {
            using (StreamWriter streamWriter = new StreamWriter(outputPath))
            {                
                await Generate(streamWriter);
            }
        }

        private ReleaseNote CreateReleaseNote(ReleaseNoteHeader header, ClosedIssue[] closedIssues, MergedPullRequest[] mergedPullRequests)
        {
            var categorizedEntries = new Dictionary<TargetGroup, (List<ClosedIssue> issues, List<MergedPullRequest> pullRequests)>();

            var closedIssuesBetweenTags = GetClosedIssuesBetweenTags(header, closedIssues);
            var mergedPullRequestsBetweenTags = GetMergedPullRequestsBetweenTags(header, mergedPullRequests);

            foreach (var closedIssue in closedIssuesBetweenTags)
            {
                var targetGroup = GetTargetGroupForClosedIssue(closedIssue);
                GetListsForTargetGroup(targetGroup).issues.Add(closedIssue);
            }

            foreach (var mergedPullRequest in mergedPullRequestsBetweenTags)
            {
                var targetGroup = GetTargetGroupForMergedPullRequest(mergedPullRequest);
                GetListsForTargetGroup(targetGroup).pullRequests.Add(mergedPullRequest);
            }

            var groups = categorizedEntries.Select(kvp => new Group(kvp.Key.Name, kvp.Key.SortOrder, kvp.Value.issues.ToArray(), kvp.Value.pullRequests.ToArray()));

            return new ReleaseNote(header, groups.ToArray());

            (List<ClosedIssue> issues, List<MergedPullRequest> pullRequests) GetListsForTargetGroup(TargetGroup entryGroup)
            {
                if (!categorizedEntries.TryGetValue(entryGroup, out var entryLists))
                {
                    entryLists = (new List<ClosedIssue>(), new List<MergedPullRequest>());
                    categorizedEntries.Add(entryGroup, entryLists);
                }
                return entryLists;
            }
        }

        private static MergedPullRequest[] GetMergedPullRequestsBetweenTags(ReleaseNoteHeader tagCommit, MergedPullRequest[] mergedPullRequests)
        {
            var mergedPullRequestsBetweenTags = mergedPullRequests.Where(i => i.MergedAt <= tagCommit.Until && i.MergedAt > tagCommit.Since).ToArray();
            return mergedPullRequestsBetweenTags;
        }

        private static ClosedIssue[] GetClosedIssuesBetweenTags(ReleaseNoteHeader tagCommit, ClosedIssue[] closedIssues)
        {
            var closedIssuesBetweenTags = closedIssues.Where(i => i.ClosedAt <= tagCommit.Until && i.ClosedAt > tagCommit.Since).ToArray();
            return closedIssuesBetweenTags;
        }

        /// <summary>
        /// Writes out the <paramref name="changeLogSummary"/> as MarkDown to the given <paramref name="textWriter"/>.
        /// </summary>
        /// <param name="textWriter">The <see cref="TextWriter"/> for which to write out the <paramref name="changeLogSummary"/>.</param>
        /// <param name="changeLogSummary">The <see cref="ChangeLogSummary"/> representing the generated change log.</param>
        public static void AsMarkdown(TextWriter textWriter, ChangeLogSummary changeLogSummary)
        {
            textWriter.WriteLine($"# {changeLogSummary.Header}");
            foreach (var releaseNote in changeLogSummary.ReleaseNotes)
            {                
                textWriter.WriteLine($"## [{releaseNote.Header.Title} ({releaseNote.Header.Until.ToString("d")})]({releaseNote.Header.Url})");
                textWriter.WriteLine();
                textWriter.WriteLine($@"### [Full Changelog]({releaseNote.Header.CompareUrl})");
                textWriter.WriteLine();
                var groups = releaseNote.Groups.OrderBy(eg => eg.SortOrder);
                foreach (var group in groups)
                {
                    textWriter.WriteLine($"**{group.Name}**");
                    foreach (var mergedPullRequest in group.MergedPullRequests)
                    {                        
                        textWriter.WriteLine($@"* {mergedPullRequest.Title} ({mergedPullRequest.MergedAt.ToString("d")}) [\#{mergedPullRequest.Number}]({mergedPullRequest.Url}) ([{mergedPullRequest.UserLogin}]({mergedPullRequest.UserUrl}))");
                    }
                    textWriter.WriteLine();
                    foreach (var closedIssue in group.ClosedIssues)
                    {
                        textWriter.WriteLine($@"* {closedIssue.Title} ({closedIssue.ClosedAt.ToString("d")}) [\#{closedIssue.Number}]({closedIssue.Url}) ([{closedIssue.UserLogin}]({closedIssue.UserUrl}))");                       
                    }
                }
            }
        }

        private TargetGroup GetTargetGroupForClosedIssue(ClosedIssue closedIssue)
        {
            foreach (var issueMatcher in _issueMatchers)
            {
                var group = issueMatcher(closedIssue);
                if (group != null)
                {
                    return group;
                }
            }

            return new TargetGroup(_defaultIssuesHeader, int.MaxValue);
        }

        private TargetGroup GetTargetGroupForMergedPullRequest(MergedPullRequest mergedPullRequest)
        {
            foreach (var issueMatcher in _pullRequestMatchers)
            {
                var group = issueMatcher(mergedPullRequest);
                if (group != null)
                {
                    return group;
                }
            }

            return new TargetGroup(_defaultPullRequestsHeader, int.MaxValue - 1);
        }

        private async Task<ReleaseNoteHeader[]> GetReleaseNoteHeaders(HttpClient client, string owner, string name)
        {
            List<ReleaseNoteHeader> headers = new List<ReleaseNoteHeader>();
            var result = await client.ExecuteAsync(TagsQuery, new { owner, name });
            var tagConnection = result.Get<Connection<TagResult>>("repository.tags");
            var firstAndLastCommit = await GetFirstAndLastCommit();

            var nodes = tagConnection.Nodes.Where(n => Regex.IsMatch(n.Name, _tagPattern)).OrderByDescending(n => n.Commit.Date).ToArray();

            string compareUrl = "";
            string url = "";

            for (int i = 0; i < nodes.Length; i++)
            {
                DateTimeOffset since;
                var currentTagResult = nodes[i];

                // Since the previous tag if it exists
                if (i < nodes.Length - 1)
                {
                    since = nodes[i + 1].Commit.Date;
                    compareUrl = $"https://github.com/{owner}/{name}/compare/{nodes[i + 1].Name}...{currentTagResult.Name}";
                }
                else
                {
                    compareUrl = $"https://github.com/{owner}/{name}/compare/{firstAndLastCommit.firstCommit.Hash}...{currentTagResult.Name}";
                    since = DateTimeOffset.MinValue;
                }
                url = $"https://github.com/{owner}/{name}/tree/{currentTagResult.Name}";
                var tagCommit = new ReleaseNoteHeader(currentTagResult.Name, since, currentTagResult.Commit.Date, compareUrl, url);
                headers.Add(tagCommit);
            }

            string sinceTag = _sinceTag;
            if (_sinceLatestTag)
            {
                sinceTag = headers.FirstOrDefault()?.Title;
            }

            if (!string.IsNullOrWhiteSpace(sinceTag))
            {
                var sinceHeader = headers.FirstOrDefault(tc => tc.Title == sinceTag);
                if (sinceHeader == null)
                {
                    throw new InvalidOperationException($"Unable to find since tag {sinceTag}");
                }

                headers = headers.Where(tc => tc.Until >= sinceHeader.Until).ToList();
            }

            if (_includeUnreleased)
            {
                DateTimeOffset since;
                url = $"https://github.com/{owner}/{name}/tree/HEAD";
                if (headers.Count > 0)
                {
                    since = headers.First().Until;
                    compareUrl = $"https://github.com/{owner}/{name}/compare/{headers.First().Title}...{firstAndLastCommit.latestCommit.Hash}";
                }
                else
                {
                    since = firstAndLastCommit.firstCommit.Date;
                    compareUrl = $"https://github.com/{owner}/{name}/compare/{firstAndLastCommit.firstCommit.Hash}...{firstAndLastCommit.latestCommit.Hash}";
                }
                var unreleasedHeader = new ReleaseNoteHeader("Unreleased", since, firstAndLastCommit.latestCommit.Date, compareUrl, url);
                headers.Add(unreleasedHeader);
            }
            return headers.OrderByDescending(tc => tc.Since).ToArray();

            async Task<(CommitResult firstCommit, CommitResult latestCommit)> GetFirstAndLastCommit()
            {
                var lastCommitResult = await client.ExecuteAsync(LatestCommitQuery, new { owner, name });
                var lastCommitConnection = lastCommitResult.Get<Connection<CommitResult>>("repository.ref.target.history");
                var endCursor = Regex.Replace(lastCommitConnection.PageInfo.EndCursor, @"^(.*\s*)\d+$", "${1}" + lastCommitConnection.TotalCount);
                var firstCommitResult = await client.ExecuteAsync(FirstCommitQuery, new { owner, name, endCursor });
                var firstCommitConnection = firstCommitResult.Get<Connection<CommitResult>>("repository.ref.target.history");
                return (firstCommitConnection.Nodes.Single(), lastCommitConnection.Nodes.Single());
            }
        }

        private async Task<MergedPullRequest[]> GetMergedPullRequests(HttpClient client, string owner, string name)
        {
            var connections = new List<Connection<MergedPullRequestResult>>();
                                    
            var mergedPullRequestConnection = await ExecuteQuery();
            connections.Add(mergedPullRequestConnection);
            while(mergedPullRequestConnection.PageInfo.HasNextPage)
            {
                mergedPullRequestConnection = await ExecuteQuery(mergedPullRequestConnection.PageInfo.EndCursor);
                connections.Add(mergedPullRequestConnection);
            }
           
            return connections.SelectMany(c => c.Nodes).Select(n => new MergedPullRequest(n.Number, n.Title, n.Url, GetMergedDate(n), n.Author.Login, n.Author.Url, n.Labels.Nodes.Select(ln => ln.Name).ToArray())).ToArray();
            

            async Task<Connection<MergedPullRequestResult>> ExecuteQuery(string endCursor = null)
            {
                var result = await client.ExecuteAsync(MergedPullRequestsQuery, new { owner, name, endCursor });
                return result.Get<Connection<MergedPullRequestResult>>("repository.pullRequests");
            }

            DateTimeOffset GetMergedDate(MergedPullRequestResult pullRequestResult)
            {
                if (pullRequestResult.MergeCommit != null)
                {
                    return pullRequestResult.MergeCommit.Date;
                }

                return pullRequestResult.MergedAt;
            }
        }

        private async Task<ClosedIssue[]> GetClosedIssues(HttpClient client, string owner, string name)
        {
            var connections = new List<Connection<ClosedIssueResult>>();
            var closedIssuesConnection = await ExecuteQuery();
            connections.Add(closedIssuesConnection);
            while(closedIssuesConnection.PageInfo.HasNextPage)
            {
                closedIssuesConnection = await ExecuteQuery();
                connections.Add(closedIssuesConnection);
            }
            
            return connections.SelectMany(c => c.Nodes).Select(n => new ClosedIssue(n.Number, n.Title, n.Url, n.ClosedAt.AddMinutes(-2), n.Author.Login, n.Author.Url, n.Labels.Nodes.Select(ln => ln.Name).ToArray())).ToArray();
           
            async Task<Connection<ClosedIssueResult>> ExecuteQuery(string endCursor = null)
            {
                var result = await client.ExecuteAsync(ClosedIssuesQuery, new { owner, name, endCursor });
                return result.Get<Connection<ClosedIssueResult>>("repository.issues");
            }
        }

        /// <summary>
        /// Represents a custom header for issues matching a certain condition.
        /// This could for instance be used to put all issues with a "bugfix" label into a header named "Fixed issues".
        /// </summary>
        public class IssueGrouping
        {
            private readonly ChangeLogGenerator _generator;
            private readonly Func<ClosedIssue, bool> _predicate;

            /// <summary>
            /// Initializes a new instance of the <see cref="IssueGrouping"/> class.
            /// </summary>
            /// <param name="generator">The current <see cref="ChangeLogGenerator"/>.</param>
            /// <param name="predicate">The predicate used to determine if a given issue should be put under this group.</param>
            public IssueGrouping(ChangeLogGenerator generator, Func<ClosedIssue, bool> predicate)
            {
                _generator = generator;
                _predicate = predicate;
            }

            /// <summary>
            /// Specifies the name and the sort order of the group.
            /// </summary>
            /// <param name="group">The name of the header for which to put the matching issues.</param>
            /// <param name="sortOrder">The sort order used when rendering the output.</param>
            /// <returns><see cref="ChangeLogGenerator"/></returns>
            public ChangeLogGenerator GroupsInto(string group, int sortOrder)
            {
                var issueMatchers = new List<Func<ClosedIssue, TargetGroup>>(_generator._issueMatchers);
                Func<ClosedIssue, TargetGroup> matcher = closedIssue => _predicate(closedIssue) ? new TargetGroup(group, sortOrder) : null;
                issueMatchers.Add(matcher);
                return new ChangeLogGenerator(_generator._owner, _generator._name, _generator._apiKey, _generator._defaultIssuesHeader, _generator._defaultPullRequestsHeader, _generator._sinceTag, _generator._sinceLatestTag, _generator._header, _generator._tagPattern, _generator._includeUnreleased, _generator._formatter, issueMatchers, _generator._pullRequestMatchers);
            }

            /// <summary>
            /// Specifies the name of the group.
            /// </summary>
            /// <param name="group">The name of the group for which to put the matching issues.</param>           
            /// <returns><see cref="ChangeLogGenerator"/></returns>
            public ChangeLogGenerator GroupsInto(string group)
            {
                return GroupsInto(group, _generator._issueMatchers.Count() + 1);
            }
        }

        /// <summary>
        /// Represents a custom group for pullrequests a certain condition.
        /// This could for instance be used to put all pullrequests with a "feature" label into a group named "Implemented features".
        /// </summary>
        public class PullRequestGrouping
        {
            private readonly ChangeLogGenerator _generator;
            private readonly Func<MergedPullRequest, bool> _predicate;

            /// <summary>
            /// Initializes a new instance of the <see cref="PullRequestGrouping"/> class.
            /// </summary>
            /// <param name="generator">The current <see cref="ChangeLogGenerator"/>.</param>
            /// <param name="predicate">The predicate used to determine if a given issue should be put under this group.</param>
            public PullRequestGrouping(ChangeLogGenerator generator, Func<MergedPullRequest, bool> predicate)
            {
                _generator = generator;
                _predicate = predicate;
            }

            /// <summary>
            /// Specifies the name and the sort order of the group.
            /// </summary>
            /// <param name="group">The name of the group for which to put the matching pullrequests.</param>     
            /// <param name="sortOrder">The sort order used when rendering the output.</param>      
            /// <returns><see cref="ChangeLogGenerator"/></returns>
            public ChangeLogGenerator GroupsInto(string group, int sortOrder)
            {
                var pullRequestMatchers = new List<Func<MergedPullRequest, TargetGroup>>(_generator._pullRequestMatchers);
                Func<MergedPullRequest, TargetGroup> matcher = mergedPullRequest => _predicate(mergedPullRequest) ? new TargetGroup(group, sortOrder) : null;
                pullRequestMatchers.Add(matcher);
                return new ChangeLogGenerator(_generator._owner, _generator._name, _generator._apiKey, _generator._defaultIssuesHeader, _generator._defaultPullRequestsHeader, _generator._sinceTag, _generator._sinceLatestTag, _generator._header, _generator._tagPattern, _generator._includeUnreleased, _generator._formatter, _generator._issueMatchers, pullRequestMatchers);
            }

            /// <summary>
            /// Specifies the name and the sort order of the group.
            /// </summary>
            /// <param name="group">The name of the group for which to put the matching pullrequests.</param>                 
            /// <returns><see cref="ChangeLogGenerator"/></returns>
            public ChangeLogGenerator GroupsInto(string group)
            {
                return GroupsInto(group, _generator._pullRequestMatchers.Count() + 1);
            }
        }


        private class TargetGroup
        {
            public TargetGroup(string name, int sortOrder)
            {
                Name = name;
                SortOrder = sortOrder;
            }

            public string Name { get; }
            public int SortOrder { get; }

            public override int GetHashCode()
            {
                return Name.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return ((TargetGroup)obj).Name.Equals(Name);
            }
        }

        public class TagResult
        {
            public TagResult(string name, CommitResult commit)
            {
                Name = name;
                Commit = commit;
            }

            public string Name { get; }
            public CommitResult Commit { get; }
        }
        public class CommitResult
        {
            public CommitResult(DateTimeOffset date, string hash, string url)
            {
                Date = date;
                Hash = hash;
                Url = url;
            }

            public DateTimeOffset Date { get; }
            public string Hash { get; }
            public string Url { get; }
        }

        public class MergedPullRequestResult
        {
            public MergedPullRequestResult(int number, string title, DateTimeOffset mergedAt, string url, Connection<LabelResult> labels, UserResult author, CommitResult mergeCommit)
            {
                Number = number;
                Title = title;
                MergedAt = mergedAt;
                Url = url;
                Labels = labels;
                Author = author;
                MergeCommit = mergeCommit;
            }

            public int Number { get; }
            public string Title { get; }
            public DateTimeOffset MergedAt { get; }
            public string Url { get; }
            public Connection<LabelResult> Labels { get; }
            public UserResult Author { get; }
            public CommitResult MergeCommit { get; }
        }

        public class ClosedIssueResult
        {
            public ClosedIssueResult(int number, string title, DateTimeOffset closedAt, string url, Connection<LabelResult> labels, UserResult author)
            {
                Number = number;
                Title = title;
                ClosedAt = closedAt;
                Url = url;
                Labels = labels;
                Author = author;
            }

            public int Number { get; }
            public string Title { get; }
            public DateTimeOffset ClosedAt { get; }
            public string Url { get; }
            public Connection<LabelResult> Labels { get; }
            public UserResult Author { get; }
        }


        public class UserResult
        {
            public UserResult(string login, string url)
            {
                Login = login;
                Url = url;
            }

            public string Login { get; }
            public string Url { get; }
        }

        public class LabelResult
        {
            public LabelResult(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }
    }

    /// <summary>
    /// Represents the result of the change log generation.
    /// </summary>
    public class ChangeLogSummary
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ChangeLogSummary"/> class.
        /// </summary>
        /// <param name="header">The header to be used for the change log summary.</param>
        /// <param name="releaseNotes">An array containing a <see cref="ReleaseNote"/> item for each tag.</param>
        public ChangeLogSummary(string header, ReleaseNote[] releaseNotes)
        {
            Header = header;
            ReleaseNotes = releaseNotes;
        }
        /// <summary>
        /// Gets the header to be used for the change log summary.
        /// </summary>
        public string Header { get; }

        /// <summary>
        /// Gets array containing an <see cref="ReleaseNote"/> item for each tag.
        /// </summary>
        public ReleaseNote[] ReleaseNotes { get; }
    }

    /// <summary>
    /// Represents changes belonging to a tag (Release).
    /// </summary>
    public class ReleaseNote
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReleaseNote"/> class.
        /// </summary>
        /// <param name="header">The <see cref="ReleaseNoteHeader"/> describing the release.</param>
        /// <param name="groups">An array containing a <see cref="Group"/> item for each group in the release note.</param>
        public ReleaseNote(ReleaseNoteHeader header, Group[] groups)
        {
            Header = header;
            Groups = groups;
        }
        /// <summary>
        /// Gets the <see cref="ReleaseNoteHeader"/> for this <see cref="ReleaseNote"/>.
        /// </summary>
        public ReleaseNoteHeader Header { get; }

        /// <summary>
        /// Gets array containing a <see cref="Group"/> item for each group in the release note.
        /// </summary>
        public Group[] Groups { get; }
    }

    /// <summary>
    /// Represents a group inside a <see cref="ReleaseNote"/> for which to place each 
    /// <see cref="ClosedIssue"/> and <see cref="MergedPullRequest"/>.
    /// </summary>
    public class Group
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Group"/> class.
        /// </summary>
        /// <param name="name">The name of the group.</param>
        /// <param name="sortOrder">The sort order used to determine the order when rendering the group.</param>
        /// <param name="closedIssues">An array containing <see cref="ClosedIssue"/> elements belonging to this <see cref="Group"/>.</param>
        /// <param name="mergedPullRequest">An array containing <see cref="MergedPullRequest"/> elements belonging to this <see cref="Group"/>.</param>
        public Group(string name, int sortOrder, ClosedIssue[] closedIssues, MergedPullRequest[] mergedPullRequest)
        {
            Name = name;
            SortOrder = sortOrder;
            ClosedIssues = closedIssues;
            MergedPullRequests = mergedPullRequest;
        }

        /// <summary>
        /// Gets the name of the group.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the sort order used to determine the order when rendering the group.
        /// </summary>
        public int SortOrder { get; }

        /// <summary>
        /// Gets an array containing <see cref="ClosedIssue"/> elements belonging to this <see cref="Group"/>.
        /// </summary>
        public ClosedIssue[] ClosedIssues { get; }

        /// <summary>
        /// Gets an array containing <see cref="MergedPullRequest"/> elements belonging to this <see cref="Group"/>.
        /// </summary>
        public MergedPullRequest[] MergedPullRequests { get; }
    }

    /// <summary>
    /// Represents a merged pullrequest.
    /// </summary>
    public class MergedPullRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MergedPullRequest"/> class.
        /// </summary>
        /// <param name="number">The id of the pullrequest.</param>
        /// <param name="title">The title of the pull request.</param>
        /// <param name="url">The url to the pullrequest on GitHub.</param>
        /// <param name="mergedAt">The <see cref="DateTimeOffset"/> for when this pullrequest was merged.</param>
        /// <param name="userLogin">The user that created the pull request.</param>
        /// <param name="userUrl">The url to the GitHub user that created the pull request.</param>
        /// <param name="labels">A list of labels for this pullrequest.</param>
        public MergedPullRequest(int number, string title, string url, DateTimeOffset mergedAt, string userLogin, string userUrl, string[] labels)
        {
            Number = number;
            Title = title;
            Url = url;
            MergedAt = mergedAt;
            UserLogin = userLogin;
            UserUrl = userUrl;
            Labels = labels;
        }

        /// <summary>
        /// Gets id of the pullrequest.
        /// </summary>
        public int Number { get; }

        /// <summary>
        /// Gets the title of the pull request.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets url to the pullrequest on GitHub. 
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Gets the <see cref="DateTimeOffset"/> for when this pullrequest was merged.
        /// </summary>
        public DateTimeOffset MergedAt { get; }

        /// <summary>
        /// Gets the user that created the pull request.
        /// </summary>
        public string UserLogin { get; }

        /// <summary>
        /// Gets the url to the GitHub user that created the pull request.
        /// </summary>
        public string UserUrl { get; }

        /// <summary>
        /// Gets s list of labels for this pullrequest.
        /// </summary>
        public string[] Labels { get; }
    }

    /// <summary>
    /// Represents a closed issue.
    /// </summary>
    public class ClosedIssue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClosedIssue"/> class.
        /// </summary>
        /// <param name="number">The id of the closed issue.</param>
        /// <param name="title">The title of the closed issue.</param>
        /// <param name="url">The url to the closed issue on GitHub.</param>
        /// <param name="closedAt">The <see cref="DateTimeOffset"/> for when this issue was closed.</param>
        /// <param name="userLogin">The user that created the issue.</param>
        /// <param name="userUrl">The url to the GitHub user that created the issue.</param>
        /// <param name="labels">A list of labels for this issue.</param>
        public ClosedIssue(int number, string title, string url, DateTimeOffset closedAt, string userLogin, string userUrl, string[] labels)
        {
            Number = number;
            Title = title;
            Url = url;
            ClosedAt = closedAt;
            UserLogin = userLogin;
            UserUrl = userUrl;
            Labels = labels;
        }

        /// <summary>
        /// Gets the id of the closed issue.
        /// </summary>
        public int Number { get; }

        /// <summary>
        /// Gets the title of the closed issue.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the url to the closed issue on GitHub.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// Gets the <see cref="DateTimeOffset"/> for when this issue was closed.
        /// </summary>
        public DateTimeOffset ClosedAt { get; }

        /// <summary>
        /// Gets the user that created the issue.
        /// </summary>
        public string UserLogin { get; }

        /// <summary>
        /// Gets the url to the GitHub user that created the issue.
        /// </summary>
        public string UserUrl { get; }

        /// <summary>
        /// Gets a list of labels for this issue.
        /// </summary>
        public string[] Labels { get; }
    }

    /// <summary>
    /// Represents a header for each tag/release.
    /// </summary>
    public class ReleaseNoteHeader
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReleaseNoteHeader"/> class.
        /// </summary>
        /// <param name="title">The title of the header.</param>
        /// <param name="since">The <see cref="DateTimeOffset"/> that indicates to include changes since this date.</param>
        /// <param name="until">The <see cref="DateTimeOffset"/> that indicates to include changes until this date.</param>
        /// <param name="compareUrl">The url for comparing changes on GitHub.</param>
        /// <param name="url">The url to the tag/release.</param>
        public ReleaseNoteHeader(string title, DateTimeOffset since, DateTimeOffset until, string compareUrl, string url)
        {
            Title = title;
            Since = since;
            Until = until;
            CompareUrl = compareUrl;
            Url = url;
        }

        /// <summary>
        /// Gets the title of the header.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the <see cref="DateTimeOffset"/> that indicates to include changes since this date.
        /// </summary>
        public DateTimeOffset Since { get; }

        /// <summary>
        /// Gets the <see cref="DateTimeOffset"/> that indicates to include changes until this date.
        /// </summary>
        public DateTimeOffset Until { get; }

        /// <summary>
        /// Gets the url for comparing changes on GitHub.
        /// </summary>
        public string CompareUrl { get; }

        /// <summary>
        /// Gets the url to the tag/release.
        /// </summary>
        public string Url { get; }
    }
}