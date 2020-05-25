using System.Collections.Immutable;
using TfvcMigrator.Operations;

namespace TfvcMigrator
{
    internal readonly struct MappingState
    {
        public int Changeset { get; }
        public ImmutableArray<TopologicalOperation> TopologicalOperations { get; }
        public ImmutableArray<(BranchIdentity Branch, int ParentChangeset, BranchIdentity ParentBranch)> AdditionalParents { get; }
        public BranchIdentity Master { get; }
        public ImmutableDictionary<BranchIdentity, RepositoryBranchMapping> BranchMappings { get; }

        public MappingState(
            int changesetId,
            ImmutableArray<TopologicalOperation> topologicalOperations,
            ImmutableArray<(BranchIdentity Branch, int ParentChangeset, BranchIdentity ParentBranch)> additionalParents,
            BranchIdentity master,
            ImmutableDictionary<BranchIdentity, RepositoryBranchMapping> branchMappings)
        {
            Changeset = changesetId;
            TopologicalOperations = topologicalOperations;
            AdditionalParents = additionalParents;
            Master = master;
            BranchMappings = branchMappings;
        }
    }
}
