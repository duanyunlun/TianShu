using System.Globalization;

string? outPath = null;

for (var i = 0; i < args.Length; i++)
{
    var token = args[i];
    if (token.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        outPath = args[i + 1];
        i++;
        continue;
    }
}

ApplicationConfiguration.Initialize();

var form = new Form
{
    Text = "Windows.Agent UIA Test App",
    Width = 520,
    Height = 240,
    StartPosition = FormStartPosition.CenterScreen
};

var input = new TextBox
{
    Name = "txtInput",
    Left = 20,
    Top = 20,
    Width = 460
};

var btnInvoke = new Button
{
    Name = "btnInvoke",
    Text = "Invoke",
    Left = 20,
    Top = 60,
    Width = 120
};

var label = new Label
{
    Name = "lblResult",
    Text = "Idle",
    Left = 20,
    Top = 110,
    Width = 460
};

btnInvoke.Click += (_, _) =>
{
    label.Text = "Invoked";

    if (string.IsNullOrWhiteSpace(outPath))
    {
        return;
    }

    try
    {
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(outPath, input.Text ?? string.Empty);
    }
    catch (Exception ex)
    {
        label.Text = string.Create(CultureInfo.InvariantCulture, $"Write failed: {ex.Message}");
    }
};

form.Controls.Add(input);
form.Controls.Add(btnInvoke);
form.Controls.Add(label);

Application.Run(form);

