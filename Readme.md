# TFVC Migrator

Migrates source history from TFVC (Team Foundation Version Control) to idiomatic Git history while preserving branch topology. The goal of the tool is to answer the question, “What would it have looked like if we had been using Git from the beginning instead of TFVC?”

For example, say a project folder contains subdirectories A, B, and C, and you create a TFVC branch B-dev based on B. It's little more than a copied folder with metadata about where it came from. In TFVC, you see A, B, B-dev, and C all at once in the same file system.  
When this tool converts the project folder, it creates a Git repo with branches `main` and `B-dev`. The `main` Git branch contains A, B, and C, matching the same three TFVC folders. The `B-dev` Git branch also contains an A, an B, and a C, but this time the B folder corresponds to the *B-dev* TFVC folder rather than the *B* TFVC folder.

Differences from [https://github.com/git-tfs/git-tfs](https://github.com/git-tfs/git-tfs):

- Handles more complex branching and renaming than git-tfs (which was the main motivation)
- Designed for a one-time, one-way conversion rather than an ongoing bridge
- Very slightly nicer `[Migrated from CS12345]` appended to commit messages
- Exists to meet a need ad-hoc; not widely used or tested

## Support

This tool is offered **as-is** for a notoriously complex problem. We hope (without having a way to tell) that it works for someone else as well as it did for our projects. It might just work in one shot. You might have to be willing to get a little grease on your hands to take it the last step of the way. Issues and pull requests are welcome, but we can’t promise anything.

## How to use

1. Clone this repository (example: `git clone https://github.com/Techsola/tfvc-migrator`)
2. Navigate to the `src\TfvcMigrator` subdirectory (example: `cd tfvc-migrator\src\TfvcMigrator`)
3. Type `dotnet run` followed by the arguments below.
4. For large repositories, expect to wait for a while. You'll get a progress view like this:
   ```
   CS11853 (78.0%, 844 ms/changeset, ETA 11 min...)
   ```

## Command-line arguments

```
Usage:
  TfvcMigrator <project-collection-url> <root-path> [options]

Arguments:
  <project-collection-url>  The URL of the Azure DevOps project collection.
  <root-path>               The source path within the TFVC repository to migrate as a Git repository.

Options:
  --authors <authors> (REQUIRED)           Path to an authors file with lines mapping TFVC usernames to
                                           Git authors, e.g.: DOMAIN\John = John Doe <john@doe.com>
                                           Auto-generates file at provided path with placeholders if
                                           not found, eg: DOMAIN\John = John Doe <email>
  --out-dir <out-dir>                      The directory path at which to create a new Git repository.
                                           Defaults to the last segment in the root path under the
                                           current directory.
  --min-changeset <min-changeset>          The changeset defining the initial commit. Defaults to the
                                           first changeset under the given source path.
  --max-changeset <max-changeset>          The last changeset to migrate. Defaults to the most recent
                                           changeset under the given source path.
  --root-path-changes <root-path-changes>  Followed by one or more arguments with the format
                                           CS1234:$/New/Path. Changes the path that is mapped as the Git
                                           repository root to a new path during a specified changeset.
  --pat <pat>                              Personal access token, required to access TFVC repositories
                                           hosted on Azure DevOps Services. If not provided, default
                                           client credentials will be used which are only suitable for
                                           repositories hosted on Azure DevOps Server on-premises.
  --version                                Show version information
  -?, -h, --help                           Show help and usage information
```
