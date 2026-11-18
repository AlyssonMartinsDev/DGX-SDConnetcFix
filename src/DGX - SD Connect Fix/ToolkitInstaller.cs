using System.Diagnostics;
using Microsoft.Win32;

namespace Dgx.SdConnectFix;

internal static class ToolkitInstaller
{
    internal static void Install(string installationRoot, bool isTruck, char[] password, Action<int> progress)
    {
        if (XentryProcesses.IsRunning())
            throw new InvalidOperationException("Feche o XENTRY antes de instalar o Toolkit.");

        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Execute a edição x64 do DGX para instalar as bibliotecas.");

        using var mutex = new Mutex(false, @"Global\Dgx.SdConnectFix.ToolkitInstall");
        try
        {
            if (!mutex.WaitOne(0))
                throw new InvalidOperationException("Outra instância está instalando o Toolkit.");
        }
        catch (AbandonedMutexException)
        {
            // The previous installer stopped. A new installation can proceed.
        }

        try
        {
            InstallCore(installationRoot, isTruck, password, progress);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void InstallCore(string installationRoot, bool isTruck, char[] password, Action<int> progress)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            $".dgx-toolkit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string installer = Path.Combine(directory, "SDconnect.Toolkit.v2.1.14.0.exe");
        ToolkitLibraries.Backup? backup = null;
        try
        {
            EncryptedResource.Read("Dgx.SdConnectFix.Packages.Toolkit", "DGXINS01"u8, password, bytes =>
            {
                using var stream = new FileStream(installer, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(bytes);
                return true;
            });

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            backup = ToolkitLibraries.Install(windowsDirectory, password);
            DiagnosticLog.Write($"Quatro bibliotecas do Toolkit instaladas. Backup: {backup.DirectoryPath}");
            progress(40);
            RunAndWait(CreateStartInfo(installer, installationRoot), "Instalador do Toolkit");
            progress(75);

            ProcessStartInfo registration = CreateRegistrationStartInfo(installationRoot);
            string server = Path.Combine(registration.WorkingDirectory, "TkServer.exe");
            if (!File.Exists(server))
                throw new FileNotFoundException($"Servidor do Toolkit ausente: {server}", server);

            RunAndWait(registration, "Registro do TkServer");
            DiagnosticLog.Write($"TkServer registrado a partir de {registration.WorkingDirectory}.");
            if (isTruck)
                AuditTruckRegistration(installationRoot, windowsDirectory);
            progress(95);
            backup.Discard();
        }
        catch
        {
            if (backup is not null)
            {
                DiagnosticLog.Write($"Instalação interrompida. Restaurando DLLs. Backup: {backup.DirectoryPath}");
                backup.Restore();
            }

            throw;
        }
        finally
        {
            try
            {
                File.Delete(installer);
                Directory.Delete(directory);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.Write($"Limpeza do instalador temporário: {error.Message}");
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string installer, string installationRoot)
    {
        var start = new ProcessStartInfo(installer)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installer)!
        };
        start.ArgumentList.Add("/DIR=" + Path.Combine(installationRoot, "SDconnect Toolkit"));
        return start;
    }

    internal static ProcessStartInfo CreateRegistrationStartInfo(string installationRoot) =>
        new(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            Arguments = "/d /c start \"\" /wait .\\TkServer.exe /regserver",
            WorkingDirectory = Path.Combine(installationRoot, "SDconnect Toolkit", "bin"),
            UseShellExecute = false,
            CreateNoWindow = true
        };

    private static void AuditTruckRegistration(string installationRoot, string windowsDirectory)
    {
        const string toolkitClass = @"SOFTWARE\Classes\CLSID\{BFDC0FA8-4AB8-41DE-9615-2542A7DAB30E}\LocalServer32";
        AuditArchitecture(RegistryView.Registry64, "bin", "System32");
        AuditArchitecture(RegistryView.Registry32, "binx86", "SysWOW64");

        void AuditArchitecture(RegistryView view, string serverFolder, string libraryFolder)
        {
            try
            {
                string server = Path.Combine(installationRoot, "SDconnect Toolkit", serverFolder, "TkServer.exe");
                using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hive.OpenSubKey(toolkitClass);
                string? command = key?.GetValue(null) as string;
                string? registered = ReadExecutablePath(command);
                string serverVersion = ReadVersion(server);
                string apiVersion = ReadVersion(Path.Combine(windowsDirectory, libraryFolder, "ToolkitApiDll.dll"));
                string traceVersion = ReadVersion(Path.Combine(windowsDirectory, libraryFolder, "ToolkitTrace.dll"));
                bool matches = string.Equals(registered, server, StringComparison.OrdinalIgnoreCase)
                    && serverVersion != "indisponível" && serverVersion == apiVersion && serverVersion == traceVersion;
                DiagnosticLog.Write($"Toolkit {view}: {(matches ? "conferido" : "ATENÇÃO: divergência")}. " +
                    $"COM: {registered ?? "(ausente)"}; esperado: {server}; " +
                    $"servidor: {serverVersion}; API: {apiVersion}; trace: {traceVersion}.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                DiagnosticLog.Write($"Não foi possível conferir o registro Toolkit {view}: {error.Message}");
            }
        }
    }

    private static string ReadVersion(string path)
    {
        if (!File.Exists(path))
            return "indisponível";
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
        return string.IsNullOrWhiteSpace(info.FileVersion)
            ? "indisponível"
            : $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
    }

    private static string? ReadExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        command = command.Trim();
        int suffix = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return suffix < 0 ? null : command[..(suffix + 4)].TrimStart('"');
    }

    internal static void RunAndWait(ProcessStartInfo start, string step)
    {
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"Não foi possível iniciar: {step}.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{step} retornou código {process.ExitCode}.");
    }
}
