using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PclVersionBridge.Core.Friends;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.App.Friends;

/// <summary>Optional account and consent-based friends. Contains no sample users or presence simulation.</summary>
public sealed class FriendsView : UserControl, IDisposable
{
    private readonly StackPanel _body = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _status = Text("", "Hint", new Thickness(0, 8, 0, 8));
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource _lifetime = new();
    private FriendsViewModel? _vm;
    private string _configurationStatus = "尚未配置好友服务。原版安装和整合包检索可继续使用。";
    private bool _development, _optedIn, _active, _disposed, _running;
    private int _tab;
    private PackReference? _shareCandidate;
    private string _shareCandidateLabel = "当前选择的整合包版本";
    public FriendsViewModel? ViewModel => _vm;
    public event Action<PackInvitation>? InvitationRequested;
    public Func<PackReference, CancellationToken, Task<ModpackDetails>>? InvitationSourceLoader { get; set; }
    public string ShareCandidateLabel { get => _shareCandidateLabel; set { _shareCandidateLabel = value; if (!_disposed) Render(); } }

    public FriendsView()
    {
        SetResourceReference(FontFamilyProperty,"AppFont"); FontSize=13;
        SetResourceReference(ForegroundProperty,"TextPrimary");
        var content = new Grid(); content.RowDefinitions.Add(new RowDefinition()); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var statusCard=new Border {Child=_status,Padding=new Thickness(14,10,14,10),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,12,0,0)};
        statusCard.SetResourceReference(BackgroundProperty,"SurfaceInset");
        Grid.SetRow(statusCard, 1); content.Children.Add(statusCard); Content = content;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _poll.Tick += Poll;
        IsVisibleChanged += VisibilityChanged;
        Render();
    }
    public void Initialize(FriendsViewModel? viewModel, string configurationStatus, bool isDevelopment)
    {
        if (_vm is not null) _vm.PropertyChanged -= Changed;
        _vm = viewModel; _configurationStatus = configurationStatus; _development = isDevelopment;
        if (_vm is not null) _vm.PropertyChanged += Changed;
        Render(); UpdatePolling();
    }
    public void SetShareCandidate(PackReference? candidate) { _shareCandidate = candidate; if (!_disposed) Render(); }
    public void SetActive(bool active)
    {
        if (_disposed) return;
        var entering = active && !_active; _active = active; UpdatePolling();
        if (entering && _optedIn && _vm is not null) _ = Run(t => _vm.RefreshAsync(t));
    }
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdatePolling();
    private void UpdatePolling()
    {
        if (!_disposed && _active && IsVisible && _optedIn && _vm?.IsLoggedIn == true) _poll.Start(); else _poll.Stop();
    }
    private async void Poll(object? sender, EventArgs e)
    { if (_vm is not null && _active && IsVisible && !_vm.IsBusy) await Run(t => _vm.RefreshAsync(t)); }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Changed(sender, e)); return; }
        if (e.PropertyName is "Status" or "Error" or "IsBusy") UpdateStatus(); else Render();
        UpdatePolling();
    }
    private void UpdateStatus()
    {
        _status.Text = _vm is null ? _configurationStatus : !string.IsNullOrWhiteSpace(_vm.Error) ? _vm.Error + "\n" + _vm.Status : _vm.IsBusy ? "正在连接好友服务…" : _vm.Status;
        if (_vm?.IsLoggedIn == true && _vm.LastSynchronizedAtUtc is { } synchronized)
            _status.Text += "\n上次同步：" + synchronized.ToLocalTime().ToString("g");
        _status.SetResourceReference(ForegroundProperty, string.IsNullOrWhiteSpace(_vm?.Error) ? "TextSecondary" : "ErrorText");
        _body.IsEnabled = !_running && _vm?.IsBusy != true;
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (_running || _disposed || _vm?.IsBusy == true) return;
        _running = true; UpdateStatus();
        try { await action(_lifetime.Token); }
        catch (OperationCanceledException) { }
        catch { if (!_disposed) _status.Text = "好友操作未完成，请检查连接后重试。"; }
        finally { _running = false; if (!_disposed) { Render(); UpdatePolling(); } }
    }
    private void Render()
    {
        if (_disposed) return;
        _body.Children.Clear();
        _body.Children.Add(Text("好友", "SectionTitle"));
        _body.Children.Add(Text(_configurationStatus, "Hint", new Thickness(0, 6, 0, 8)));
        if (_development) _body.Children.Add(Text("本机开发测试服务：邮件在开发收件箱中查看，不代表已部署的公共好友服务。", "Hint", new Thickness(0, 0, 0, 8)));
        if (!_optedIn)
        {
            _body.Children.Add(Card(Text("和认识的朋友互相确认，再分享同一发布版本的整合包。好友功能使用独立账号；不用好友时可直接安装游戏。", "Hint")));
            var enable = Button("启用好友", async () => { _optedIn = true; Render(); if (_vm is not null) await Run(t => _vm.InitializeAsync(t)); });
            enable.SetResourceReference(StyleProperty,"PrimaryButton");
            enable.IsEnabled = _vm is not null; _body.Children.Add(Buttons(enable)); UpdateStatus(); return;
        }
        if (_vm is null) { _body.Children.Add(Text("好友服务未配置，暂时无法注册、登录或同步。", "Hint")); UpdateStatus(); return; }
        if (!_vm.IsLoggedIn)
        {
            _body.Children.Add(Card(Text("启用好友需要邮箱、密码和昵称。请使用原版直达自己的账号，不要输入 Microsoft、Minecraft 或 PCL 的密码。", "Hint")));
            _body.Children.Add(Buttons(Button("登录", Login), Button("注册账号", Register)));
            _body.Children.Add(Buttons(Button("验证邮箱", ConfirmEmail), Button("找回 / 重置密码", ResetPassword)));
            UpdateStatus(); return;
        }
        var me = _vm.Me!;
        _body.Children.Add(Card(new StackPanel { Children = { Text(me.DisplayName, "SectionTitle"), Text("我的好友码：" + me.FriendCode, "Hint", new Thickness(0, 4, 0, 8)),
            Buttons(Button("复制好友码", () => Copy(me.FriendCode)), Button("添加好友", AddFriend), Button("账号", Account)) } }));
        _body.Children.Add(Buttons(Button("刷新", async () => await Run(t => _vm.RefreshAsync(t)))));
        var tabs = new TabControl { Margin = new Thickness(0, 8, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        tabs.Items.Add(new TabItem { Header = "好友", Content = FriendList() });
        tabs.Items.Add(new TabItem { Header = "申请", Content = RequestList() });
        tabs.Items.Add(new TabItem { Header = "整合包邀请", Content = InvitationList() });
        tabs.Items.Add(new TabItem { Header = "已拉黑", Content = BlockList() });
        tabs.SelectedIndex = _tab;
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) _tab = tabs.SelectedIndex; };
        _body.Children.Add(tabs); UpdateStatus();
    }
    private FrameworkElement FriendList()
    {
        var list = ListPanel();
        if (_vm!.Friends.Count == 0) list.Children.Add(Text("还没有好友。复制你的好友码发给朋友，或输入朋友的好友码。", "Hint"));
        foreach (var friend in _vm.Friends)
        {
            var row = new StackPanel(); row.Children.Add(Text(friend.DisplayName, "SectionTitle"));
            row.Children.Add(Text("好友码：" + friend.FriendCode, "Hint"));
            if (_vm.Notes.TryGetValue(friend.UserId, out var note) && !string.IsNullOrWhiteSpace(note)) row.Children.Add(Text("本机备注：" + note, "Hint"));
            row.Children.Add(Buttons(Button("查看", () => FriendDetails(friend)))); list.Children.Add(Card(row));
        }
        list.Children.Add(Paging(_vm.FriendsPage, _vm.FriendsTotal, p => _vm.LoadFriendsAsync(p, _lifetime.Token))); return list;
    }
    private FrameworkElement RequestList()
    {
        var list = ListPanel();
        if (_vm!.Requests.Count == 0) list.Children.Add(Text("没有好友申请。对方明确同意后才会成为好友。", "Hint"));
        foreach (var request in _vm.Requests)
        {
            var incoming = request.To.UserId == _vm.Me!.UserId; var person = incoming ? request.From : request.To;
            var row = new StackPanel(); row.Children.Add(Text((incoming ? "收到：" : "发给：") + person.DisplayName, "SectionTitle"));
            row.Children.Add(Text("好友码：" + person.FriendCode + " · " + State(request.State), "Hint"));
            row.Children.Add(Text("有效期至 " + request.ExpiresAtUtc.ToLocalTime().ToString("g"), "Hint"));
            if (request.State == "pending" && request.ExpiresAtUtc > DateTimeOffset.UtcNow)
                row.Children.Add(incoming ? Buttons(Button("同意", async () => await Run(t => _vm.ResolveRequestAsync(request.Id, "accept", t))), Button("拒绝", async () => await Run(t => _vm.ResolveRequestAsync(request.Id, "reject", t))))
                    : Buttons(Button("撤回申请", async () => await Run(t => _vm.ResolveRequestAsync(request.Id, "withdraw", t)))));
            list.Children.Add(Card(row));
        }
        list.Children.Add(Paging(_vm.RequestsPage, _vm.RequestsTotal, p => _vm.LoadRequestsAsync(p, _lifetime.Token))); return list;
    }
    private FrameworkElement InvitationList()
    {
        var list = ListPanel();
        list.Children.Add(Text("收到的卡片只会在你主动查看后进入检查。同一发布版本不保证本机配置或服务器兼容性完全相同。", "Hint"));
        if (_vm!.Invitations.Count == 0) list.Children.Add(Text("没有整合包邀请。在整合包页选择准确版本后，可向已确认的好友发送。", "Hint", new Thickness(0, 12, 0, 0)));
        foreach (var invitation in _vm.Invitations)
        {
            var incoming = invitation.To.UserId == _vm.Me!.UserId; var person = incoming ? invitation.From : invitation.To;
            var row = new StackPanel(); row.Children.Add(Text((incoming ? "来自：" : "发给：") + person.DisplayName, "SectionTitle"));
            row.Children.Add(Text("来源：Modrinth · 项目 " + invitation.Pack.ProjectId + " · 版本 " + invitation.Pack.VersionId, "Hint"));
            row.Children.Add(Text(State(invitation.State) + " · 有效期至 " + invitation.ExpiresAtUtc.ToLocalTime().ToString("g"), "Hint"));
            if (invitation.State == "pending" && invitation.ExpiresAtUtc > DateTimeOffset.UtcNow)
                row.Children.Add(incoming ? Buttons(Button("查看并检查", () => ShowInvitation(invitation)), Button("忽略", async () => await Run(t => _vm.ResolveInvitationAsync(invitation.Id, "ignore", t))))
                    : Buttons(Button("查看来源", () => ShowInvitation(invitation)), Button("撤回邀请", async () => await Run(t => _vm.ResolveInvitationAsync(invitation.Id, "withdraw", t)))));
            list.Children.Add(Card(row));
        }
        list.Children.Add(Paging(_vm.InvitationsPage, _vm.InvitationsTotal, p => _vm.LoadInvitationsAsync(p, _lifetime.Token))); return list;
    }
    private FrameworkElement BlockList()
    {
        var list = ListPanel();
        if (_vm!.Blocks.Count == 0) list.Children.Add(Text("没有已拉黑的账号。", "Hint"));
        foreach (var person in _vm.Blocks)
            list.Children.Add(Card(new StackPanel { Children = { Text(person.DisplayName, "SectionTitle"), Text(person.FriendCode, "Hint"), Buttons(Button("解除拉黑", async () =>
                { if (Confirm("解除拉黑不会恢复好友关系或旧邀请。", "解除拉黑")) await Run(t => _vm.UnblockAsync(person.UserId, t)); })) } }));
        list.Children.Add(Paging(_vm.BlocksPage, _vm.BlocksTotal, p => _vm.LoadBlocksAsync(p, _lifetime.Token))); return list;
    }
    private FrameworkElement Paging(int page, int total, Func<int, Task> load)
    {
        var previous = Button("上一页", async () => await Run(_ => load(page - 1))); previous.IsEnabled = page > 0;
        var next = Button("下一页", async () => await Run(_ => load(page + 1))); next.IsEnabled = (page + 1) * 20 < total;
        return new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { Text($"第 {page + 1} 页 · 共 {total} 项", "Hint"), Buttons(previous, next) } };
    }
    private void Login()
    {
        var dialog = Form("登录好友账号", "仅用于原版直达好友服务。记住登录使用 Windows 当前用户加密保护。", "登录",
            (d, t) => _vm!.LoginAsync(new(d.Value("email"), d.Value("password")), d.Checked("remember"), t));
        dialog.Input("email", "邮箱"); dialog.Password("password", "密码"); dialog.Check("remember", "在这台电脑记住登录"); dialog.ShowDialog();
    }
    private void Register()
    {
        var dialog = Form("注册好友账号", "注册后请从邮箱取得验证码并验证。昵称可重复，好友身份以完整好友码核对。密码建议至少 12 位，包含大小写字母、数字和符号。", "注册",
            (d, t) => _vm!.RegisterAsync(new(d.Value("email"), d.Value("password"), d.Value("name")), t));
        dialog.Input("email", "邮箱"); dialog.Input("name", "昵称", maximum: 40); dialog.Password("password", "密码"); dialog.ShowDialog();
    }
    private void ConfirmEmail()
    {
        var dialog = Form("验证邮箱", "输入邮箱收到的验证码。开发测试模式请查看服务目录中的开发收件箱。", "验证",
            (d, t) => _vm!.ConfirmEmailAsync(new(d.Value("email"), d.Value("code")), t));
        var email = dialog.Input("email", "邮箱"); dialog.Input("code", "验证码", maximum: 4096);
        dialog.Fields.Children.Add(Button("重新发送验证邮件", async () =>
        {
            if (_vm!.IsBusy) return; await _vm.ResendConfirmationAsync(new(email.Text.Trim()), dialog.Token);
            dialog.Status(string.IsNullOrWhiteSpace(_vm.Error) ? _vm.Status : _vm.Error);
        })); dialog.ShowDialog();
    }
    private void ResetPassword()
    {
        var choice = new FriendDialog(Window.GetWindow(this), "找回密码", "先请求重置邮件；已收到验证码时，选择“输入验证码重置”。", "请求重置邮件", async (d, t) =>
        { await _vm!.ForgotPasswordAsync(new(d.Value("email")), t); d.Status(string.IsNullOrWhiteSpace(_vm.Error) ? _vm.Status : _vm.Error); return false; });
        choice.Input("email", "邮箱"); choice.Fields.Children.Add(Button("输入验证码重置", () =>
        {
            var reset = Form("重置密码", "请使用重置邮件中的验证码。完成后重新登录。", "重置密码", (d, t) => _vm!.ResetPasswordAsync(new(d.Value("email"), d.Value("code"), d.Value("password")), t));
            reset.Input("email", "邮箱", choice.Value("email")); reset.Input("code", "重置验证码", maximum: 4096); reset.Password("password", "新密码"); reset.ShowDialog();
        })); choice.ShowDialog();
    }
    private void AddFriend()
    {
        FriendProfile? found = null;
        var dialog = new FriendDialog(Window.GetWindow(this), "添加好友", "输入完整好友码，查找后核对昵称与完整好友码。发送申请后需等待对方同意。", "查找并核对", async (d, t) =>
        {
            if (_vm!.IsBusy) { d.Status("另一项好友操作正在进行，请稍后重试。"); return false; }
            found = null;
            await _vm.LookupAsync(d.Value("code"), t);
            if (string.IsNullOrWhiteSpace(_vm.Error)) found = _vm.LookupResult;
            d.Status(!string.IsNullOrWhiteSpace(_vm.Error) ? _vm.Error : found is null ? "暂时无法向该好友码发送申请。" : $"昵称：{found.DisplayName}\n好友码：{found.FriendCode}\n请核对后点击发送申请。"); return false;
        });
        var input = dialog.Input("code", "好友码", maximum: 32);
        var send = Button("发送申请", async () =>
        {
            if (found is null || _vm!.IsBusy) { dialog.Status("请先查找并核对好友码。"); return; }
            var target = found;
            await _vm.SendRequestAsync(target.FriendCode, dialog.Token);
            dialog.Status(string.IsNullOrWhiteSpace(_vm.Error) ? "申请已发送，等待对方同意。" : _vm.Error);
            if (string.IsNullOrWhiteSpace(_vm.Error)) found = null;
        });
        input.TextChanged += (_, _) => found = null;
        dialog.Fields.Children.Add(send); dialog.ShowDialog();
    }
    private void FriendDetails(FriendProfile person)
    {
        var dialog = new FriendDialog(Window.GetWindow(this), person.DisplayName, "好友码：" + person.FriendCode + "\n备注仅保存在当前电脑，不会上传。", "保存本机备注", async (d, t) =>
        { await _vm!.SetNoteAsync(person.UserId, d.Value("note"), t); d.Status(string.IsNullOrWhiteSpace(_vm.Error) ? "本机备注已保存。" : _vm.Error); return false; });
        dialog.Input("note", "本机备注（最多 200 字）", _vm!.Notes.GetValueOrDefault(person.UserId) ?? "", 200);
        var share = Button("发送整合包", () => Share(person)); share.IsEnabled = _shareCandidate is not null;
        share.ToolTip = _shareCandidate is null ? "请先在整合包页选择一个受支持的准确版本。" : _shareCandidateLabel;
        dialog.Fields.Children.Add(Buttons(share, Button("删除好友", async () =>
        { if (Confirm("双方好友关系将解除，旧的待处理整合包邀请会失效。", "删除好友")) { await _vm.RemoveFriendAsync(person.UserId, dialog.Token); dialog.Status(CurrentMessage()); if (string.IsNullOrWhiteSpace(_vm.Error)) dialog.Close(); } }),
            Button("拉黑", async () =>
            { if (Confirm("将解除好友关系，撤销双方待处理申请和邀请，并阻止双向新交互。", "拉黑账号")) { await _vm.BlockAsync(person.UserId, dialog.Token); dialog.Status(CurrentMessage()); if (string.IsNullOrWhiteSpace(_vm.Error)) dialog.Close(); } })));
        dialog.ShowDialog();
    }
    private void Share(FriendProfile person)
    {
        var pack = _shareCandidate; if (pack is null) return;
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var dialog = Form("发送整合包邀请", "发给：" + person.DisplayName + "（" + person.FriendCode + "）\n" + _shareCandidateLabel + "\n来源：Modrinth\n项目：" + pack.ProjectId + "\n版本：" + pack.VersionId + "\n卡片只分享来源中的准确发布版本，不包含本机存档、配置或自定义改动。", "确认发送",
            (_, t) => _vm!.SendInvitationAsync(new(person.UserId, pack, idempotencyKey), t));
        dialog.ShowDialog();
    }
    private void ShowInvitation(PackInvitation invitation)
    {
        var canCheck = invitation.To.UserId == _vm?.Me?.UserId;
        var dialog = new InvitationDetailsWindow(Window.GetWindow(this), invitation, InvitationSourceLoader, canCheck);
        if (dialog.ShowDialog() == true && !_disposed) InvitationRequested?.Invoke(invitation);
    }
    private void Account()
    {
        var dialog = new FriendDialog(Window.GetWindow(this), "好友账号", "昵称和好友码是公开资料。邮箱不向好友公开。退出会清除本机的会话和当前名单。", "保存昵称", async (d, t) =>
        { await _vm!.UpdateProfileAsync(new(d.Value("name")), t); d.Status(CurrentMessage()); return false; });
        dialog.Input("name", "昵称", _vm!.Me!.DisplayName, 40);
        dialog.Fields.Children.Add(Buttons(Button("重置好友码", async () =>
        { if (Confirm("旧好友码立即失效，现有好友关系保留。", "重置好友码")) { await _vm.ResetFriendCodeAsync(dialog.Token); dialog.Status(CurrentMessage() + (_vm.Me is { } profile ? "\n当前好友码：" + profile.FriendCode : "")); } }), Button("修改密码", () => { ChangePassword(); if (!_vm.IsLoggedIn) dialog.Close(); })));
        dialog.Fields.Children.Add(Buttons(Button("退出登录", async () => { await _vm.LogoutAsync(dialog.Token); if (!_vm.IsLoggedIn) dialog.Close(); else dialog.Status(CurrentMessage()); }),
            Button("退出所有设备", async () => { if (Confirm("所有设备的好友会话将被撤销。", "退出所有设备")) { await _vm.LogoutAllAsync(dialog.Token); if (!_vm.IsLoggedIn) dialog.Close(); else dialog.Status(CurrentMessage()); } }), Button("删除账号", () => { DeleteAccount(); if (!_vm.IsLoggedIn) dialog.Close(); })));
        dialog.ShowDialog();
    }
    private void ChangePassword()
    {
        var dialog = Form("修改密码", "修改成功后所有设备的会话将撤销，需要重新登录。", "修改密码", (d, t) => _vm!.ChangePasswordAsync(new(d.Value("old"), d.Value("new")), t));
        dialog.Password("old", "当前密码"); dialog.Password("new", "新密码"); dialog.ShowDialog();
    }
    private void DeleteAccount()
    {
        if (!Confirm("账号、好友关系、申请和邀请将删除。此操作不可撤销。", "删除好友账号")) return;
        var dialog = Form("删除好友账号", "再次输入好友账号密码确认删除。不会删除你已经安装的游戏。", "永久删除账号", (d, t) => _vm!.DeleteAccountAsync(new(d.Value("password")), t));
        dialog.Password("password", "好友账号密码"); dialog.ShowDialog();
    }
    private FriendDialog Form(string title, string description, string submit, Func<FriendDialog, CancellationToken, Task> action) =>
        new(Window.GetWindow(this), title, description, submit, async (dialog, token) =>
        {
            if (_vm?.IsBusy == true) { dialog.Status("另一项好友操作正在进行，请稍后重试。"); return false; }
            await action(dialog, token);
            if (!string.IsNullOrWhiteSpace(_vm?.Error)) { dialog.Status(_vm.Error); return false; }
            return true;
        });
    private bool Confirm(string message, string title) => MessageBox.Show(Window.GetWindow(this), message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
    private string CurrentMessage() => string.IsNullOrWhiteSpace(_vm?.Error) ? _vm?.Status ?? "" : _vm.Error;
    private void Copy(string value)
    { try { Clipboard.SetText(value); _status.Text = "好友码已复制。"; } catch { _status.Text = "暂时无法复制，请手动选择好友码。"; } }
    internal static TextBlock Text(string value, string style = "Hint", Thickness? margin = null)
    { var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) }; text.SetResourceReference(StyleProperty, style); return text; }
    private static StackPanel ListPanel() => new() { Margin = new Thickness(0,4,8,8) };
    internal static Border Card(UIElement content) { var border = new Border { Child = content, Padding = new Thickness(16), Margin = new Thickness(0, 12, 0, 0) }; border.SetResourceReference(StyleProperty,"SurfaceCard"); return border; }
    internal static WrapPanel Buttons(params Button[] buttons) { var panel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; foreach (var button in buttons) panel.Children.Add(button); return panel; }
    internal static Button Button(string title, Action action) { var button = new Button { Content = title, Margin = new Thickness(0, 4, 8, 0) }; button.Click += (_, _) => action(); return button; }
    internal static Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 4, 8, 0) };
        button.Click += async (_, _) =>
        {
            if (!button.IsEnabled) return; button.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException) { }
            catch { MessageBox.Show(Window.GetWindow(button), "操作未完成，请检查连接后重试。", "好友", MessageBoxButton.OK, MessageBoxImage.Information); }
            finally { button.IsEnabled = true; }
        }; return button;
    }
    internal static string State(string value) => value switch { "pending" => "等待处理", "accepted" => "已同意", "rejected" => "已拒绝", "withdrawn" => "已撤回", "expired" => "已过期", "invalidated" => "已失效", "ignored" => "已忽略", _ => "状态暂不可用" };
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _poll.Stop(); _poll.Tick -= Poll;
        IsVisibleChanged -= VisibilityChanged; if (_vm is not null) _vm.PropertyChanged -= Changed;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}
