using System.Reflection;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;

namespace TfvcMigrator.Tests;

public static class EntryPointTests
{
    [Test]
    public static void Version_option_prints_the_application_version()
    {
        var console = new TestConsole();

        Program.CreateCommandApp(console).Run(["--version"]).ShouldBe(0);
        console.Output.ShouldContain(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
    }

    [Test]
    public static void Unrecognized_option_is_rejected_before_migration()
    {
        var tester = new CommandAppTester();
        tester.SetDefaultCommand<Program.MigrateCommand>();
        tester.Configure(config => config.UseStrictParsing());

        var result = tester.Run([
            "http://someurl",
            "$/SomePath",
            "--authors", "authors.txt",
            "--xyz",
        ]);

        result.ExitCode.ShouldBe(-1);
        result.Output.ShouldContain("--xyz");
        result.Settings.ShouldBeNull();
    }

    [Test]
    public static void Minimal_command_line_invokes_successfully()
    {
        string[] commandLine =
        [
            "http://someurl",
            "$/SomePath",
            "--authors", "authors.txt",
        ];

        var tester = new CommandAppTester();
        tester.SetDefaultCommand<TestMigrateCommand>();
        var result = tester.Run(commandLine);

        result.ExitCode.ShouldBe(0);

        var settings = result.Settings.ShouldBeOfType<Program.MigrateSettings>();
        settings.ProjectCollectionUrl.ShouldBe("http://someurl");
        settings.RootPath.ShouldBe("$/SomePath");
        settings.Authors.ShouldBe("authors.txt");
    }

    [Test]
    public static void Complete_command_line_invokes_successfully()
    {
        string[] commandLine =
        [
            "http://someurl",
            "$/SomePath",
            "--authors", "authors.txt",
            "--out-dir", "somedir",
            "--min-changeset", "42",
            "--max-changeset", "43",
            "--directories", "a/", "--directories", "b/c",
            "--root-path-changes", "CS1234:$/New/Path", "--root-path-changes", "CS1235:$/Another/Path",
            "--pat", "somepat",
        ];

        var tester = new CommandAppTester();
        tester.SetDefaultCommand<TestMigrateCommand>();
        var result = tester.Run(commandLine);

        result.ExitCode.ShouldBe(0);

        var settings = result.Settings.ShouldBeOfType<Program.MigrateSettings>();
        settings.ProjectCollectionUrl.ShouldBe("http://someurl");
        settings.RootPath.ShouldBe("$/SomePath");
        settings.Authors.ShouldBe("authors.txt");
        settings.OutDir.ShouldBe("somedir");
        settings.MinChangeset.ShouldBe(42);
        settings.MaxChangeset.ShouldBe(43);
        settings.Directories.ShouldBe(["a/", "b/c"]);
        settings.RootPathChanges.ShouldBe(["CS1234:$/New/Path", "CS1235:$/Another/Path"]);
        settings.Pat.ShouldBe("somepat");
    }

    private sealed class TestMigrateCommand : Command<Program.MigrateSettings>
    {
        protected override int Execute(CommandContext context, Program.MigrateSettings settings, CancellationToken cancellationToken) => 0;
    }
}
