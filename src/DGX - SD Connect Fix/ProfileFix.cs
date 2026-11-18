using System.Xml;
using System.Xml.Linq;

namespace Dgx.SdConnectFix;

internal sealed record FixResult(bool Changed, string? BackupPath);

internal sealed class ProfileFix(string directory, Func<bool> isXentryRunning)
{
    private const string SdconnectProfile = "pdu_api_root_sdconnect.xml";
    private const string BoschProfile = "pdu_api_root_bosch.xml";
    private const string Actia = "I+ME ACTIA GmbH";

    public FixResult Apply(Action<int>? reportProgress = null)
    {
        EnsureXentryClosed();
        reportProgress?.Invoke(20);
        string sourcePath = Path.Combine(directory, SdconnectProfile);
        string targetPath = Path.Combine(directory, BoschProfile);
        string backupPath = targetPath + ".bak";
        string libraryPath = Path.Combine(directory, "SDConnect", "PDUAPI_I+ME_ACTIA_XS.dll");

        byte[] source = PduProfilePaths.Rebase(ReadRequiredFile(sourcePath), directory);
        ValidateSdconnectProfile(source, libraryPath);
        if (!File.Exists(libraryPath))
            throw new FileNotFoundException($"DLL do SDconnect ausente: {libraryPath}");

        reportProgress?.Invoke(55);

        // The lock serializes repair attempts from different instances of this tool.
        using var mutex = new Mutex(false, @"Global\Dgx.SdConnectFix.PduProfile");
        try
        {
            if (!mutex.WaitOne(0))
                throw new InvalidOperationException("Outra instância está aplicando o fix.");
        }
        catch (AbandonedMutexException)
        {
            // The previous instance exited. Re-read the files under the acquired lock.
        }

        try
        {
            byte[] original = ReadRequiredFile(targetPath);
            bool hasBackup = File.Exists(backupPath);
            if (original.AsSpan().SequenceEqual(source))
                return new FixResult(false, hasBackup ? backupPath : null);

            ValidateBoschProfile(original);
            if (hasBackup)
                throw new InvalidOperationException(
                    $"Backup já existe. Nenhum arquivo foi alterado: {backupPath}");

            reportProgress?.Invoke(80);
            ReplaceProfile(source, targetPath, backupPath);
            return new FixResult(true, backupPath);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private void ReplaceProfile(byte[] content, string targetPath, string newBackupPath)
    {
        string stagedPath = Path.Combine(directory, $".sdconnect-fix-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            EnsureXentryClosed();
            File.Replace(stagedPath, targetPath, newBackupPath);
        }
        finally
        {
            File.Delete(stagedPath);
        }
    }

    private void EnsureXentryClosed()
    {
        if (isXentryRunning())
            throw new InvalidOperationException("O XENTRY está aberto. Feche-o e clique em Fix SDconnect novamente.");
    }

    private static byte[] ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Arquivo necessário ausente: {path}");

        return File.ReadAllBytes(path);
    }

    private static IEnumerable<XElement> ReadEntries(byte[] content)
    {
        using var input = new MemoryStream(content);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        XElement? root = XDocument.Load(reader).Root;
        if (root?.Name != "MVCI_PDU_API_ROOT")
            throw new InvalidOperationException("O arquivo não contém uma configuração PDU reconhecida.");

        return root.Elements("MVCI_PDU_API");
    }

    private static void ValidateSdconnectProfile(byte[] content, string libraryPath)
    {
        XElement? entry = ReadEntries(content).FirstOrDefault(item =>
            ValueIs(item, "SHORT_NAME", "SDConnect") && ValueIs(item, "SUPPLIER_NAME", Actia));
        string? libraryUri = (string?)entry?.Element("LIBRARY_FILE")?.Attribute("URI");
        if (!Uri.TryCreate(libraryUri, UriKind.Absolute, out Uri? uri) || !uri.IsFile
            || !string.Equals(uri.LocalPath, libraryPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O perfil SDconnect não declara a entrada ACTIA com o caminho esperado da DLL.");
        }
    }

    private static void ValidateBoschProfile(byte[] content)
    {
        XElement[] entries = ReadEntries(content).ToArray();
        if (!entries.Any(item => ValueIs(item, "SHORT_NAME", "D_PDU_API_Bosch_MTS6532"))
            || entries.Any(item => ValueIs(item, "SUPPLIER_NAME", Actia)))
        {
            throw new InvalidOperationException("O arquivo de destino difere do perfil Bosch original identificado para esta correção.");
        }
    }

    private static bool ValueIs(XElement entry, string name, string expected) =>
        string.Equals(((string?)entry.Element(name))?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
