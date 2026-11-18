using System.Diagnostics;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Dgx.SdConnectFix;

internal static class TruckPackageFix
{
    private static readonly string[] Configurations =
    [
        TruckConfiguration.GlobalSettings,
        TruckConfiguration.Services,
        TruckConfiguration.AktServices,
        TruckConfiguration.XentryCal,
        TruckConfiguration.DasCal
    ];

    private sealed record FileSnapshot(string Target, string? Backup);

    internal static void Apply(string installationRoot, char[] password, Action<int> progress)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        string packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Daimler-Truck");
        if (!string.Equals(packageRoot, @"C:\Program Files (x86)\Daimler-Truck", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(root, packageRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"O instalador Link C4 usa a pasta padrão {packageRoot}. Instalação detectada: {root}.");

        using var mutex = new Mutex(false, @"Global\Dgx.SdConnectFix.PduProfile");
        try
        {
            if (!mutex.WaitOne(0))
                throw new InvalidOperationException("Outra instância está aplicando o fix.");
        }
        catch (AbandonedMutexException)
        {
            // The mutex is acquired after an interrupted run.
        }

        try
        {
            EnsureClosed(XentryProcesses.IsRunning);
            EncryptedResource.Read("Dgx.SdConnectFix.Packages.LinkC4.07-2026", "DGXLNK01"u8, password, bytes =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                ApplyArchive(root, archive, XentryProcesses.IsRunning, ToolkitInstaller.RunAndWait, progress);
                return true;
            });
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    internal static void ApplyArchive(string root, ZipArchive archive, Func<bool> isXentryRunning,
        Action<ProcessStartInfo, string> runInstaller, Action<int> progress)
    {
        EnsureClosed(isXentryRunning);
        string pdu = Path.Combine(root, "Xentry", "bin", "PDU64");
        if (!Directory.Exists(pdu) || Configurations.Any(path => !Directory.Exists(Path.GetDirectoryName(Path.Combine(root, path)))))
            throw new DirectoryNotFoundException("A estrutura de configuração do XENTRY Truck não foi localizada.");

        string[] pduFiles = ReadText(archive, "pdu-files.txt")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pduFiles.Length != 166 || pduFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 166)
            throw new InvalidDataException("Manifesto PDU64 não reconhecido.");

        string installerPassword = ReadText(archive, "installer-password.txt");
        if (string.IsNullOrWhiteSpace(installerPassword))
            throw new InvalidDataException("Credencial do instalador ausente no pacote.");

        string id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string backupRoot = Path.Combine(root, "Xentry", "bin", "DGX-Backup", "Link-C4-" + id);
        string staging = Path.Combine(root, ".dgx-link-c4-" + id);
        string installer = Path.Combine(staging, "NP.exe");
        var pending = new List<string>();
        var snapshots = new List<FileSnapshot>();
        var modified = new List<FileSnapshot>();
        var timeline = new List<string>();
        bool installerStarted = false;

        void Note(string message) => timeline.Add($"[{DateTimeOffset.Now:O}] {message}");

        try
        {
            Note($"ANTES | LEITURA do pacote Link C4, configurações locais e manifesto de {pduFiles.Length} arquivos PDU64.");
            Note($"ANTES | Destino: {root}. Backup: {backupRoot}");
            Directory.CreateDirectory(staging);
            pending.Add(installer);
            GetEntry(archive, "NP.exe").ExtractToFile(installer);
            Note("ANTES | GRAVAÇÃO do instalador temporário autenticado.");

            var configurations = new List<(FileSnapshot Snapshot, string Staged)>();
            foreach (string relative in Configurations)
            {
                FileSnapshot snapshot = SaveOriginal(root, backupRoot, relative, Note);
                if (snapshot.Backup is null)
                {
                    if (relative == TruckConfiguration.AktServices)
                        continue;
                    throw new FileNotFoundException($"Configuração local ausente: {relative}", snapshot.Target);
                }

                byte[] original = File.ReadAllBytes(snapshot.Backup);
                byte[] updated = TruckConfiguration.Update(relative, original);
                if (original.AsSpan().SequenceEqual(updated))
                    continue;

                string staged = Path.Combine(staging, $"config-{configurations.Count}");
                pending.Add(staged);
                File.WriteAllBytes(staged, updated);
                configurations.Add((snapshot, staged));
            }

            foreach (string relative in pduFiles)
                snapshots.Add(SaveOriginal(root, backupRoot, "Xentry/bin/PDU64/" + relative, Note));

            progress(40);
            foreach ((FileSnapshot snapshot, string staged) in configurations)
            {
                EnsureClosed(isXentryRunning);
                if (!File.ReadAllBytes(snapshot.Target).AsSpan().SequenceEqual(File.ReadAllBytes(snapshot.Backup!)))
                    throw new IOException($"Configuração alterada durante a preparação: {snapshot.Target}");
                File.Replace(staged, snapshot.Target, null);
                modified.Add(snapshot);
                Note($"ANTES | GRAVAÇÃO {snapshot.Target}");
            }

            EnsureClosed(isXentryRunning);
            progress(60);
            var start = new ProcessStartInfo(installer) { UseShellExecute = false, WorkingDirectory = staging };
            start.ArgumentList.Add("/s");
            start.ArgumentList.Add("/pw=" + installerPassword);
            Note("EXE | Início do instalador original Link C4 em modo silencioso.");
            installerStarted = true;
            runInstaller(start, "Instalador Link C4");
            Note("DEPOIS | Instalador retornou código 0.");

            foreach (FileSnapshot snapshot in snapshots)
            {
                var file = new FileInfo(snapshot.Target);
                if (!file.Exists || file.Length == 0)
                    throw new InvalidDataException($"Arquivo esperado do PDU64 ausente ou vazio: {snapshot.Target}");
                Note($"DEPOIS | LEITURA de verificação {file.FullName} ({file.Length} bytes)");
            }

            ValidateSdconnectProfile(pdu);
            Note("DEPOIS | Perfil Bosch contém a DLL e os descritores ACTIA esperados.");
            Note("DEPOIS | Validação concluída. Backups preservados.");
            progress(95);
        }
        catch (Exception error)
        {
            Note($"FALHA | {error.Message}");
            if (installerStarted)
                RestoreFiles(snapshots, Note);
            RestoreFiles(modified, Note);
            throw;
        }
        finally
        {
            foreach (string file in pending)
            {
                try { File.Delete(file); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Note($"FALHA ao remover arquivo temporário {file}: {error.Message}");
                }
            }

            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Note($"FALHA ao remover pasta temporária {staging}: {error.Message}");
                }
            }

            DiagnosticLog.Write(string.Join(Environment.NewLine, timeline));
        }
    }

    private static FileSnapshot SaveOriginal(string root, string backupRoot, string relative, Action<string> note)
    {
        string target = Under(root, relative);
        if (!File.Exists(target))
        {
            note($"ANTES | LEITURA {target} (ausente)");
            return new FileSnapshot(target, null);
        }

        string backup = Under(backupRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(target, backup);
        note($"ANTES | LEITURA {target} | GRAVAÇÃO backup {backup}");
        return new FileSnapshot(target, backup);
    }

    private static string Under(string root, string relative)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Caminho fora da pasta esperada: {relative}");
        return fullPath;
    }

    private static void RestoreFiles(List<FileSnapshot> snapshots, Action<string> note)
    {
        for (int index = snapshots.Count - 1; index >= 0; index--)
        {
            FileSnapshot snapshot = snapshots[index];
            try
            {
                if (snapshot.Backup is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Target)!);
                    File.Copy(snapshot.Backup, snapshot.Target, overwrite: true);
                    note($"REVERSÃO | Restaurado {snapshot.Target}");
                }
                else if (File.Exists(snapshot.Target))
                {
                    File.Delete(snapshot.Target);
                    note($"REVERSÃO | Removido arquivo que não existia: {snapshot.Target}");
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                note($"FALHA na reversão {snapshot.Target}: {error.Message}");
            }
        }
    }

    private static void ValidateSdconnectProfile(string pdu)
    {
        string path = Path.Combine(pdu, "pdu_api_root_bosch.xml");
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        XDocument profile = XDocument.Load(reader);
        XElement? entry = profile.Root?.Elements("MVCI_PDU_API")
            .FirstOrDefault(node => string.Equals((string?)node.Element("SHORT_NAME"), "SDConnect", StringComparison.OrdinalIgnoreCase));
        if (!string.Equals((string?)entry?.Element("SUPPLIER_NAME"), "I+ME ACTIA GmbH", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O instalador não deixou a entrada ACTIA no perfil Bosch.");

        string directory = Path.Combine(pdu, "SDConnect");
        CheckProfileFile("LIBRARY_FILE", "PDUAPI_I+ME_ACTIA_XS.dll");
        CheckProfileFile("MODULE_DESCRIPTION_FILE", "MDF_I+ME_ACTIA_XS.xml");
        CheckProfileFile("CABLE_DESCRIPTION_FILE", "CDF_I+ME_ACTIA_XS.xml");

        void CheckProfileFile(string element, string file)
        {
            string expected = Path.Combine(directory, file);
            string? value = (string?)entry?.Element(element)?.Attribute("URI");
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !uri.IsFile
                || !string.Equals(uri.LocalPath, expected, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(expected))
                throw new InvalidDataException($"Arquivo ACTIA ausente ou caminho inválido no perfil: {expected}");
        }
    }

    private static ZipArchiveEntry GetEntry(ZipArchive archive, string name) =>
        archive.GetEntry(name) ?? throw new InvalidDataException($"Arquivo ausente no pacote Link C4: {name}");

    private static string ReadText(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(GetEntry(archive, name).Open());
        return reader.ReadToEnd();
    }

    private static void EnsureClosed(Func<bool> isXentryRunning)
    {
        if (isXentryRunning())
            throw new InvalidOperationException("Feche o XENTRY antes de aplicar a correção.");
    }
}
