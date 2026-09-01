using System.CommandLine;
using System.CommandLine.Help;
using System.Text.RegularExpressions;

namespace TfvcMigrator.Tests;

public class ReadmeTests
{
    [Test]
    public async Task Command_line_arguments_section_is_up_to_date()
    {
        var command = Program.CreateCommand();

        // Pin the width so that Readme.md does not depend on the terminal the tests happen to run in.
        ((HelpAction)command.Options.OfType<HelpOption>().Single().Action!).MaxWidth = 100;

        var output = new StringWriter();
        await command.Parse(new[] { "--help" }).InvokeAsync(new InvocationConfiguration { Output = output });

        var helpOutput = output.ToString()
            .Replace(System.Reflection.Assembly.GetEntryAssembly()!.GetName().Name!, typeof(Program).Assembly.GetName().Name);

        var expectedReadmeCodeBlock = Regex.Replace(helpOutput, @"\ADescription:\s*\n[^\n]*\n\s*\n", "");

        var readmeContents = File.ReadAllText(Path.Join(TestUtils.DetectSolutionDirectory(), "Readme.md"));
        var actualReadmeCodeBlock = Regex.Match(readmeContents, @"^## Command-line arguments(?:\s*\n)+```\s*\n(?<contents>.*)\s*\n```", RegexOptions.Singleline | RegexOptions.Multiline).Groups["contents"].Value;

        if (Normalize(actualReadmeCodeBlock) != Normalize(expectedReadmeCodeBlock))
        {
            Assert.Fail("Update the ‘Command-line arguments’ section in Readme.md using the program output for --help.");
        }
    }

    private static string Normalize(string possiblyWrappedText)
    {
        return Regex.Replace(possiblyWrappedText.Trim(), @"\s+", " ");
    }
}
