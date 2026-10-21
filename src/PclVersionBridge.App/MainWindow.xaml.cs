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

namespace PclVersionBridge.App;

public partial class MainWindow : Window
{
    private readonly HttpClient _client = OfficialHostHandler.CreateClient();
    private readonly MainViewModel _viewModel;
    private Task? _installTask;
    private bool _closeAfterInstall;
    private bool _isClosingForCancellation;

    public MainWindow()
    {
        InitializeComponent();
        var source = new CmlInstallSource(_client);
        var coordinator = new InstallCoordinator(source, new InstallCommitter());
        var suggestion = Path.Combine(AppContext.BaseDirectory, ".minecraft");
        _viewModel = new MainViewModel(new VersionCatalog(_client), coordinator,
            Directory.Exists(suggestion) ? suggestion : null);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadVersionsAsync();
        Closing += MainWindow_Closing;
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
        if (_closeAfterInstall || _installTask is null || _installTask.IsCompleted) return;
        e.Cancel = true;
        if (_isClosingForCancellation) return;
        _isClosingForCancellation = true;
        _viewModel.Cancel();
        try { await _installTask; }
        finally
        {
            _closeAfterInstall = true;
            Close();
        }
    }
}


