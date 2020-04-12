namespace TfvcMigrator.Operations
{
    public sealed class DeleteOperation : BranchingOperation
    {
        public DeleteOperation(BranchIdentity branch)
        {
            Branch = branch;
        }

        public BranchIdentity Branch { get; }
    }
}
