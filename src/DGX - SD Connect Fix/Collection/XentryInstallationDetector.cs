using Microsoft.Win32;

namespace Dgx.SdConnectFix.Collection;

internal enum XentryEdition
{
    Unknown,
    MercedesBenz,
    DaimlerTruck
}

internal sealed record XentryInstallation(
    string RootDirectory, XentryEdition Edition, string? RegisteredApplicationType, string? Version);

internal static class XentryInstallationDetector
{
    private const string RegistryPath = @"SOFTWARE\Mercedes-Benz\Installation Framework";
    internal const string UserDataRoot = @"C:\ProgramData\Mercedes-Benz";

    internal static XentryInstallation? Detect()
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var framework = registry.OpenSubKey(RegistryPath);
        XentryInstallation? registered = ReadCandidate(framework?.GetValue("INSTALLDIR") as string,
            framework?.GetValue("DAI_APPTYPE") as string,
            framework?.GetValue("DAI_VERSION") as string ?? framework?.GetValue("ReleaseVersionString") as string);
        if (registered is not null)
            return registered;

        return FindSingleCandidate(
            from drive in new[] { @"C:\", @"D:\" }
            from vendor in new[] { "Mercedes-Benz", "Daimler-Truck" }
            select Path.Combine(drive, "Program Files (x86)", vendor));
    }

    internal static XentryInstallation? FindSingleCandidate(IEnumerable<string> candidates)
    {
        XentryInstallation? found = null;
        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            XentryInstallation? candidate = ReadCandidate(path, null, null);
            if (candidate is null)
                continue;
            if (found is not null)
                return null;

            found = candidate;
        }

        return found;
    }

    private static XentryInstallation? ReadCandidate(string? candidate, string? applicationType, string? version)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        string path = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(path))
            return null;

        string root;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return null;
        }

        XentryEdition edition = IdentifyEdition(applicationType, Path.GetFileName(root));
        if (edition == XentryEdition.Unknown || !HasApplicationFiles(root, edition))
            return null;

        string? release = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        return new XentryInstallation(root, edition, applicationType, release);
    }

    internal static bool HasApplicationFiles(string root, XentryEdition edition)
    {
        string xentry = Path.Combine(root, "Xentry");
        string bin = Path.Combine(xentry, "bin");
        if (edition == XentryEdition.DaimlerTruck)
        {
            string truckBin = Path.Combine(xentry, "XD_DT", "bin");
            return Directory.Exists(truckBin) &&
                (File.Exists(Path.Combine(truckBin, "GlobalSystemSettings_install.properties"))
                    || File.Exists(Path.Combine(truckBin, "Xentry64.exe"))
                    || File.Exists(Path.Combine(bin, "Xentry64.exe")));
        }

        string carsBin = Path.Combine(xentry, "XD_MB", "bin");
        return File.Exists(Path.Combine(bin, "Xentry64.exe"))
            || File.Exists(Path.Combine(bin, "Xentry.exe"))
            || File.Exists(Path.Combine(carsBin, "GlobalSystemSettings_install.properties"));
    }

    internal static XentryEdition IdentifyEdition(string? applicationType, string folderName)
    {
        XentryEdition folderEdition = folderName.ToUpperInvariant() switch
        {
            "MERCEDES-BENZ" => XentryEdition.MercedesBenz,
            "DAIMLER-TRUCK" => XentryEdition.DaimlerTruck,
            _ => XentryEdition.Unknown
        };
        XentryEdition registeredEdition = applicationType?.Trim().ToUpperInvariant() switch
        {
            "XDOS" => XentryEdition.MercedesBenz,
            "S_DTPID" or "SDPID" => XentryEdition.DaimlerTruck,
            _ => XentryEdition.Unknown
        };

        if (registeredEdition == XentryEdition.Unknown)
            return folderEdition;

        if (folderEdition != XentryEdition.Unknown && folderEdition != registeredEdition)
            return XentryEdition.Unknown;

        return registeredEdition;
    }
}
