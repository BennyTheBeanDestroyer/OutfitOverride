using System.Reflection;

namespace OutfitOverride;

/// <summary>Loads assemblies embedded as resources (0Harmony.dll) so the plugin is a single DLL.</summary>
internal static class EmbeddedAssemblies
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            string file = new AssemblyName(args.Name).Name + ".dll";
            Assembly self = typeof(EmbeddedAssemblies).Assembly;
            string? resource = self.GetManifestResourceNames()
                .FirstOrDefault(r => r.Equals(file, StringComparison.OrdinalIgnoreCase));
            if (resource is null)
                return null;
            using Stream stream = self.GetManifestResourceStream(resource)!;
            using MemoryStream ms = new();
            stream.CopyTo(ms);
            return Assembly.Load(ms.ToArray());
        };
    }
}
