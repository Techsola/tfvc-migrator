using System.Reflection;
using System.Text.RegularExpressions;

namespace TfvcMigrator.Tests;

public static class ReadmeTests
{
    [Test]
    public static void Command_line_arguments_section_is_up_to_date()
    {
        var helpOutput = TestUtils.CaptureConsoleOutput(() => Program.Main(new[] { "--help" }))
            .Replace(Assembly.GetEntryAssembly()!.GetName().Name!, typeof(Program).Assembly.GetName().Name);

        var expectedReadmeCodeBlock = Regex.Replace(helpOutput, @"\ADescription:\s*\n[^\n]*\n\s*\n", "");

        var readmeContents = File.ReadAllText(Path.Join(TestUtils.DetectSolutionDirectory(), "Readme.md"));
        var actualReadmeCodeBlock = Regex.Match(readmeContents, @"^## Command-line arguments(?:\s*\n)+```\s*\n(?<contents>.*)\s*\n```", RegexOptions.Singleline | RegexOptions.Multiline).Groups["contents"].Value;

        if (Normalize(actualReadmeCodeBlock) != Normalize(expectedReadmeCodeBlock))
        {
            Assert.Fail("Update the ‘Command-line arguments’ section in Readme.md using the program output for the --help.");
        }
    }

    private static string Normalize(string possiblyWrappedText)
    {
        return Regex.Replace(possiblyWrappedText.Trim(), @"\s+", " ");
    }
}
