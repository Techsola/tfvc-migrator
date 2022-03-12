namespace TfvcMigrator.Tests;

public static class EntryPointTests
{
    [Test]
    public static async Task No_System_CommandLine_failure_for_minimal_arguments()
    {
        var arguments = await CommandVerifier.VerifyArgumentsAsync(async () =>
            await Program.Main(new[] { "http://someurl", "$/SomePath", "--authors", "authors.txt" }));

        arguments[0].ShouldBe(new Uri("http://someurl"));
        arguments[1].ShouldBe("$/SomePath");
        arguments[2].ShouldBe("authors.txt");
    }
}
