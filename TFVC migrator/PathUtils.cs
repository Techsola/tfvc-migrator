using System;

namespace TfvcMigrator
{
    internal static class PathUtils
    {
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
    }
}
