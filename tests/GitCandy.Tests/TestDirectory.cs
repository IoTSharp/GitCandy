namespace GitCandy.Tests;

internal static class TestDirectory
{
    private const int DeleteAttempts = 10;
    private static readonly string TestRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "GitCandy.Tests"));

    public static string Create()
    {
        var path = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var expectedPrefix = TestRoot.EndsWith(Path.DirectorySeparatorChar)
            ? TestRoot
            : TestRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to delete test path outside '{TestRoot}'.");
        }

        if (Directory.Exists(fullPath))
        {
            foreach (var fileSystemInfo in new DirectoryInfo(fullPath)
                .EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                if ((fileSystemInfo.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    fileSystemInfo.Attributes &= ~FileAttributes.ReadOnly;
                }
            }

            for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
            {
                try
                {
                    Directory.Delete(fullPath, recursive: true);
                    break;
                }
                catch (Exception exception) when (
                    (exception is IOException or UnauthorizedAccessException)
                    && attempt < DeleteAttempts)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                }
            }
        }
    }
}
