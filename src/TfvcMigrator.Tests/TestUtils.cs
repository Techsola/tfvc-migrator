namespace TfvcMigrator.Tests;

internal static class TestUtils
{
    public static string DetectSolutionDirectory()
    {
        for (var current = AppContext.BaseDirectory; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Directory.EnumerateFiles(current, "*.sln").Any())
                return current;
        }

        throw new InvalidOperationException("Unable to locate the solution directory.");
    }
}
