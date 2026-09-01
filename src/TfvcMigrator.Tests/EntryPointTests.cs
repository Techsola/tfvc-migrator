namespace TfvcMigrator.Tests;

public static class EntryPointTests
{
    [Test]
    public static void No_System_CommandLine_failure_for_minimal_arguments()
    {
        var parseResult = Program.CreateCommand().Parse(
            new[]
            {
                "http://someurl",
                "$/SomePath",
                "--authors", "authors.txt",
            });

        parseResult.Errors.Select(error => error.Message).ShouldBeEmpty();
        parseResult.GetRequiredValue<string>("project-collection-url").ShouldBe("http://someurl");
        parseResult.GetRequiredValue<string>("root-path").ShouldBe("$/SomePath");
        parseResult.GetRequiredValue<string>("--authors").ShouldBe("authors.txt");
    }

    [Test]
    public static void No_System_CommandLine_failure_for_all_arguments()
    {
        var parseResult = Program.CreateCommand().Parse(
            new[]
            {
                "http://someurl",
                "$/SomePath",
                "--authors", "authors.txt",
                "--out-dir", "somedir",
                "--min-changeset", "42",
                "--max-changeset", "43",
                "--directories", "a/", "b/c",
                "--root-path-changes", "CS1234:$/New/Path", "CS1235:$/Another/Path",
                "--pat", "somepat",
            });

        parseResult.Errors.Select(error => error.Message).ShouldBeEmpty();
        parseResult.GetRequiredValue<string>("project-collection-url").ShouldBe("http://someurl");
        parseResult.GetRequiredValue<string>("root-path").ShouldBe("$/SomePath");
        parseResult.GetRequiredValue<string>("--authors").ShouldBe("authors.txt");
        parseResult.GetValue<string?>("--out-dir").ShouldBe("somedir");
        parseResult.GetValue<int?>("--min-changeset").ShouldBe(42);
        parseResult.GetValue<int?>("--max-changeset").ShouldBe(43);
        parseResult.GetValue<ImmutableArray<string>>("--directories").ShouldBe(new[] { "a/", "b/c" });
        parseResult.GetValue<ImmutableArray<RootPathChange>>("--root-path-changes").ShouldBe(new[]
        {
            new RootPathChange(1234, "$/New/Path"),
            new RootPathChange(1235, "$/Another/Path"),
        });
        parseResult.GetValue<string?>("--pat").ShouldBe("somepat");
    }
}
