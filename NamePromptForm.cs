namespace LootChatReader;

internal sealed class NamePromptForm : Form
{
    private readonly TextBox _nameBox = new();

    public string EnteredName => _nameBox.Text.Trim();

    public NamePromptForm(string title, string initialName)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(360, 92);
        Font = new Font("Segoe UI", 9F);

        _nameBox.Text = initialName;
        _nameBox.Location = new Point(12, 12);
        _nameBox.Width = 336;

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(192, 52),
            Width = 75
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(273, 52),
            Width = 75
        };
        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.Add(_nameBox);
        Controls.Add(okButton);
        Controls.Add(cancelButton);
        Shown += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
        FormClosing += (_, eventArgs) =>
        {
            if (DialogResult != DialogResult.OK || EnteredName.Length > 0)
            {
                return;
            }

            eventArgs.Cancel = true;
            System.Media.SystemSounds.Beep.Play();
            _nameBox.Focus();
        };
    }
}
