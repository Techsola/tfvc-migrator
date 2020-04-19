using System.Collections.Immutable;
using TfvcMigrator.Operations;

namespace TfvcMigrator
{
    internal readonly struct MappingState
    {
        public int ChangesetId { get; }
        public ImmutableArray<TopologicalOperation> TopologicalOperations { get; }
        public BranchIdentity Master { get; }
        public ImmutableDictionary<BranchIdentity, RepositoryBranchMapping> BranchMappings { get; }

        public MappingState(
            int changesetId,
            ImmutableArray<TopologicalOperation> topologicalOperations,
            BranchIdentity master,
            ImmutableDictionary<BranchIdentity, RepositoryBranchMapping> branchMappings)
        {
            ChangesetId = changesetId;
            TopologicalOperations = topologicalOperations;
            Master = master;
            BranchMappings = branchMappings;
        }
    }
}
