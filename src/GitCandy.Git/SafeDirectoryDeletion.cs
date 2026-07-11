namespace GitCandy.Git;

internal static class SafeDirectoryDeletion
{
    public static void Delete(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        DeleteDirectory(directory, isRoot: true);
    }

    private static void DeleteDirectory(DirectoryInfo directory, bool isRoot)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Attributes = FileAttributes.Normal;
            directory.Delete();
            return;
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo childDirectory)
            {
                DeleteDirectory(childDirectory, isRoot: false);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
                entry.Delete();
            }
        }

        if (!isRoot || directory.Exists)
        {
            directory.Attributes = FileAttributes.Normal;
            directory.Delete();
        }
    }
}
