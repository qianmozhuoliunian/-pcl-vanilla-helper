using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using Microsoft.Win32;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Paths;
using PclVersionBridge.Core.Presentation;
using PclVersionBridge.Core.Modpacks;
using PclVersionBridge.Core.Friends;

namespace PclVersionBridge.App;

public partial class ModpackView : UserControl
{
    public ModpackView() => InitializeComponent();
    public ModpackViewModel ViewModel { get => (ModpackViewModel)DataContext; set => DataContext = value; }
    public Task? ActiveTask { get; private set; }
    public event Action<ModpackRelease>? ShareRequested;
    public Task PreviewInvitationAsync(PackReference reference,CancellationToken token) => Run(()=>ViewModel.PreviewInvitationAsync(reference,token));
    private void Share_Click(object sender,RoutedEventArgs e)
    {if(!ViewModel.IsBusy && ViewModel.SelectedRelease is { } release)ShareRequested?.Invoke(release);}
    private async Task Run(Func<Task> action)
    {
        if (ActiveTask is { IsCompleted: false }) return;
        try { ActiveTask = action(); await ActiveTask; }
        catch (InstallException ex) { MessageBox.Show(Window.GetWindow(this),ex.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Information); }
        finally { ActiveTask = null; }
    }
    private async void Search_Click(object sender,RoutedEventArgs e) => await Run(() => ViewModel.SearchAsync());
    private async void Previous_Click(object sender,RoutedEventArgs e) => await Run(() => ViewModel.ChangePageAsync(Math.Max(0,ViewModel.Offset-20)));
    private async void Next_Click(object sender,RoutedEventArgs e)
    { if (ViewModel.Offset+20 < ViewModel.Total) await Run(() => ViewModel.ChangePageAsync(ViewModel.Offset+20)); }
    private async void Query_KeyDown(object sender,KeyEventArgs e)
    {if(e.Key==Key.Enter && !ViewModel.IsBusy){e.Handled=true;await Run(()=>ViewModel.SearchAsync());}}
    private void ShowDetails(ModpackProject project)
    {
        if(ViewModel.IsBusy)return;
        ViewModel.SelectedProject=project;
        new ModpackDetailsWindow(project,token=>ViewModel.GetProjectDetailsAsync(project,token)){Owner=Window.GetWindow(this)}.ShowDialog();
    }
    private void Details_Click(object sender,RoutedEventArgs e)
    {if((sender as FrameworkElement)?.DataContext is ModpackProject project)ShowDetails(project);}
    private void ProjectTitle_Click(object sender,RoutedEventArgs e)
    {if((sender as Hyperlink)?.DataContext is ModpackProject project)ShowDetails(project);}
    private void Projects_MouseDoubleClick(object sender,MouseButtonEventArgs e)
    {if((sender as ListBox)?.SelectedItem is ModpackProject project){e.Handled=true;ShowDetails(project);}}
    private void Projects_KeyDown(object sender,KeyEventArgs e)
    {if(e.Key==Key.Enter && (sender as ListBox)?.SelectedItem is ModpackProject project){e.Handled=true;ShowDetails(project);}}
    private async void Versions_Click(object sender,RoutedEventArgs e) => await Run(ViewModel.LoadVersionsAsync);
    private async void Preview_Click(object sender,RoutedEventArgs e) => await Run(ViewModel.PreviewAsync);
    private async void Install_Click(object sender,RoutedEventArgs e) => await Run(ViewModel.InstallAsync);
    private void Cancel_Click(object sender,RoutedEventArgs e) => ViewModel.Cancel();
    private async void Import_Click(object sender,RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title="选择 Modrinth 整合包",Filter="Modrinth 整合包 (*.mrpack)|*.mrpack",CheckFileExists=true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await Run(() => ViewModel.ImportAsync(dialog.FileName));
    }
    private void Open_Click(object sender,RoutedEventArgs e)
    {
        try { var root = GamePathValidator.ValidateRoot(ViewModel.DirectoryPath); Process.Start(new ProcessStartInfo("explorer.exe",root) { UseShellExecute=true }); }
        catch { MessageBox.Show(Window.GetWindow(this),"请先选择有效的游戏目录。","无法打开目录"); }
    }
}
