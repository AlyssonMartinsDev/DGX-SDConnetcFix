using Dgx.SdConnectFix.Collection;

namespace Dgx.SdConnectFix;

internal sealed class MainForm : Form
{
    private readonly Button install = new() { Text = "Install SD Connect Toolkit", AutoSize = true };
    private readonly Button fix = new() { Text = "Fix SD Connect Toolkit", AutoSize = true };
    private readonly XentryInstallation? installation;
    private readonly Label product = new() { AutoSize = true, ForeColor = DgxTheme.Text, Anchor = AnchorStyles.Right };
    private readonly Label version = new() { AutoSize = true, ForeColor = DgxTheme.Secondary, Anchor = AnchorStyles.Right };
    private bool busy;
    private readonly Label status = new() { Text = "Pronto para iniciar", AutoSize = true, MaximumSize = new Size(370, 0) };
    private readonly Label percentage = new() { Text = "0%", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly ProgressTrack progress = new() { Dock = DockStyle.Fill, Height = 9, Margin = new Padding(0) };

    public MainForm(XentryInstallation? installation, char[] password)
    {
        this.installation = installation;
        Text = "DGX - SD Connect Fix";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(630, 320);
        MinimumSize = new Size(530, 350);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = DgxTheme.Background;
        Font = new Font("Segoe UI", 10f);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(0, 0, 0, 18)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = Text, AutoSize = true, ForeColor = DgxTheme.Text,
            Font = new Font(Font.FontFamily, 17f, FontStyle.Bold),
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0)
        };
        header.Controls.Add(title, 0, 0);
        header.SetRowSpan(title, 2);
        header.Controls.Add(product, 1, 0);
        header.Controls.Add(version, 1, 1);
        layout.Controls.Add(header, 0, 0);

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, BackColor = DgxTheme.Surface,
            Padding = new Padding(16), Margin = new Padding(0), ColumnCount = 2, RowCount = 2
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        status.ForeColor = percentage.ForeColor = DgxTheme.Secondary;
        status.Margin = new Padding(0, 0, 8, 10);
        percentage.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(status, 0, 0);
        card.Controls.Add(percentage, 1, 0);
        card.Controls.Add(progress, 0, 1);
        card.SetColumnSpan(progress, 2);
        layout.Controls.Add(card, 0, 1);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0)
        };
        DgxTheme.StyleButton(install);
        DgxTheme.StyleButton(fix);
        install.Margin = new Padding(0, 0, 10, 0);
        fix.Margin = new Padding(0);
        actions.Controls.Add(install);
        actions.Controls.Add(fix);
        layout.Controls.Add(actions, 0, 2);
        Controls.Add(layout);
        UpdateInstallation();

        install.Click += async (_, _) =>
        {
            if (installation is not null)
                await RunAction(progress => ToolkitInstaller.Install(installation.RootDirectory,
                    installation.Edition == XentryEdition.DaimlerTruck, password, progress));
        };
        fix.Click += async (_, _) =>
        {
            if (installation is null)
                return;

            if (installation.Edition == XentryEdition.Unknown)
            {
                status.Text = "Não foi possível identificar o produto instalado.";
                DiagnosticLog.Write($"Fix indisponível: {installation.Edition} {installation.Version ?? "(sem versão)"} em {installation.RootDirectory}");
                return;
            }

            await RunAction(progress => ExecuteFix(installation, password, progress));
        };
    }

    private void UpdateInstallation()
    {
        product.Text = $"Produto  {EditionDisplayName(installation)}";
        version.Text = $"Versão  {installation?.Version ?? "—"}";

        status.Text = installation is null
            ? "Nenhuma instalação do XENTRY Cars ou Daimler Trucks encontrada."
            : "Pronto para iniciar";
        UpdateActions();
    }

    private void UpdateActions()
    {
        install.Enabled = !busy && installation is not null;
        fix.Enabled = !busy && installation is not null;
        install.BackColor = install.Enabled ? DgxTheme.Primary : DgxTheme.Track;
        fix.BackColor = fix.Enabled ? DgxTheme.Primary : DgxTheme.Track;
    }

    private static string EditionDisplayName(XentryInstallation? installation) => installation?.Edition switch
    {
        XentryEdition.DaimlerTruck => "Daimler Trucks",
        XentryEdition.MercedesBenz => "XENTRY Cars",
        _ => "—"
    };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy && e.CloseReason == CloseReason.UserClosing)
            e.Cancel = true;

        base.OnFormClosing(e);
    }

    private async Task RunAction(Action<Action<int>> action)
    {
        busy = true;
        UpdateActions();
        status.Text = "Em andamento…";
        SetProgress(0);
        IProgress<int> reporter = new Progress<int>(value =>
        {
            if (busy && value > progress.Value)
                SetProgress(value);
        });

        try
        {
            await Task.Run(() => action(reporter.Report));
            SetProgress(100);
            status.Text = "Concluído";
        }
        catch (Exception error)
        {
            DiagnosticLog.Write(error.ToString());
            status.Text = "Não foi possível concluir";
        }
        finally
        {
            busy = false;
            UpdateActions();
        }
    }

    private static void ExecuteFix(XentryInstallation installation, char[] password, Action<int> progress)
    {
        string directory = Path.Combine(installation.RootDirectory, "Xentry", "bin", "PDU64");
        switch (installation.Edition)
        {
            case XentryEdition.DaimlerTruck:
                TruckPackageFix.Apply(installation.RootDirectory, password, progress);
                break;
            case XentryEdition.MercedesBenz:
                FixResult result = new ProfileFix(directory, XentryProcesses.IsRunning).Apply(progress);
                DiagnosticLog.Write(result.Changed
                    ? $"Perfil aplicado. Backup: {result.BackupPath}"
                    : $"Perfil já aplicado. Backup: {result.BackupPath ?? "ausente"}");
                break;
            default:
                throw new InvalidOperationException("Não foi possível identificar o produto instalado.");
        }
    }

    private void SetProgress(int value)
    {
        progress.Value = value;
        percentage.Text = $"{value}%";
    }
}
