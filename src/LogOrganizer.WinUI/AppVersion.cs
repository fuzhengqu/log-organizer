using System.Reflection;

namespace LogOrganizer.WinUI;

/// <summary>Shows the shipped version ("0.01") without the build metadata suffix.</summary>
internal static class AppVersion
{
    internal static string Display { get; } = Read();

    private static string Read()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.00";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? informational[..plus] : informational;
    }
}