#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;

namespace SynToolkit.ViewModels
{
    public enum InstallerQueueState
    {
        Pending,
        Installing,
        Completed,
        Failed,
        Canceled
    }

    /// <summary>
    /// Displays the progress of one app in a sequential WinGet installation queue.
    /// </summary>
    public partial class InstallerQueueItemViewModel : ObservableObject
    {
        public InstallerQueueItemViewModel(FeaturedInstallerViewModel installer)
        {
            Installer = installer;
            State = InstallerQueueState.Pending;
        }

        public FeaturedInstallerViewModel Installer { get; }

        public string Name => Installer.Name;

        public string IconSource => Installer.IconSource;

        public string PackageIdentifier => Installer.PackageIdentifier;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsInstalling))]
        [NotifyPropertyChangedFor(nameof(StateGlyph))]
        private InstallerQueueState _state;

        [ObservableProperty]
        private string _detail = "Waiting";

        [ObservableProperty]
        private double _progress;

        public bool IsInstalling => State == InstallerQueueState.Installing;

        public string StateGlyph => State switch
        {
            InstallerQueueState.Pending => "\uE823",
            InstallerQueueState.Installing => "\uE896",
            InstallerQueueState.Completed => "\uE73E",
            InstallerQueueState.Failed => "\uE783",
            InstallerQueueState.Canceled => "\uE711",
            _ => "\uE946"
        };
    }
}
