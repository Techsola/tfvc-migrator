using System;

namespace TfvcMigrator
{
    internal static class PathUtils
    {
        public static bool IsAbsolute(string sourcePath)
        {
            return sourcePath.StartsWith("$/", StringComparison.Ordinal);
        }

        public static bool IsOrContains(string parentPath, string otherPath)
        {
            if (parentPath.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(parentPath));

            if (otherPath.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(otherPath));

            return otherPath.Length > parentPath.Length + 1
                ? otherPath[parentPath.Length] == '/' && otherPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase)
                : otherPath.Equals(parentPath, StringComparison.OrdinalIgnoreCase);
        }

        public static string ReplaceContainingPath(string path, string containingPath, string newContainingPath)
        {
            if (path.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(path));

            if (containingPath.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(containingPath));

            if (newContainingPath.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(newContainingPath));

            if (!IsOrContains(containingPath, path))
                throw new ArgumentException("The specified containing path does not contain the specified path.");

            return newContainingPath + path.Substring(containingPath.Length);
        }

        public static (string SourcePath, string TargetPath) RemoveCommonTrailingSegments(
            ReadOnlySpan<char> sourcePath,
            ReadOnlySpan<char> targetPath)
        {
            var targetLengthOffset = targetPath.Length - sourcePath.Length;

            while (true)
            {
                var sourceSlashIndex = sourcePath.LastIndexOf('/');
                if (sourceSlashIndex == -1) break;

                if (!targetPath.EndsWith(sourcePath.Slice(sourceSlashIndex), StringComparison.OrdinalIgnoreCase))
                {
                    return (sourcePath.ToString(), targetPath.ToString());
                }

                sourcePath = sourcePath.Slice(0, sourceSlashIndex);
                targetPath = targetPath.Slice(0, sourceSlashIndex + targetLengthOffset);
            }

            if (!targetPath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return (sourcePath.ToString(), targetPath.ToString());
            }

            return (string.Empty, string.Empty);
        }
    }
}
