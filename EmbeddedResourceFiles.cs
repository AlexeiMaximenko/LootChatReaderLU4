using System.Reflection;

namespace LootChatReader;

internal static class EmbeddedResourceFiles
{
    public static Icon? LoadIcon(string resourceName)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return null;
            }

            using var source = new Icon(stream);
            return (Icon)source.Clone();
        }
        catch
        {
            return null;
        }
    }

    public static void EnsureExtracted(string resourceName, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException($"No directory was specified for {destinationPath}.");
        Directory.CreateDirectory(directory);

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource {resourceName} was not found.");

        var temporaryPath = destinationPath + $".{Environment.ProcessId}.tmp";
        try
        {
            using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                resource.CopyTo(destination);
            }

            try
            {
                File.Move(temporaryPath, destinationPath, false);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // Another application instance extracted the same resource first.
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
