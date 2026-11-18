using System.Xml;
using System.Xml.Linq;

namespace Dgx.SdConnectFix;

internal static class PduProfilePaths
{
    private const string PduSegment = @"\Xentry\bin\PDU64\";

    internal static byte[] Rebase(byte[] content, string directory)
    {
        using var input = new MemoryStream(content);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit
        });
        XDocument profile = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (profile.Root?.Name != "MVCI_PDU_API_ROOT")
            throw new InvalidDataException("O arquivo não contém um perfil PDU reconhecido.");

        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))
            + Path.DirectorySeparatorChar;
        bool changed = false;
        foreach (XAttribute attribute in profile.Descendants().Attributes("URI"))
        {
            string reference = attribute.Value;
            if (!Uri.TryCreate(reference, UriKind.Absolute, out Uri? uri) || !uri.IsFile || uri.IsUnc)
                continue;

            string path = uri.LocalPath.Replace('/', '\\');
            int segment = path.IndexOf(PduSegment, StringComparison.OrdinalIgnoreCase);
            if (segment < 0 || path.StartsWith(destination, StringComparison.OrdinalIgnoreCase))
                continue;

            string relative = path[(segment + PduSegment.Length)..];
            string newPath = Path.GetFullPath(Path.Combine(destination, relative));
            if (!newPath.StartsWith(destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Referência de arquivo fora da pasta PDU64.");

            attribute.Value = reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                ? new Uri(newPath).AbsoluteUri
                : newPath.Replace('\\', '/');
            changed = true;
        }

        if (!changed)
            return content;

        using var output = new MemoryStream();
        profile.Save(output, SaveOptions.DisableFormatting);
        return output.ToArray();
    }
}
