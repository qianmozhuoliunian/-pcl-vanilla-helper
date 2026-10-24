using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PclVersionBridge.Core.Friends;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.App.Friends;

/// <summary>Metadata is supplied exclusively by the trusted catalog callback, never by a peer.</summary>
public sealed class InvitationDetailsWindow : Window
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextBlock _title, _description, _status;
    private readonly ProjectImage _icon = new() { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Button _check;
    private readonly PackInvitation _invitation;
    private readonly Func<PackReference, CancellationToken, Task<ModpackDetails>>? _load;
    public InvitationDetailsWindow(Window? owner, PackInvitation invitation,
        Func<PackReference, CancellationToken, Task<ModpackDetails>>? load, bool canCheck)
    {
        _invitation = invitation; _load = load;
        Title = "整合包邀请 · 来源核对"; Width = 520; Height = 610; MinWidth = 400; MinHeight = 400;
        if (owner is not null) Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("AppBackground"); SetResourceReference(FontFamilyProperty,"AppFont"); FontSize=13; SetResourceReference(ForegroundProperty,"TextPrimary");
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(FriendsView.Text("来自 " + invitation.From.DisplayName + "（" + invitation.From.FriendCode + "）", "Hint", new Thickness(0, 0, 0, 12)));
        body.Children.Add(_icon);
        _title = FriendsView.Text("正在从 Modrinth 读取来源资料…", "SectionTitle"); body.Children.Add(_title);
        body.Children.Add(FriendsView.Text("来源：Modrinth · 项目与版本经来源核对后显示", "Hint", new Thickness(0, 6, 0, 12)));
        _description = FriendsView.Text("", "Hint"); body.Children.Add(_description);
        body.Children.Add(FriendsView.Text("项目 ID：" + invitation.Pack.ProjectId + "\n准确版本 ID：" + invitation.Pack.VersionId + "\nSHA-512：" + invitation.Pack.ArchiveSha512,
            "Hint", new Thickness(0, 12, 0, 12)));
        body.Children.Add(FriendsView.Text("只分享同一发布版本，不含朋友的本机存档、配置或自定义改动。查看并检查会重新确认卡片权限并进入现有预检；安装仍需你自行确认。", "Hint"));
        _status = FriendsView.Text("正在核对来源…", "Hint", new Thickness(0, 12, 0, 8));
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite); body.Children.Add(_status);
        _check = new Button { Content = "查看并检查", IsDefault = true, IsEnabled = false, Margin = new Thickness(0, 4, 8, 0), Visibility = canCheck ? Visibility.Visible : Visibility.Collapsed };
        _check.SetResourceReference(StyleProperty, "PrimaryButton");
        _check.Click += (_, _) => DialogResult = true;
        var close = new Button { Content = "关闭", IsCancel = true, Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(FriendsView.Buttons(_check, close));
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += async (_, _) => await LoadSourceAsync();
        Closed += (_, _) => _cancel.Cancel();
    }
    private async Task LoadSourceAsync()
    {
        if (_load is null) { _status.Text = "来源验证暂不可用，请关闭后重试。"; return; }
        try
        {
            var details = await _load(_invitation.Pack, _cancel.Token);
            _cancel.Token.ThrowIfCancellationRequested();
            if (details.Id != _invitation.Pack.ProjectId) { _status.Text = "来源项目不匹配，无法检查此邀请。"; return; }
            _title.Text = details.Title; _description.Text = details.DescriptionText;
            _icon.ProjectId = details.Id; _icon.SourceUri = details.RawIconUri ?? details.IconUri;
            _status.Text = "标题、图片和简介来自 Modrinth。卡片操作前仍将重新确认服务端权限。";
            _check.IsEnabled = _invitation.State == "pending" && _invitation.ExpiresAtUtc > DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) { if (!_cancel.IsCancellationRequested) _status.Text = "来源请求已取消，可关闭后重试。"; }
        catch { if (!_cancel.IsCancellationRequested) { _title.Text = "暂时无法获取这个准确版本"; _status.Text = "来源资料不可用、版本已失效或已不受支持。可关闭后重试。"; } }
    }
}
