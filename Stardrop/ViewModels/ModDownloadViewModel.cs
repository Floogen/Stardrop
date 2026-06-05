using ReactiveUI;
using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;

namespace Stardrop.ViewModels
{
    public enum ModDownloadStatus
    {
        NotStarted,
        InProgress,
        Successful,
        Canceled,
        Failed
    }

    public class ModDownloadViewModel : ViewModelBase
    {
        private readonly DateTimeOffset _startTime;
        private readonly CancellationTokenSource _downloadCancellationSource;

        // Communicates up to the parent panel that the user wants to remove this download from the list.
        public event EventHandler? RemovalRequested = null!;

        // --Set-once properties--
        public Uri ModUri { get; init; }

        // --Bindable properties--        

        private string _name;
        public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

        private long? _sizeBytes;
        public long? SizeBytes { get => _sizeBytes; set => this.RaiseAndSetIfChanged(ref _sizeBytes, value); }

        private long _downloadedBytes;
        public long DownloadedBytes { get => _downloadedBytes; set => this.RaiseAndSetIfChanged(ref _downloadedBytes, value); }

        private ModDownloadStatus _downloadStatus = ModDownloadStatus.NotStarted;
        public ModDownloadStatus DownloadStatus { get => _downloadStatus; set => this.RaiseAndSetIfChanged(ref _downloadStatus, value); }

        // --Composite or dependent properties--

        private readonly ObservableAsPropertyHelper<double> _completion = null!;
        public double Completion => _completion.Value;

        private readonly ObservableAsPropertyHelper<bool> _isSizeUnknown = null!;
        public bool IsSizeUnknown => _isSizeUnknown.Value;

        private readonly ObservableAsPropertyHelper<string> _downloadSpeedLabel = null!;
        public string DownloadSpeedLabel => _downloadSpeedLabel.Value;

        private readonly ObservableAsPropertyHelper<string> _downloadProgressLabel = null!;
        public string DownloadProgressLabel => _downloadProgressLabel.Value;

        // --Commands--

        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

        public ModDownloadViewModel(Uri modUri, string name, long? sizeInBytes, CancellationTokenSource downloadCancellationSource)
        {
            _startTime = DateTimeOffset.UtcNow;

            ModUri = modUri;
            _name = name;
            _sizeBytes = sizeInBytes;
            _downloadedBytes = 0;
            _downloadCancellationSource = downloadCancellationSource;

            CancelCommand = ReactiveCommand.Create(Cancel);
            RemoveCommand = ReactiveCommand.Create(Remove);

            // SizeBytes null-ness to IsSizeUnknown converison
            this.WhenAnyValue(x => x.SizeBytes)
                .Select(x => x.HasValue is false)
                .ToProperty(this, x => x.IsSizeUnknown, out _isSizeUnknown);

            // DownloadedBytes to DownloadSpeedLabel conversion
            this.WhenAnyValue(x => x.DownloadedBytes)
                .Sample(TimeSpan.FromMilliseconds(500), RxApp.MainThreadScheduler)
                .Select(bytes =>
                {
                    double elapsedSeconds = (DateTimeOffset.UtcNow - _startTime).TotalSeconds;
                    double bytesPerSecond = bytes / elapsedSeconds;
                    if (bytesPerSecond > 1024 * 1024)  // MB 
                    {
                        return $"{(bytesPerSecond / (1024 * 1024)):N2} {Program.translation.Get("internal.measurements.megabytes_per_second")}";
                    }
                    else if (bytesPerSecond > 1024) // KB
                    {
                        return $"{(bytesPerSecond / 1024):N2} {Program.translation.Get("internal.measurements.kilobytes_per_second")}";
                    }
                    else // Bytes
                    {
                        return $"{bytesPerSecond:N0} {Program.translation.Get("internal.measurements.bytes_per_second")}";
                    }
                }).ToProperty(this, x => x.DownloadSpeedLabel, out _downloadSpeedLabel);

            // DownloadedBytes and SizeBytes to DownloadProgressLabel conversion
            this.WhenAnyValue(x => x.DownloadedBytes, x => x.SizeBytes)
                .Sample(TimeSpan.FromMilliseconds(500), RxApp.MainThreadScheduler)
                .Select(((long Bytes, long? Total) x) =>
                {
                    string bytesString = ToHumanReadable(x.Bytes);
                    if (x.Total is null)
                    {
                        return $"{bytesString} / ??? {Program.translation.Get("internal.measurements.megabytes_size")}";
                    }
                    else
                    {
                        string totalString = ToHumanReadable(x.Total!.Value);
                        return $"{bytesString} / {totalString}";
                    }

                    static string ToHumanReadable(long bytes)
                    {
                        if (bytes > 1024 * 1024) // MB
                        {
                            return $"{(bytes / (1024.0 * 1024.0)):N2} {Program.translation.Get("internal.measurements.megabytes_size")}";
                        }
                        else if (bytes > 1024) // KB
                        {
                            return $"{(bytes / 1024.0):N2} {Program.translation.Get("internal.measurements.kilobytes_size")}";
                        }
                        else
                        {
                            return $"{bytes:N0} {Program.translation.Get("internal.measurements.bytes_size")}";
                        }
                    }
                }).ToProperty(this, x => x.DownloadProgressLabel, out _downloadProgressLabel);

            if (SizeBytes.HasValue)
            {
                // DownloadedBytes to Completion conversion
                this.WhenAnyValue(x => x.DownloadedBytes)
                    .Sample(TimeSpan.FromMilliseconds(500), RxApp.MainThreadScheduler)
                    .Select(x => (DownloadedBytes / (double)SizeBytes) * 100)
                    .ToProperty(this, x => x.Completion, out _completion);
            }
        }

        private void Cancel()
        {
            _downloadCancellationSource.Cancel();
            DownloadStatus = ModDownloadStatus.Canceled;
        }

        private void Remove()
        {
            RemovalRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
