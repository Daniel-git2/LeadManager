using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LeadManager.Services;

/// <summary>A short message that clears itself after a few seconds. Errors stay until dismissed.</summary>
public sealed partial class Notice : ObservableObject
{
    private readonly DispatcherTimer _timer = new();

    public Notice()
    {
        _timer.Tick += (_, _) => Dismiss();
    }

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    public void Show(string message, int seconds = 4)
    {
        IsError = false;
        Message = message;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    public void ShowError(string message)
    {
        _timer.Stop();
        IsError = true;
        Message = message;
    }

    [RelayCommand]
    public void Dismiss()
    {
        _timer.Stop();
        Message = null;
        IsError = false;
    }
}
