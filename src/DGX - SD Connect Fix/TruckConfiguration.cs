using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Dgx.SdConnectFix;

internal static class TruckConfiguration
{
    internal const string GlobalSettings = "Xentry/XD_DT/bin/GlobalSystemSettings_install.properties";
    internal const string Services = "Xentry/XD_DT/bin/XentryServices.xml";
    internal const string AktServices = "Xentry/XD_DT/bin/AktServices.xml";
    internal const string XentryCal = "Xentry/XD_DT/bin/CAL.ini";
    internal const string DasCal = "DAS/bin/cal.ini";

    private const string LegacyService = "com.daimler.fusoko.services.legacyvci.LegacyVCIServiceImpl";
    private const string ToolkitService = "com.dcx.indiaes.service.sdconnect.toolkit.SDConnectServiceImpl";

    internal static byte[] Update(string path, byte[] original)
    {
        // For ASCII-compatible formats, Latin-1 preserves the original bytes.
        Encoding encoding = original.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })
            ? Encoding.Unicode
            : original.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })
                ? Encoding.BigEndianUnicode
                : Encoding.Latin1;
        string text = encoding.GetString(original);
        string updated = path switch
        {
            GlobalSettings => UpdateSettings(text),
            Services => UpdateService(text, original, Services),
            AktServices => UpdateService(text, original, AktServices),
            XentryCal => UpdateCal(text, enableToolkit: true),
            DasCal => UpdateCal(text, enableToolkit: false),
            _ => throw new ArgumentException("Configuração do Truck desconhecida.", nameof(path))
        };
        return encoding.GetBytes(updated);
    }

    private static string UpdateSettings(string text)
    {
        text = UpdateKey(text, "system.asam.asam3dini.nosim.CAESAR.Drivers", "T");
        text = UpdateKey(text, "system.asam.gal.vendor.nosim", "I+ME ACTIA GmbH");
        text = UpdateKey(text, "system.asam.caesar.vendor.nosim", "I+ME ACTIA GmbH");
        return UpdateKey(text, "system.asam.gal.longname.nosim", "");
    }

    private static string UpdateKey(string text, string key, string value, bool ini = false)
    {
        string expression = ini
            ? $@"(?mi)^([ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*)[^;\r\n]*?([ \t]*(?:;[^\r\n]*)?)(?=\r?$)"
            : $@"(?m)^([ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*)[^\r\n]*?([ \t]*)(?=\r?$)";
        return ReplaceUnique(text, expression,
            match => match.Groups[1].Value + value + match.Groups[2].Value, key);
    }

    private static string UpdateCal(string text, bool enableToolkit)
    {
        var section = new Regex(@"(?mi)^[ \t]*\[SERVER\][ \t]*(?:[;#][^\r\n]*)?\r?$");
        MatchCollection matches = section.Matches(text);
        if (matches.Count != 1)
            throw new InvalidDataException("Seção [SERVER] ausente ou duplicada no CAL.ini.");

        int start = matches[0].Index + matches[0].Length;
        Match next = new Regex(@"(?m)^[ \t]*\[").Match(text, start);
        int end = next.Success ? next.Index : text.Length;
        string updated = UpdateKey(text[start..end], "PLATFORM", "PART_T", ini: true);
        if (enableToolkit)
            updated = UpdateKey(updated, "TOOLKIT", "1", ini: true);
        return text[..start] + updated + text[end..];
    }

    private static string UpdateService(string text, byte[] original, string path)
    {
        using var stream = new MemoryStream(original, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        XDocument document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        XElement[] services = document.Descendants("service")
            .Where(node => (string?)node.Attribute("name") == "SDConnectService").ToArray();
        if (services.Length != 1)
            throw new InvalidDataException($"SDConnectService ausente ou duplicado em {path}.");

        XAttribute? implementor = services[0].Attribute("implementor");
        if (implementor?.Value == ToolkitService)
            return text;
        if (implementor?.Value != LegacyService)
            throw new InvalidDataException($"Implementação de SDConnectService desconhecida em {path}.");

        var location = (IXmlLineInfo)implementor;
        int start = 0;
        for (int line = 1; line < location.LineNumber; line++)
            start = text.IndexOf('\n', start) + 1;

        // Latin-1 maps UTF-8 bytes one by one, so use the attribute's line
        // instead of the XML parser's character-based column.
        int end = text.IndexOf('\n', start);
        if (end < 0)
            end = text.Length;
        string updated = ReplaceUnique(text[start..end],
            @"(\s+implementor\s*=\s*)(['""])" + Regex.Escape(LegacyService) + @"\2",
            match => match.Groups[1].Value + match.Groups[2].Value + ToolkitService + match.Groups[2].Value,
            "SDConnectService implementor");
        return text[..start] + updated + text[end..];
    }

    private static string ReplaceUnique(string text, string expression, MatchEvaluator replacement, string label)
    {
        var pattern = new Regex(expression, RegexOptions.CultureInvariant);
        MatchCollection matches = pattern.Matches(text);
        if (matches.Count != 1)
            throw new InvalidDataException($"Configuração ausente ou duplicada: {label}.");
        Match match = matches[0];
        return text[..match.Index] + replacement(match) + text[(match.Index + match.Length)..];
    }
}
