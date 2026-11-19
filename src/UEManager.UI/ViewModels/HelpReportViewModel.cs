using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>
/// The "Report a problem" window: the player says in a few words what happened, the manager adds the
/// facts. The app sends nothing; the player copies the report to wherever they ask for help, or pastes it
/// into a new issue on GitHub.
/// </summary>
public sealed partial class HelpReportViewModel(MainViewModel owner, string facts) : LocalizedObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportText))]
    private string problem = "";

    [ObservableProperty]
    private string? copiedText;

    public string ReportText => HelpReport.Compose(Problem, facts);

    [RelayCommand]
    private void Copy()
    {
        if (TryCopy())
            CopiedText = L.T("Copied. Paste it where you ask for help.");
    }

    /// <summary>Copies the report and opens GitHub's page for a new issue, where the player pastes and posts it.</summary>
    [RelayCommand]
    private void OpenIssue()
    {
        if (!TryCopy())
            return;
        owner.OpenPath(HelpReport.IssueUrl(Problem, L.T("Press Ctrl+V here to paste the report, then post it.")));
        CopiedText = L.T("Copied. On the GitHub page that opened, press Ctrl+V to paste it, then post it. This needs a GitHub account.");
    }

    private bool TryCopy()
    {
        if (ClipboardText.Set(ReportText) is not { } error)
            return true;
        CopiedText = L.F($"The clipboard is in use by another app. Try again. ({error})");
        return false;
    }

    [RelayCommand]
    private void Close() => owner.CloseHelpDialog();
}
