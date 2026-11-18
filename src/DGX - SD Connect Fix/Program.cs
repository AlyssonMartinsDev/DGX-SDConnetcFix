using System.Security;
using Dgx.SdConnectFix.Collection;

namespace Dgx.SdConnectFix;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            Run();
        }
        catch (Exception error)
        {
            DiagnosticLog.Write($"Falha ao abrir o DGX: {error}");
            MessageBox.Show("Não foi possível abrir o DGX. Consulte o log de diagnóstico.",
                "DGX - SD Connect Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    private static void Run()
    {
        ApplicationConfiguration.Initialize();
        char[] password;
        using (var login = new PasswordDialog())
        {
            if (login.ShowDialog() != DialogResult.OK || login.SessionPassword is not char[] authenticated)
                return;

            password = authenticated;
        }

        try
        {
            XentryInstallation? installation;
            try
            {
                installation = XentryInstallationDetector.Detect();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
            {
                DiagnosticLog.Write(error.ToString());
                MessageBox.Show("Não foi possível verificar a instalação do XENTRY.", "DGX - SD Connect Fix",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm(installation, password));
        }
        finally
        {
            Array.Clear(password);
        }
    }
}
