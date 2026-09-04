using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stardrop.Utilities;
using Stardrop.ViewModels;
using System;
using System.Collections.Generic;

namespace Stardrop.Views
{
    public partial class ChangelogWindow : BaseWindow
    {
        public ChangelogWindow() : base()
        {
            AvaloniaXamlLoader.Load(this);

            this.FindControl<Button>("closeButton").Click += delegate { this.Close(); };
            this.FindControl<Button>("modPageButton").Click += delegate
            {
                if (DataContext is ChangelogWindowViewModel viewModel)
                {
                    Toolkit.OpenBrowser(viewModel.ModPageUri);
                }
            };
        }

        /// <summary>
        /// Opens the window in its loading state. The caller is expected to show the window straight
        /// away and then hand the fetched changelogs to <see cref="SetChangelogs"/>, so that the user
        /// sees the window immediately rather than waiting on the Nexus request.
        /// </summary>
        public ChangelogWindow(string modName, string modPageUri) : this()
        {
            if (DataContext is not ChangelogWindowViewModel viewModel)
            {
                return;
            }

            viewModel.ModName = String.Format(Program.translation.Get("ui.window.changelog.title"), modName);
            viewModel.ModPageUri = modPageUri;
            viewModel.HasModPage = String.IsNullOrEmpty(modPageUri) is false;
            viewModel.IsLoading = true;
        }

        public void SetChangelogs(Dictionary<string, List<string>>? changelogs)
        {
            if (DataContext is not ChangelogWindowViewModel viewModel)
            {
                return;
            }

            viewModel.IsLoading = false;

            if (changelogs is null)
            {
                viewModel.EmptyMessage = Program.translation.Get("ui.window.changelog.fetch_failed");
                viewModel.ShowEmptyMessage = true;
                return;
            }

            // Only the newest entry is shown - mods such as SVE publish 170+ versions, and rendering
            // them all was noticeably slow. The mod page button covers reading the rest.
            var newest = ChangelogWindowViewModel.SortNewestFirst(changelogs);
            if (newest.Count > 0)
            {
                viewModel.Versions.Add(newest[0]);
            }

            // Nexus answers with an empty object for mods whose authors publish changelogs elsewhere.
            viewModel.HasChanges = viewModel.Versions.Count > 0;
            if (viewModel.HasChanges is false)
            {
                viewModel.EmptyMessage = Program.translation.Get("ui.window.changelog.none_published");
                viewModel.ShowEmptyMessage = true;
            }
        }
    }
}
