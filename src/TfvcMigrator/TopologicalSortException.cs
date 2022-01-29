using System.Text;

namespace TfvcMigrator;

public abstract class TopologicalSortException : Exception
{
    protected TopologicalSortException(string message) : base(message)
    {
    }
}

public sealed class TopologicalSortException<TItem, TKey> : TopologicalSortException
{
    public ImmutableArray<TItem> CyclicalDependencies { get; }
    public ImmutableArray<TKey> ExternalDependencies { get; }
    public ImmutableArray<TItem> ExternalDependents { get; }

    public TopologicalSortException(ImmutableArray<TItem> cyclicalDependencies, ImmutableArray<TKey> externalDependencies, ImmutableArray<TItem> externalDependents)
        : base(CreateMessage(cyclicalDependencies, externalDependencies, externalDependents))
    {
        CyclicalDependencies = cyclicalDependencies;
        ExternalDependencies = externalDependencies;
        ExternalDependents = externalDependents;
    }

    private static string CreateMessage(ImmutableArray<TItem> cyclicalDependencies, ImmutableArray<TKey> externalDependencies, ImmutableArray<TItem> externalDependents)
    {
        var r = new StringBuilder("There ");

        switch (cyclicalDependencies.Length)
        {
            case 0:
                break;
            case 1:
                r.Append("is one cyclical dependency");
                break;
            default:
                r.Append("are ").Append(cyclicalDependencies.Length).Append(" cyclical dependencies");
                break;
        }

        if (externalDependencies.Length != 0)
        {
            if (cyclicalDependencies.Length != 0) r.Append(", and there ");
            if (externalDependencies.Length == 1)
                r.Append("is one dependency not in the list (required by ");
            else
                r.Append("are ").Append(externalDependencies.Length).Append(" dependencies not in the list (required by ");
            if (externalDependents.Length == 1)
                r.Append("one dependent)");
            else
                r.Append(externalDependents.Length).Append(" dependents)");
        }

        return r.Append(". See this exception object for more detail.").ToString();
    }
}
