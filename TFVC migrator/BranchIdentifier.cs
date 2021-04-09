using System;
using System.Collections.Generic;
using System.Linq;

namespace TfvcMigrator
{
    public sealed class BranchIdentifier
    {
        private readonly List<(BranchIdentity Identity, int? DeleteChangeset)> branchesByDescendingSpecificity = new();
        private int maxKnownChangesetId;

        public BranchIdentifier(BranchIdentity initialFolder)
        {
            maxKnownChangesetId = initialFolder.CreationChangeset;
            branchesByDescendingSpecificity.Add((initialFolder, DeleteChangeset: null));
        }

        public BranchIdentity? FindBranchIdentity(int changeset, string itemPath)
        {
            if (maxKnownChangesetId < changeset)
                throw new ArgumentOutOfRangeException(nameof(changeset), changeset, $"Branch states are only known up to {maxKnownChangesetId}.");

            return branchesByDescendingSpecificity
                .FirstOrNull(b =>
                    (b.DeleteChangeset is null || changeset < b.DeleteChangeset.Value)
                    && b.Identity.IsOrContains(itemPath))
                ?.Identity;
        }

        public void NoFurtherChangesUpTo(int currentChangesetId)
        {
            if (currentChangesetId < maxKnownChangesetId)
                throw new ArgumentOutOfRangeException(nameof(currentChangesetId), currentChangesetId, "Operations must be performed in order.");

            maxKnownChangesetId = currentChangesetId;
        }

        public void Add(BranchIdentity newBranch)
        {
            if (newBranch.CreationChangeset <= maxKnownChangesetId)
                throw new ArgumentException("Operations must be performed in order.", nameof(newBranch));

            if (branchesByDescendingSpecificity.Any(b =>
                b.DeleteChangeset is null
                && b.Identity.Path.Equals(newBranch.Path, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"A branch already exists with the root path {newBranch.Path} right before CS{newBranch.CreationChangeset}.", nameof(newBranch));
            }

            InsertBranchInDescendingSpecificity(newBranch);

            maxKnownChangesetId = newBranch.CreationChangeset - 1;
        }

        public BranchIdentity Delete(int deleteChangeset, string branchPath)
        {
            if (deleteChangeset <= maxKnownChangesetId)
                throw new ArgumentOutOfRangeException(nameof(deleteChangeset), deleteChangeset, "Operations must be performed in order.");

            var index = GetBranchIndex(deleteChangeset, branchPath);

            var identity = branchesByDescendingSpecificity[index].Identity;
            branchesByDescendingSpecificity[index] = (identity, deleteChangeset);

            maxKnownChangesetId = deleteChangeset - 1;
            return identity;
        }

        public void Rename(int renameChangeset, string oldBranchPath, string newBranchPath, out BranchIdentity oldIdentity)
        {
            if (renameChangeset <= maxKnownChangesetId)
                throw new ArgumentOutOfRangeException(nameof(renameChangeset), renameChangeset, "Operations must be performed in order.");

            var index = GetBranchIndex(renameChangeset, oldBranchPath);

            oldIdentity = branchesByDescendingSpecificity[index].Identity;
            branchesByDescendingSpecificity.RemoveAt(index);
            InsertBranchInDescendingSpecificity(new BranchIdentity(renameChangeset, newBranchPath));

            maxKnownChangesetId = renameChangeset - 1;
        }

        private void InsertBranchInDescendingSpecificity(BranchIdentity newBranch)
        {
            var firstContainingBranchIndex = branchesByDescendingSpecificity.FindIndex(b => b.Identity.Contains(newBranch.Path));

            branchesByDescendingSpecificity.Insert(
                firstContainingBranchIndex != -1 ? firstContainingBranchIndex : branchesByDescendingSpecificity.Count,
                (newBranch, DeleteChangeset: null));
        }

        private int GetBranchIndex(int deleteChangeset, string branchPath)
        {
            var index = branchesByDescendingSpecificity.FindIndex(b =>
                b.DeleteChangeset is null && b.Identity.Path.Equals(branchPath, StringComparison.OrdinalIgnoreCase));

            return index == -1
                ? throw new InvalidOperationException($"No branch exists with the root path {branchPath} right before CS{deleteChangeset}.")
                : index;
        }
    }
}
