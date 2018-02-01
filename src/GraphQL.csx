#! "netcoreapp2.0"
#r "nuget:System.Net.Http, 4.3.3"
#r "nuget:Newtonsoft.Json, 10.0.3"

using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static async Task<GraphQueryResult> ExecuteAsync(this HttpClient client, string requestUri, string query, object variables)
{
    var graphQuery = new GraphQuery(query, variables);
    var graphQueryAsJson = JsonConvert.SerializeObject(graphQuery);
    var content = new StringContent(graphQueryAsJson, System.Text.Encoding.UTF8, "application/json");
    var response = await client.PostAsync(requestUri, content);
    var resultAsJson = await response.Content.ReadAsStringAsync();
    return new GraphQueryResult(resultAsJson);
}

public static async Task<GraphQueryResult> ExecuteAsync(this HttpClient client, string query, object variables)
{
    return await ExecuteAsync(client, string.Empty, query, variables);
}

public class GraphQueryResult
{
    private JObject jObject;

    public GraphQueryResult(string json)
    {
        jObject = JObject.Parse(json);
    }

    public T Get<T>(string path)
    {
        if (jObject == null) return default(T);
        try
        {
           var token = jObject.SelectToken("data").SelectToken(path);           
           return token.ToObject<T>();
        }
        catch
        {
            return default(T);
        }
    }
}

public class GraphQuery
{
    public GraphQuery(string query, object variables)
    {
        Query = query;
        Variables = variables;
    }

    [JsonProperty("query")]
    public string Query { get; set; }

    [JsonProperty("variables")]
    public object Variables { get; set; }
}

public class Connection<T>
{
    public PageInfo PageInfo {get;set;}

    public int TotalCount {get;set;}

    public T[] Nodes {get;set;}
}

public class PageInfo
{    
  public PageInfo(string endCursor, bool hasNextPage)
  {
        EndCursor = endCursor;
        HasNextPage = hasNextPage;
    }

    public string EndCursor { get; }
    public bool HasNextPage { get; }
}