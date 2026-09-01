using System.Text.RegularExpressions;
using Spectre.Console.Testing;

namespace TfvcMigrator.Tests;

public static class ReadmeTests
{
    [Test]
    public static void Command_line_arguments_section_is_up_to_date()
    {
        var console = new TestConsole();
        console.Profile.Width = 100;

        Program.CreateCommandApp(console).Run(["--help"]);

        var helpOutput = console.Output.TrimLines().Trim();
        var expectedReadmeCodeBlock = Regex.Replace(helpOutput, @"\ADESCRIPTION:(\r?\n[^\r\n]+)*(\r?\n){2}", "", RegexOptions.IgnoreCase);

        var readmeContents = File.ReadAllText(Path.Join(TestUtils.DetectSolutionDirectory(), "Readme.md"));
        var actualReadmeCodeBlock = Regex.Match(readmeContents, @"^## Command-line arguments(?:\s*\n)+```\s*\n(?<contents>.*)\s*\n```", RegexOptions.Singleline | RegexOptions.Multiline).Groups["contents"].Value;

        if (actualReadmeCodeBlock.NormalizeLineEndings() != expectedReadmeCodeBlock.NormalizeLineEndings())
        {
            Assert.Fail($"""
                Update the ‘Command-line arguments’ section in Readme.md with the following exact contents:
                -----
                {expectedReadmeCodeBlock}
                -----
                """);
        }
    }
}
