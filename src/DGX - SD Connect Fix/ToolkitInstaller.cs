using System.Diagnostics;

namespace Dgx.SdConnectFix;

internal static class ToolkitInstaller
{
    internal static void Install(string installationRoot, char[] password, Action<int> progress)
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
            InstallCore(installationRoot, password, progress);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void InstallCore(string installationRoot, char[] password, Action<int> progress)
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

    internal static void RunAndWait(ProcessStartInfo start, string step)
    {
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"Não foi possível iniciar: {step}.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{step} retornou código {process.ExitCode}.");
    }
}
