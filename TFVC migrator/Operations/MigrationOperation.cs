namespace TfvcMigrator.Operations
{
    public abstract class MigrationOperation
    {
        public abstract int Changeset { get; }

        public abstract override string ToString();
        public abstract override bool Equals(object? obj);
        public abstract override int GetHashCode();
    }
}
