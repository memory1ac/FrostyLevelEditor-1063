using System;
using System.IO;
using System.Reflection;

namespace LevelEditorPlugin;

/// <summary>
/// Packs gizmo / shader / texture files into the plugin DLL and writes them
/// to the Frosty Editor folders a DLL-only install would otherwise miss.
/// </summary>
internal static class ResourceExtractor
{
    private const string ContentMarker = ".Content.";
    private static readonly object Gate = new object();
    private static bool extracted;

    public static int EnsureExtracted()
    {
        lock (Gate)
        {
            if (extracted)
                return 0;

            Assembly assembly = Assembly.GetExecutingAssembly();
            string pluginDir = Path.GetDirectoryName(assembly.Location) ?? "";
            string editorRoot = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string cwdRoot = Directory.GetCurrentDirectory();

            int written = 0;
            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                string relativePath = TryGetContentRelativePath(resourceName);
                if (relativePath == null)
                    continue;

                byte[] bytes;
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        continue;
                    bytes = new byte[stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                            break;
                        offset += read;
                    }
                }

                written += WriteIfNeeded(Path.Combine(pluginDir, "LevelEditor", "Content", relativePath), bytes);

                string unix = relativePath.Replace('\\', '/');
                if (unix.StartsWith("Shaders/", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(relativePath);
                    written += WriteIfNeeded(Path.Combine(editorRoot, "Shaders", fileName), bytes);
                    if (!PathsEqual(editorRoot, cwdRoot))
                        written += WriteIfNeeded(Path.Combine(cwdRoot, "Shaders", fileName), bytes);
                }
                else if (unix.StartsWith("Textures/", StringComparison.OrdinalIgnoreCase))
                {
                    string underTextures = unix.Substring("Textures/".Length);
                    bool isDefault = underTextures.Equals("DefaultLayer.dds", StringComparison.OrdinalIgnoreCase)
                        || underTextures.Equals("DefaultSprite.dds", StringComparison.OrdinalIgnoreCase);

                    string editorTexturePath = isDefault
                        ? Path.Combine(editorRoot, "Resources", "Textures", Path.GetFileName(relativePath))
                        : Path.Combine(editorRoot, "Resources", "Textures", "LevelEditor", underTextures.Replace('/', Path.DirectorySeparatorChar));
                    written += WriteIfNeeded(editorTexturePath, bytes);

                    if (!PathsEqual(editorRoot, cwdRoot))
                    {
                        string cwdTexturePath = isDefault
                            ? Path.Combine(cwdRoot, "Resources", "Textures", Path.GetFileName(relativePath))
                            : Path.Combine(cwdRoot, "Resources", "Textures", "LevelEditor", underTextures.Replace('/', Path.DirectorySeparatorChar));
                        written += WriteIfNeeded(cwdTexturePath, bytes);
                    }
                }
            }

            extracted = true;
            return written;
        }
    }

    private static string TryGetContentRelativePath(string resourceName)
    {
        int marker = resourceName.IndexOf(ContentMarker, StringComparison.Ordinal);
        if (marker < 0)
            return null;

        string rest = resourceName.Substring(marker + ContentMarker.Length);
        string[] folders =
        [
            "Textures.Sprites.",
            "Textures.",
            "Shaders.",
            "Meshes."
        ];

        foreach (string folder in folders)
        {
            if (!rest.StartsWith(folder, StringComparison.Ordinal))
                continue;

            string directory = folder.TrimEnd('.').Replace('.', Path.DirectorySeparatorChar);
            string fileName = rest.Substring(folder.Length);
            if (string.IsNullOrEmpty(fileName))
                return null;
            return Path.Combine(directory, fileName);
        }

        return null;
    }

    private static int WriteIfNeeded(string path, byte[] bytes)
    {
        try
        {
            FileInfo info = new FileInfo(path);
            if (info.Exists && info.Length == bytes.Length)
                return 0;

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(path, bytes);
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        return string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
