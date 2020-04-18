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

        public static bool Overlaps(string path1, string path2)
        {
            if (path1.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(path1));

            if (path2.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(path2));

            return
                path2.Length > path1.Length + 1 ? path2[path1.Length] == '/' && path2.StartsWith(path1, StringComparison.OrdinalIgnoreCase) :
                path1.Length > path2.Length + 1 ? path1[path2.Length] == '/' && path1.StartsWith(path2, StringComparison.OrdinalIgnoreCase) :
                path1.Equals(path2, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetLeaf(string path)
        {
            var index = path.LastIndexOf('/');
            return index == -1 ? path : path[(index + 1)..];
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

        public static string RemoveContainingPath(string path, string containingPath)
        {
            if (path.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(path));

            if (containingPath.EndsWith('/'))
                throw new ArgumentException("Path should not end with a trailing slash.", nameof(containingPath));

            if (!IsOrContains(containingPath, path))
                throw new ArgumentException("The specified containing path does not contain the specified path.");

            return path.Substring(containingPath.Length);
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
