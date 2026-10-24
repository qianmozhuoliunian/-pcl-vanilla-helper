using System.ComponentModel;

namespace PclVersionBridge.Core.Presentation;

public sealed class InstallationGate : INotifyPropertyChanged
{
    private int _busy;
    public bool IsBusy => Volatile.Read(ref _busy) != 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public IDisposable Acquire()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InstallException("已有安装正在进行，请等待完成。");
        PropertyChanged?.Invoke(this, new(nameof(IsBusy)));
        return new Lease(this);
    }
    private sealed class Lease(InstallationGate gate) : IDisposable
    {
        private InstallationGate? _gate = gate;
        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _gate, null);
            if (value is null) return;
            Interlocked.Exchange(ref value._busy, 0);
            value.PropertyChanged?.Invoke(value, new(nameof(IsBusy)));
        }
    }
}
