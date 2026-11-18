using System.Globalization;

namespace Dgx.SdConnectFix;

internal static class DiagnosticLog
{
    public static void Write(string message)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "DGX - SD Connect Fix");
            DirectoryInfo folder = Directory.CreateDirectory(directory);
            if ((folder.Attributes & FileAttributes.Hidden) == 0)
                folder.Attributes |= FileAttributes.Hidden;

            string timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            File.AppendAllText(Path.Combine(directory, "diagnostico.log"),
                $"[{timestamp}] {message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A logging failure does not change the repair result.
        }
    }
}
