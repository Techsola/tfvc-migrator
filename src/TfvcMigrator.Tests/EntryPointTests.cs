namespace TfvcMigrator.Tests;

public static class EntryPointTests
{
    [Test]
    public static async Task No_System_CommandLine_failure_for_minimal_arguments()
    {
        var arguments = await CommandVerifier.VerifyArgumentsAsync(async () =>
            await Program.Main(new[]
            {
                "http://someurl",
                "$/SomePath",
                "--authors", "authors.txt",
            }));

        arguments[0].ShouldBe(new Uri("http://someurl"));
        arguments[1].ShouldBe("$/SomePath");
        arguments[2].ShouldBe("authors.txt");
    }

    [Test]
    public static async Task No_System_CommandLine_failure_for_all_arguments()
    {
        var arguments = await CommandVerifier.VerifyArgumentsAsync(async () =>
            await Program.Main(new[]
            {
                "http://someurl",
                "$/SomePath",
                "--authors", "authors.txt",
                "--out-dir", "somedir",
                "--min-changeset", "42",
                "--max-changeset", "43",
                "--root-path-changes", "CS1234:$/New/Path", "CS1235:$/Another/Path",
                "--pat", "somepat",
            }));

        arguments[0].ShouldBe(new Uri("http://someurl"));
        arguments[1].ShouldBe("$/SomePath");
        arguments[2].ShouldBe("authors.txt");
        arguments[3].ShouldBe("somedir");
        arguments[4].ShouldBe(42);
        arguments[5].ShouldBe(43);
        arguments[6].ShouldBeOfType<ImmutableArray<RootPathChange>>().ShouldBe(new[]
        {
            new RootPathChange(1234, "$/New/Path"),
            new RootPathChange(1235, "$/Another/Path"),
        });
        arguments[7].ShouldBe("somepat");
    }
}
