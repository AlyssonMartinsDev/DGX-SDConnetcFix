using System.Security.Cryptography;

namespace Dgx.SdConnectFix;

internal sealed class PasswordDialog : Form
{
    private readonly TextBox password = new() { UseSystemPasswordChar = true, Dock = DockStyle.Top };
    private readonly Label status = new() { AutoSize = true, ForeColor = DgxTheme.Secondary };

    internal char[]? SessionPassword { get; private set; }

    internal PasswordDialog()
    {
        Text = "DGX - SD Connect Fix";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 244);
        BackColor = DgxTheme.Background;
        ForeColor = DgxTheme.Text;
        Font = new Font("Segoe UI", 10f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Digite a senha",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 17f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        });

        AddPasswordField(layout, "Senha", password);

        var submit = new Button { Text = "Desbloquear", AutoSize = true };
        DgxTheme.StyleButton(submit);
        submit.Margin = new Padding(0, 5, 0, 8);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        var cancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        cancel.FlatStyle = FlatStyle.Flat;
        cancel.FlatAppearance.BorderColor = DgxTheme.Track;
        cancel.ForeColor = DgxTheme.Text;
        cancel.Margin = new Padding(8, 5, 0, 8);
        cancel.Padding = new Padding(12, 7, 12, 7);
        actions.Controls.Add(submit);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions);
        layout.Controls.Add(status);
        Controls.Add(layout);

        AcceptButton = submit;
        CancelButton = cancel;
        ActiveControl = password;
        submit.Click += (_, _) => Submit();
    }

    private static void AddPasswordField(TableLayoutPanel layout, string label, TextBox field)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        field.BackColor = DgxTheme.Surface;
        field.ForeColor = DgxTheme.Text;
        field.AccessibleName = label;
        field.Margin = new Padding(0, 0, 0, 9);
        layout.Controls.Add(field);
    }

    private void Submit()
    {
        try
        {
            if (!PasswordVerifier.Matches(password.Text))
            {
                status.Text = "Senha incorreta.";
                password.Clear();
                password.Focus();
                return;
            }

            SessionPassword = password.Text.ToCharArray();
            password.Clear();
            DialogResult = DialogResult.OK;
        }
        catch (CryptographicException)
        {
            status.Text = "Não foi possível validar o acesso.";
        }
    }
}
