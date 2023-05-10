namespace TfvcMigrator;
public sealed class CommandLineException : ApplicationException
{
    public int ExitCode { get; }
    public CommandLineException(string message, int exitCode = 1) : base(message)
    {
        ExitCode = exitCode;
    }
}
