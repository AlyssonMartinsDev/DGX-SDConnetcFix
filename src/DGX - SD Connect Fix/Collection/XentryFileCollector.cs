using System.Globalization;

namespace Dgx.SdConnectFix.Collection;

internal sealed record CollectionResult(string Destination, int Copied, int Missing, int Failed);

internal static class XentryFileCollector
{
    private static readonly string[] ConfigurationFiles =
        ["ReadFIN.cfg", "GlobalSystemSettings*.properties", "XentryServices.xml", "CAL.ini"];

    internal static CollectionResult CollectToDesktop(IReadOnlyList<XentryInstallation> installations)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new DirectoryNotFoundException("Área de Trabalho não localizada.");

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string destination = Path.Combine(desktop, $"DGX - Coleta XENTRY {timestamp}-{Guid.NewGuid():N}");
        var run = new CollectionRun(destination);

        // Capture the active PDU profile before the installation files.
        CollectUserData(run, XentryInstallationDetector.UserDataRoot, @"ProgramData\Mercedes-Benz");
        string nestedData = Path.Combine(XentryInstallationDetector.UserDataRoot, "ProgramData");
        if (Directory.Exists(Path.Combine(nestedData, "config")))
            CollectUserData(run, nestedData, @"ProgramData\Mercedes-Benz\ProgramData");

        if (installations.Count == 0)
            run.Note("Nenhuma instalação XENTRY localizada.");

        for (int index = 0; index < installations.Count; index++)
        {
            XentryInstallation installation = installations[index];
            string label = $"{index + 1:00}-{installation.Edition}";
            run.Note($"Instalação {label}: {installation.RootDirectory} ({installation.RegisteredApplicationType ?? "tipo não registrado"} | {installation.Version ?? "versão não informada"})");
            CollectInstallation(run, installation.RootDirectory, Path.Combine("Instalacoes", label));
        }

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0)
        {
            string nativeSystem = Path.Combine(windows, "Sysnative");
            run.Copy(Path.Combine(windows, Directory.Exists(nativeSystem) ? "Sysnative" : "System32"),
                @"Windows\System32", ["ToolkitApiDll.dll", "ToolkitTrace.dll"]);
            run.Copy(Path.Combine(windows, "SysWOW64"), @"Windows\SysWOW64",
                ["ToolkitApiDll.dll", "ToolkitTrace.dll"]);
        }

        return run.Complete();
    }

    private static void CollectUserData(CollectionRun run, string root, string target)
    {
        run.CopyFrom(root, target, @"config\ComDat", ["pdu_api_root*.xml"]);
        run.CopyFrom(root, target, @"config\ComDat\caesar\driver", ["SLAVE.INI"]);
        run.CopyFrom(root, target, @"config\Toolkit", ["toolkit_devices.ini"]);
        foreach (string config in new[] { @"config\XD_DT", @"config\XD_MB" })
            run.CopyFrom(root, target, config, [.. ConfigurationFiles, "GlobalUserSettings.properties"]);
    }

    private static void CollectInstallation(CollectionRun run, string root, string target)
    {
        foreach (string bin in new[] { @"Xentry\XD_DT\bin", @"Xentry\XD_MB\bin", @"Xentry\bin" })
            run.CopyFrom(root, target, bin, ConfigurationFiles);

        run.CopyFrom(root, target, @"DAS\bin", ["cal.ini"]);
        run.CopyFrom(root, target, @"Xentry\bin\Caesar64", ["asam3d.ini"]);
        run.CopyFrom(root, target, @"Xentry\bin\SelectPTDevice", ["SelectPTDevice.ini"]);
        run.CopyFrom(root, target, @"Xentry\bin\PDU64", ["*"], recursive: true);

        foreach (string toolkit in new[] { @"SDconnect Toolkit\bin", @"SDconnect Toolkit\binx86" })
            run.CopyFrom(root, target, toolkit,
                ["Toolkit.ini", "TkServer.exe", "TkTray.exe", "SDconnect.dll", "ToolkitAPIJNI.dll"]);
    }

    private sealed class CollectionRun(string destination)
    {
        private readonly List<string> notes = [];
        private int copied;
        private int missing;
        private int failed;

        internal void Note(string message) => notes.Add(message);

        internal void CopyFrom(string root, string target, string relativePath, string[] patterns,
            bool recursive = false) =>
            Copy(Path.Combine(root, relativePath), Path.Combine(target, relativePath), patterns, recursive);

        internal void Copy(string source, string target, string[] patterns, bool recursive = false)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false
            };

            foreach (string pattern in patterns)
            {
                try
                {
                    if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                    {
                        failed++;
                        Note($"IGNORADO {source}: ponto de reanálise.");
                        return;
                    }

                    string[] files = Directory.GetFiles(source, pattern, options);
                    if (files.Length == 0)
                    {
                        missing++;
                        Note($"AUSENTE {Path.Combine(source, pattern)}");
                    }

                    foreach (string file in files)
                        CopyFile(source, target, file);
                }
                catch (Exception error) when (error is DirectoryNotFoundException or FileNotFoundException)
                {
                    missing++;
                    Note($"PASTA AUSENTE {source}");
                    return;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    Note($"FALHA {Path.Combine(source, pattern)}: {error.Message}");
                }
            }
        }

        private void CopyFile(string source, string target, string file)
        {
            string output = Path.Combine(destination, target, Path.GetRelativePath(source, file));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(file, output);
                copied++;
                Note($"OK {file} -> {Path.GetRelativePath(destination, output)}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failed++;
                Note($"FALHA {file}: {error.Message}");
            }
        }

        internal CollectionResult Complete()
        {
            Directory.CreateDirectory(destination);
            Note($"Copiados: {copied} | Ausências: {missing} | Falhas: {failed}");
            File.WriteAllLines(Path.Combine(destination, "Resumo.txt"), notes);
            return new CollectionResult(destination, copied, missing, failed);
        }
    }
}
