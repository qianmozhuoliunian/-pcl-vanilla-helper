using System.IO;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Catalog;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Install;
using PclVersionBridge.Core.Presentation;
using PclVersionBridge.Core.Modpacks;
using System.Windows.Controls;
using PclVersionBridge.Core.Friends;
using PclVersionBridge.App.Friends;
using System.Security.Cryptography;
using System.Text;

namespace PclVersionBridge.App;

public partial class MainWindow : Window
{
    private readonly HttpClient _client = OfficialHostHandler.CreateClient();
    private readonly MainViewModel _viewModel;
    private readonly HttpClient _packClient = ModpackNetwork.CreateClient();
    private readonly InstallationGate _gate = new();
    private readonly ModpackViewModel _packs;
    private int _selectedPage;
    private Task? _installTask;
    private bool _closeAfterInstall;
    private bool _isClosingForCancellation;
    private readonly HttpClient? _friendClient;
    private readonly IFriendService? _friendService;
    private readonly FriendsViewModel? _friends;
    private readonly CancellationTokenSource _friendLifetime=new();

    public MainWindow()
    {
        InitializeComponent();
        var source = new CmlInstallSource(_client);
        var coordinator = new InstallCoordinator(source, new InstallCommitter());
        var suggestion = Path.Combine(AppContext.BaseDirectory, ".minecraft");
        _viewModel = new MainViewModel(new VersionCatalog(_client), coordinator,
            Directory.Exists(suggestion) ? suggestion : null, _gate);
        var network = new ModpackNetwork(_packClient);
        var packCatalog = new ModrinthCatalog(network);
        ProjectImage.Cache = new ModpackImageCache(network, packCatalog);
        var content = new ModpackContentSource(network);
        var fabric = new FabricInstallSource(new VersionCatalog(_client), source, network);
        _packs = new ModpackViewModel(packCatalog, content,
            new ModpackInstallCoordinator(fabric, content, new InstallCommitter()), _gate,
            Path.Combine(Path.GetTempPath(), "VanillaReach", "packs"));
        PackPage.ViewModel = _packs;
        var friendConfiguration=FriendFeatureConfiguration.Load();
        if(friendConfiguration.ServiceUri is { } friendUri)
        {
            _friendClient=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {Timeout=TimeSpan.FromSeconds(30)};
            var serviceScope=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(friendUri.GetLeftPart(UriPartial.Authority))));
            var storage=Path.Combine(new DpapiFriendSessionStore().StorageDirectory,serviceScope);
            _friendService=new FriendApiClient(_friendClient,new FriendServiceOptions(friendUri,friendConfiguration.IsDevelopment),new DpapiFriendSessionStore(storage));
            _friends=new FriendsViewModel(_friendService,new DpapiFriendNotesStore(storage));
        }
        FriendsPage.Initialize(_friends,friendConfiguration.Status,friendConfiguration.IsDevelopment);
        FriendsPage.InvitationSourceLoader=async (reference,token)=>
        {
            await packCatalog.GetPinnedReleaseAsync(reference.ProjectId,reference.VersionId,reference.ArchiveSha512,token);
            return await packCatalog.GetProjectAsync(reference.ProjectId,token);
        };
        FriendsPage.InvitationRequested+=InvitationRequested;
        PackPage.ShareRequested+=release=>
        {
            if(_gate.IsBusy)return;
            FriendsPage.SetShareCandidate(new PackReference(release.ProjectId,release.Id,release.Sha512));
            FriendsPage.ShareCandidateLabel=release.Name+" · "+release.VersionNumber;
            Pages.SelectedIndex=2;
        };
        _packs.PropertyChanged+=(_,args)=>
        {
            if(args.PropertyName is nameof(ModpackViewModel.SelectedRelease) or nameof(ModpackViewModel.IsBusy))
            {
                var release=_packs.IsBusy?null:_packs.SelectedRelease;
                FriendsPage.SetShareCandidate(release is null?null:new PackReference(release.ProjectId,release.Id,release.Sha512));
                FriendsPage.ShareCandidateLabel=release is null?"尚未选择整合包版本":release.Name+" · "+release.VersionNumber;
            }
        };
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.DirectoryPath) or nameof(MainViewModel.DirectoryConfirmed))
                _packs.SetDirectory(_viewModel.DirectoryPath, _viewModel.DirectoryConfirmed);
        };
        _gate.PropertyChanged += (_, _) =>
        {
            DirectoryPanel.IsEnabled = !_gate.IsBusy;
            Navigation.IsEnabled = !_gate.IsBusy;
        };
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadVersionsAsync();
        Closing += MainWindow_Closing;
        Activated+=(_,_)=>FriendsPage.SetActive(Pages.SelectedIndex==2);
        Deactivated+=(_,_)=>FriendsPage.SetActive(false);
        Closed+=(_,_)=>
        {
            _friendLifetime.Cancel();FriendsPage.Dispose();
            (_friends as IDisposable)?.Dispose();(_friendService as IDisposable)?.Dispose();_friendClient?.Dispose();_friendLifetime.Dispose();
        };
    }
    private void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Pages || VanillaFooter is null) return;
        if (_gate.IsBusy) { Pages.SelectedIndex = _selectedPage; return; }
        _selectedPage = Pages.SelectedIndex;
        VanillaFooter.Visibility = _selectedPage == 0 ? Visibility.Visible : Visibility.Collapsed;
        DirectoryCard.Visibility = _selectedPage==2?Visibility.Collapsed:Visibility.Visible;
        FriendsPage?.SetActive(_selectedPage==2 && IsActive);
    }
    private async void InvitationRequested(PackInvitation invitation)
    {
        if(_friendService is null || _gate.IsBusy || _packs.IsBusy)return;
        var userId=_friendService.Session?.Me.UserId;
        if(userId is null || invitation.To.UserId!=userId)return;
        try
        {
            var latest=await _friendService.GetInvitationAsync(invitation.Id,_friendLifetime.Token);
            if(_friendService.Session?.Me.UserId!=userId || latest.To.UserId!=userId || latest.State!="pending" || latest.ExpiresAtUtc<=DateTimeOffset.UtcNow)
                throw new InstallException("邀请已失效或登录状态已改变，请重新打开邀请。");
            if(_gate.IsBusy || _packs.IsBusy)throw new InstallException("已有安装或检查正在进行，请稍后重试。");
            Pages.SelectedIndex=1;
            await PackPage.PreviewInvitationAsync(latest.Pack,_friendLifetime.Token);
        }
        catch(OperationCanceledException) { }
        catch(Exception)
        {
            if(!_friendLifetime.IsCancellationRequested)
                MessageBox.Show(this,"邀请暂时无法检查。可能已过期、被撤回或服务不可用；请刷新邀请后重试。","未能检查邀请",MessageBoxButton.OK,MessageBoxImage.Information);
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 PCL 使用的 .minecraft 文件夹" };
        if (dialog.ShowDialog(this) == true)
            _viewModel.DirectoryPath = dialog.FolderName;
    }

    private void ConfirmDirectory_Click(object sender, RoutedEventArgs e)
    {
        try { _viewModel.ConfirmDirectory(); }
        catch (InstallException ex)
        {
            MessageBox.Show(this, ex.Message, "目录无法使用", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadVersionsAsync();

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        _installTask = _viewModel.InstallAsync();
        try { await _installTask; }
        finally { _installTask = null; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.Cancel();

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        var task = _installTask ?? PackPage.ActiveTask;
        if (_closeAfterInstall || task is null || task.IsCompleted) return;
        e.Cancel = true;
        if (_isClosingForCancellation) return;
        _isClosingForCancellation = true;
        _viewModel.Cancel();
        _packs.Cancel();
        try { await task; }
        finally
        {
            _closeAfterInstall = true;
            Close();
        }
    }
}


