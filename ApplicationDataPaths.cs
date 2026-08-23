namespace LootChatReader;

internal static class ApplicationDataPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LU4LootChatReader");

    public static string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public static string WorkspacePath => Path.Combine(RootDirectory, "workspace.json");

    public static void EnsureRootDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
    }
}
