using System.IO;
using System.Reflection;

namespace LevelEditorPlugin;

public static class ContentPaths
{
    public static string Root
    {
        get
        {
            ResourceExtractor.EnsureExtracted();

            string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string besidePlugin = Path.Combine(pluginDirectory, "LevelEditor", "Content");
            if (Directory.Exists(besidePlugin))
                return besidePlugin;

            string nextToAssembly = Path.Combine(pluginDirectory, "Content");
            if (Directory.Exists(nextToAssembly))
                return nextToAssembly;

            return besidePlugin;
        }
    }

    public static string Mesh(string name)
    {
        return Path.Combine(Root, "Meshes", name + ".bin");
    }

    public static string Texture(params string[] parts)
    {
        return "Resources/Textures/LevelEditor/" + string.Join("/", parts);
    }
}
