using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PclVersionBridge.App.Friends;

/// <summary>Small, keyboard-accessible account/action forms. PasswordBox contents never leave the action.</summary>
internal sealed class FriendDialog : Window
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly StackPanel _fields = new();
    private readonly TextBlock _status;
    private readonly Button _submit;
    private readonly Dictionary<string, Control> _inputs = new();
    public CancellationToken Token => _cancel.Token;
    public StackPanel Fields => _fields;
    public FriendDialog(Window? owner, string title, string explanation, string submit,
        Func<FriendDialog, CancellationToken, Task<bool>> action)
    {
        Title = title; Width = 460; MinWidth = 360; MaxHeight = 720; MinHeight = 240;
        SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (owner is not null) Owner = owner;
        Background = (Brush)FindResource("AppBackground");
        SetResourceReference(FontFamilyProperty,"AppFont"); FontSize=13; SetResourceReference(ForegroundProperty,"TextPrimary");
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(FriendsView.Text(title, "SectionTitle"));
        body.Children.Add(FriendsView.Text(explanation, "Hint", new Thickness(0, 8, 0, 12)));
        body.Children.Add(FriendsView.Card(_fields));
        _status = FriendsView.Text("", "Hint", new Thickness(0, 8, 0, 8));
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        body.Children.Add(_status);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _submit = new Button { Content = submit, IsDefault = true, Margin = new Thickness(0, 4, 8, 0) };
        _submit.SetResourceReference(StyleProperty, "PrimaryButton");
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(_submit); buttons.Children.Add(cancel); body.Children.Add(buttons);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _submit.Click += async (_, _) =>
        {
            if (!_submit.IsEnabled) return;
            _submit.IsEnabled = false; _fields.IsEnabled = false; _status.Text = "正在处理…";
            try
            {
                if (await action(this, _cancel.Token)) { if (!_cancel.IsCancellationRequested) DialogResult = true; }
            }
            catch (OperationCanceledException) { if (!_cancel.IsCancellationRequested) _status.Text = "请求已取消，请重试。"; }
            catch { if (!_cancel.IsCancellationRequested) _status.Text = "操作未完成，请检查连接后重试。"; }
            finally { if (!_cancel.IsCancellationRequested) { _submit.IsEnabled = true; _fields.IsEnabled = true; } }
        };
        Closed += (_, _) => { _cancel.Cancel(); foreach (var password in _inputs.Values.OfType<PasswordBox>()) password.Clear(); };
        Loaded += (_, _) => _inputs.Values.FirstOrDefault()?.Focus();
    }
    public TextBox Input(string key, string label, string value = "", int maximum = 256)
    {
        var input = new TextBox { Text = value, MaxLength = maximum, Margin = new Thickness(0, 0, 0, 8) };
        LabelInput(key, label, input); return input;
    }
    public PasswordBox Password(string key, string label)
    {
        var input = new PasswordBox { MaxLength = 256, Margin = new Thickness(0, 0, 0, 8) };
        LabelInput(key, label, input); return input;
    }
    public CheckBox Check(string key, string label)
    {
        var input = new CheckBox { Content = label, Margin = new Thickness(0, 2, 0, 8) };
        _inputs[key] = input; _fields.Children.Add(input); return input;
    }
    private void LabelInput(string key, string label, Control input)
    {
        _inputs[key] = input;
        _fields.Children.Add(new Label { Content = label, Target = input, Padding = new Thickness(0, 4, 0, 4) });
        AutomationProperties.SetName(input, label); _fields.Children.Add(input);
    }
    public string Value(string key) => _inputs[key] is PasswordBox password ? password.Password : ((TextBox)_inputs[key]).Text.Trim();
    public bool Checked(string key) => ((CheckBox)_inputs[key]).IsChecked == true;
    public void Status(string status) => _status.Text = status;
}
