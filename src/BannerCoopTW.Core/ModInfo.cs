using System.Reflection;

namespace BannerCoopTW.Core
{
    public static class ModInfo
    {
        public const string Id = "BannerCoopTW";

        /// <summary>Informational version of the given assembly (e.g. "0.1.0+abc123"), falling back to the assembly version.</summary>
        public static string VersionOf(Assembly assembly)
        {
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
                return info!;
            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        public static string Version => VersionOf(typeof(ModInfo).Assembly);
    }
}
