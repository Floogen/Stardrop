using Avalonia.Controls;
using DynamicData;
using DynamicData.Aggregation;
using DynamicData.Binding;
using ReactiveUI;
using Stardrop.Models.Data;
using Stardrop.Utilities.External;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Stardrop.ViewModels
{
    public class DownloadPanelViewModel : ViewModelBase
    {
        private ObservableCollection<ModDownloadViewModel> _downloads = new();
        public ObservableCollection<ModDownloadViewModel> Downloads { get => _downloads; set => this.RaiseAndSetIfChanged(ref _downloads, value); }

        public IObservable<int> InProgressDownloads { get; init; }

        public DownloadPanelViewModel(NexusClient? nexusClient)
        {
            Nexus.ClientChanged += NexusClientChanged;
            if (nexusClient is not null)
            {
                RegisterEventHandlers(nexusClient);
            }

            // Count failed and canceled downloads toward this value, because those still need to be 
            // handled in some way by the user
            InProgressDownloads = Downloads
                .ToObservableChangeSet(t => t.ModUri)
                .AutoRefresh(x => x.DownloadStatus, scheduler: RxApp.MainThreadScheduler)
                .Filter(x => x.DownloadStatus != ModDownloadStatus.Successful)
                .Count();
        }

        private void NexusClientChanged(NexusClient? oldClient, NexusClient? newClient)
        {
            if (oldClient is not null)
            {
                // Cancel all downloads and clear the dictionary, so we don't have zombie downloads from an old client lingering
                foreach (var download in Downloads)
                {
                    // Trigger the cancel command and ignore any return values (as it has none)
                    download.CancelCommand.Execute().Subscribe();
                }
                ClearEventHandlers(oldClient);
                Downloads.Clear();
            }
            if (newClient is not null)
            {
                RegisterEventHandlers(newClient);
            }
        }

        private void RegisterEventHandlers(NexusClient nexusClient)
        {
            nexusClient.DownloadStarted += DownloadStarted;
            nexusClient.DownloadProgressChanged += DownloadProgressChanged;
            nexusClient.DownloadCompleted += DownloadCompleted;
            nexusClient.DownloadFailed += DownloadFailed;
        }

        private void ClearEventHandlers(NexusClient nexusClient)
        {
            nexusClient.DownloadStarted -= DownloadStarted;
            nexusClient.DownloadProgressChanged -= DownloadProgressChanged;
            nexusClient.DownloadCompleted -= DownloadCompleted;
            nexusClient.DownloadFailed -= DownloadFailed;
        }

        private void DownloadStarted(object? sender, ModDownloadStartedEventArgs e)
        {
            var existingDownload = Downloads.FirstOrDefault(x => x.ModUri == e.Uri);
            if (existingDownload is not null)
            {
                // If the user is trying to download the same file twice, it's *probably* because they
                // want to retry a failed download.
                // But just in case, check to see if the existing download is still in-progress. If it is, do nothing.
                // We don't want to stop a user's 95% download because they accidentally hit the "download again please" button!
                if (existingDownload.DownloadStatus == ModDownloadStatus.NotStarted
                    || existingDownload.DownloadStatus == ModDownloadStatus.InProgress)
                {
                    return;
                }

                // If it does exist and isn't in a progress state, they're probably trying to redownload a failed download.
                // Since we use the URI as our unique ID, we shouldn't have two items with the same URI in the list,
                // so clear out the old one.
                Downloads.Remove(existingDownload);
            }

            var downloadVM = new ModDownloadViewModel(e.Uri, e.Name, e.Size, e.DownloadCancellationSource);
            downloadVM.RemovalRequested += DownloadRemovalRequested;
            Downloads.Add(downloadVM);
        }

        private void DownloadProgressChanged(object? sender, ModDownloadProgressEventArgs e)
        {
            var download = Downloads.SingleOrDefault(x => x.ModUri == e.Uri);
            if (download is not null)
            {
                download.DownloadStatus = ModDownloadStatus.InProgress;
                download.DownloadedBytes = e.TotalBytes;
            }
        }

        private void DownloadCompleted(object? sender, ModDownloadCompletedEventArgs e)
        {
            var download = Downloads.SingleOrDefault(x => x.ModUri == e.Uri);
            if (download is not null)
            {
                download.DownloadStatus = ModDownloadStatus.Successful;
            }
        }

        private void DownloadFailed(object? sender, ModDownloadFailedEventArgs e)
        {
            var download = Downloads.SingleOrDefault(x => x.ModUri == e.Uri);
            if (download is not null)
            {
                download.DownloadStatus = ModDownloadStatus.Failed;
            }
        }

        private void DownloadRemovalRequested(object? sender, EventArgs _)
        {
            if (sender is not ModDownloadViewModel downloadVM)
            {
                return;
            }

            downloadVM.RemovalRequested -= DownloadRemovalRequested;
            Downloads.Remove(downloadVM);
        }

        // Designer-only constructor
        public DownloadPanelViewModel()
        {
            if (!Design.IsDesignMode)
            {
                throw new Exception("This constructor should only be called in design mode.");
            }

            var inProgressDownload = new ModDownloadViewModel(
                new Uri("https://www.fakeurl.com/testMod"),
                "Fake Test Mod Download",
                1024 * 1024,
                new()
            );
            inProgressDownload.DownloadStatus = ModDownloadStatus.InProgress;
            inProgressDownload.DownloadedBytes = inProgressDownload.SizeBytes!.Value / 2;
            Downloads.Add(inProgressDownload);

            var succeededDownload = new ModDownloadViewModel(
                new Uri("https://www.fakeSuccess.com"),
                "Fake Succeeded Download",
                1234,
                new()
            );
            succeededDownload.DownloadStatus = ModDownloadStatus.Successful;
            succeededDownload.DownloadedBytes = 1234;
            Downloads.Add(succeededDownload);

            var failedDownload = new ModDownloadViewModel(
                new Uri("https://www.differentFakeUrl.com"),
                "Failed Fake Download",
                1024 * 1024 * 1024,
                new()
            );
            failedDownload.DownloadedBytes = failedDownload.SizeBytes!.Value / 3;
            failedDownload.DownloadStatus = ModDownloadStatus.Failed;
            Downloads.Add(failedDownload);

            var cancelledDownload = new ModDownloadViewModel(
                new Uri("https://www.cancelledFake.com"),
                "Cancelled Fake Download",
                1024 * 1024 * 5,
                new()
            );
            cancelledDownload.DownloadedBytes = cancelledDownload.SizeBytes!.Value / 4;
            cancelledDownload.DownloadStatus = ModDownloadStatus.Canceled;
            Downloads.Add(cancelledDownload);

            var indeterminateInProgressDownload = new ModDownloadViewModel(
                new Uri("https://www.inProgressMystery.com"),
                "In Progress Download of Unknown Size",
                null,
                new()
            );
            indeterminateInProgressDownload.DownloadedBytes = 1024 * 1024 * 2;
            indeterminateInProgressDownload.DownloadStatus = ModDownloadStatus.InProgress;
            Downloads.Add(indeterminateInProgressDownload);
        }
    }
}
