using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>Where a download is in the line.</summary>
public enum DownloadState
{
    /// <summary>In line: it starts when the ones before it are done.</summary>
    Waiting,
    Running,

    /// <summary>Stopped by the player; what has arrived stays, and Continue goes on after it.</summary>
    Paused,

    /// <summary>Stopped by a problem; what has arrived stays, and Try again goes on after it.</summary>
    Failed,
}

/// <summary>
/// A download in the line at the bottom right of the window: one runs at a time, the others wait their turn. It shows
/// its progress and can be paused, continued, moved to the front of the line, or taken out.
/// </summary>
/// <param name="fileName">The name the download is saved under.</param>
/// <param name="size">The file's size in bytes for the figures shown; 0 when not known.</param>
/// <param name="changed">The line looks who is next: called when this download waits again or frees its turn.</param>
/// <param name="moveFirst">Puts this download before the others that wait.</param>
public sealed partial class DownloadItemViewModel(string id, string name, string fileName, long size, Action changed,
    Action<DownloadItemViewModel> moveFirst) : ObservableObject
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch clock = new();
    private CancellationTokenSource transfer = new();
    private TaskCompletionSource turn = new();
    private long? doneAtStart;

    public string Id => id;

    public string Name => name;

    public string FileName => fileName;

    /// <summary>Where the file can be downloaded, in order; a newer link from the site replaces them.</summary>
    internal IReadOnlyList<string> Urls { get; set; } = [];

    /// <summary>Cancelled when the player takes the download out of the line.</summary>
    public CancellationToken Token => cancellation.Token;

    /// <summary>Cancelled when the transfer that runs now ends: by a pause, or with <see cref="Token"/>.</summary>
    internal CancellationToken TransferToken => transfer.Token;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause), nameof(CanContinue), nameof(ContinueTip), nameof(Tone), nameof(ShowsProgress))]
    private DownloadState state;

    /// <summary>Its place among the downloads that wait, from 1; 0 for one that does not wait.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveFirst))]
    private int place;

    [ObservableProperty]
    private double share;

    [ObservableProperty]
    private string detail = "";

    public bool CanPause => State is DownloadState.Running or DownloadState.Waiting;

    public bool CanContinue => State is DownloadState.Paused or DownloadState.Failed;

    public string ContinueTip => State == DownloadState.Failed ? L.T("Try again") : L.T("Continue the download");

    public bool CanMoveFirst => Place > 1;

    /// <summary>The colour of the line under the name: a problem stands out.</summary>
    public string Tone => State == DownloadState.Failed ? "bad" : "";

    /// <summary>The bar shows once something has arrived; a download that only waits has none.</summary>
    public bool ShowsProgress => State != DownloadState.Waiting || Share > 0;

    /// <summary>Done once the line gives this download its turn; cancelled when the player takes it out.</summary>
    internal Task WaitForTurnAsync() => turn.Task.WaitAsync(cancellation.Token);

    /// <summary>The line gives this download its turn.</summary>
    internal void Start()
    {
        transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        doneAtStart = null;
        clock.Restart();
        Place = 0;
        State = DownloadState.Running;
        Detail = $"{Share:P0}";
        turn.TrySetResult();
    }

    /// <summary>The line tells a download that waits which place it has.</summary>
    internal void ShowPlace(int value)
    {
        Place = value;
        Detail = value == 1 ? L.T("Waiting  ·  next in line") : L.F($"Waiting  ·  number {value} in line");
    }

    /// <summary>The transfer ended with a problem; the download stays in the list for another try.</summary>
    internal void Fail(string message)
    {
        turn = new TaskCompletionSource();
        State = DownloadState.Failed;
        Detail = message;
    }

    public void Report(double value)
    {
        Share = value;
        if (State != DownloadState.Running)
            return;
        if (size <= 0)
        {
            Detail = $"{value:P0}";
            return;
        }
        var done = (long)(value * size);
        // The speed of this run: what arrived before a pause does not count.
        doneAtStart ??= done;
        var speed = (long)((done - doneAtStart.Value) / Math.Max(clock.Elapsed.TotalSeconds, 0.001));
        Detail = $"{value:P0}  ·  {Format.Size(done)} / {Format.Size(size)}  ·  {Format.Size(Math.Max(speed, 0))}/s";
    }

    partial void OnShareChanged(double value) => OnPropertyChanged(nameof(ShowsProgress));

    [RelayCommand]
    private void Pause()
    {
        if (!CanPause)
            return;
        if (State == DownloadState.Running)
            turn = new TaskCompletionSource();
        Place = 0;
        State = DownloadState.Paused;
        var done = $"{Share:P0}";
        Detail = L.F($"Paused  ·  {done}");
        transfer.Cancel();
        changed();
    }

    /// <summary>Back into the line: after a pause, or for another try after a problem.</summary>
    [RelayCommand]
    private void Continue()
    {
        if (!CanContinue)
            return;
        State = DownloadState.Waiting;
        changed();
    }

    [RelayCommand]
    private void MoveFirst() => moveFirst(this);

    [RelayCommand]
    private void Cancel() => cancellation.Cancel();
}
