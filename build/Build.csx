#! "netcoreapp2.0"
#load "Command.csx"

using System.Runtime.CompilerServices;

var scriptFolder = GetScriptFolder();
var tempFolder = Path.Combine(scriptFolder,"tmp");
RemoveDirectory(tempFolder);

var contentFolder = Path.Combine(tempFolder,"contentFiles","csx","any");
Directory.CreateDirectory(contentFolder);

File.Copy(Path.Combine(scriptFolder,"..","src","GitHub-ChangeLog.csx"), Path.Combine(contentFolder,"main.csx"));
File.Copy(Path.Combine(scriptFolder,"..","src","GraphQL.csx"), Path.Combine(contentFolder,"GraphQL.csx"));
File.Copy(Path.Combine(scriptFolder,"GitHub-ChangeLog.nuspec"),Path.Combine(tempFolder,"GitHub-ChangeLog.nuspec"));

string pathToUnitTests = Path.Combine(scriptFolder,"..","src","GitHub-ChangeLogTests.csx");
Command.Execute("dotnet", $"script {pathToUnitTests}");

Command.Execute("nuget",$"pack {Path.Combine(tempFolder,"GitHub-ChangeLog.nuspec")} -OutputDirectory {tempFolder}");



static string GetScriptPath([CallerFilePath] string path = null) => path;
static string GetScriptFolder() => Path.GetDirectoryName(GetScriptPath());

static void RemoveDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true
        foreach (string directory in Directory.GetDirectories(path))
        {
            RemoveDirectory(directory);
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException)
        {
            Directory.Delete(path, true);
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(path, true);
        }
    }