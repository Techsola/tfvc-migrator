using System.Collections.Immutable;
using TfvcMigrator.Operations;

namespace TfvcMigrator
{
    internal readonly struct MappingState
    {
        public int Changeset { get; }
        public ImmutableArray<TopologicalOperation> TopologicalOperations { get; }
        public ImmutableArray<(BranchIdentity Branch, int ParentChangeset, BranchIdentity ParentBranch)> AdditionalParents { get; }
        public BranchIdentity Trunk { get; }
        public ImmutableArray<(BranchIdentity Branch, RepositoryBranchMapping Mapping)> BranchMappingsInDependentOperationOrder { get; }

        public MappingState(
            int changesetId,
            ImmutableArray<TopologicalOperation> topologicalOperations,
            ImmutableArray<(BranchIdentity Branch, int ParentChangeset, BranchIdentity ParentBranch)> additionalParents,
            BranchIdentity trunk,
            ImmutableArray<(BranchIdentity Branch, RepositoryBranchMapping Mapping)> branchMappingsInDependentOperationOrder)
        {
            Changeset = changesetId;
            TopologicalOperations = topologicalOperations;
            AdditionalParents = additionalParents;
            Trunk = trunk;
            BranchMappingsInDependentOperationOrder = branchMappingsInDependentOperationOrder;
        }
    }
}
