namespace TfvcMigrator;
public sealed class CommandLineException : Exception
{
    public int ExitCode { get; }
    public CommandLineException(string message, int exitCode = 1) : base(message)
    {
        ExitCode = exitCode;
    }
}
