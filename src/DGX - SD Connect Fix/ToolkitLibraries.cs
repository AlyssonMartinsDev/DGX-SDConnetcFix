using System.IO.Compression;

namespace Dgx.SdConnectFix;

internal static class ToolkitLibraries
{
    private static readonly string[] Paths =
    [
        "System32/ToolkitApiDll.dll",
        "System32/ToolkitTrace.dll",
        "SysWOW64/ToolkitApiDll.dll",
        "SysWOW64/ToolkitTrace.dll"
    ];

    internal sealed record Change(string Target, string Backup, string Staged, bool Existed);

    internal sealed class Backup(string directory, IReadOnlyList<Change> changes)
    {
        internal string DirectoryPath { get; } = directory;

        internal void Restore() => RestoreFiles(changes);

        internal void Discard()
        {
            foreach (Change change in changes)
                DeleteFile(change.Backup);

            foreach (string path in changes.Select(change => Path.GetDirectoryName(change.Backup)!)
                .Distinct(StringComparer.OrdinalIgnoreCase).Append(DirectoryPath))
            {
                try { Directory.Delete(path); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    DiagnosticLog.Write($"Limpeza de backup {path}: {error.Message}");
                }
            }
        }
    }

    internal static Backup Install(string windowsDirectory, char[] password) =>
        EncryptedResource.Read("Dgx.SdConnectFix.Packages.ToolkitLibraries", "DGXDLL01"u8, password, bytes =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return InstallArchive(windowsDirectory, archive);
        });

    private static Backup InstallArchive(string windowsDirectory, ZipArchive archive)
    {
        if (archive.Entries.Count != Paths.Length)
            throw new InvalidDataException("Pacote de bibliotecas do Toolkit inválido.");

        string backupRoot = Path.Combine(windowsDirectory, "DGX-Toolkit-Backup",
            $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        var planned = new List<Change>(Paths.Length);
        var applied = new List<Change>(Paths.Length);

        try
        {
            foreach (string path in Paths)
            {
                ZipArchiveEntry entry = archive.GetEntry(path)
                    ?? throw new InvalidDataException($"Biblioteca ausente no pacote: {path}");
                string target = Path.Combine(windowsDirectory, path);
                if (!Directory.Exists(Path.GetDirectoryName(target)))
                    throw new DirectoryNotFoundException(Path.GetDirectoryName(target));

                string backup = Path.Combine(backupRoot, path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                string staged = backup + ".pending";
                var change = new Change(target, backup, staged, File.Exists(target));
                planned.Add(change);
                entry.ExtractToFile(staged);
            }

            foreach (Change change in planned)
            {
                if (change.Existed)
                    File.Replace(change.Staged, change.Target, change.Backup);
                else
                    File.Move(change.Staged, change.Target);

                applied.Add(change);
            }

            return new Backup(backupRoot, applied);
        }
        catch
        {
            RestoreFiles(applied);
            throw;
        }
        finally
        {
            foreach (Change change in planned)
                DeleteFile(change.Staged);
        }
    }

    private static void RestoreFiles(IReadOnlyList<Change> applied)
    {
        for (int index = applied.Count - 1; index >= 0; index--)
        {
            Change change = applied[index];
            string staged = change.Backup + ".restore";
            try
            {
                if (change.Existed)
                {
                    File.Copy(change.Backup, staged);
                    if (File.Exists(change.Target))
                        File.Replace(staged, change.Target, null);
                    else
                        File.Move(staged, change.Target);
                }
                else
                {
                    File.Delete(change.Target);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.Write($"Falha ao restaurar {change.Target}. Backup: {change.Backup}. {error.Message}");
            }
            finally
            {
                DeleteFile(staged);
            }
        }
    }

    private static void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Write($"Limpeza de arquivo {path}: {error.Message}");
        }
    }
}
