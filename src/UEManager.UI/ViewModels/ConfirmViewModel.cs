using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UEManager.UI.ViewModels;

/// <summary>A yes/no question shown in the themed overlay instead of a system message box; with a text box when it asks for a text.</summary>
/// <param name="input">The text the box starts with; null for a question without a text box.</param>
public sealed partial class ConfirmViewModel(string title, string message, string confirmText, string cancelText, double width = 480,
    string? input = null) : LocalizedObservableObject
{
    private readonly TaskCompletionSource<bool> result = new();

    [ObservableProperty]
    private string inputText = input ?? "";

    public bool HasInput => input is not null;

    public string Title => title;

    public string Message => message;

    public string ConfirmText => confirmText;

    public string CancelText => cancelText;

    public double Width => width;

    public Task<bool> Result => result.Task;

    [RelayCommand]
    private void Accept() => result.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => result.TrySetResult(false);
}
