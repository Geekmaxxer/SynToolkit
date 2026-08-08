#nullable enable

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SynToolkit.Services.RadeonSlimmer
{
    public abstract class RadeonSelectionItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetSelection(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class RadeonPackage : RadeonSelectionItem
    {
        private bool _keep = true;

        public required string SourceFile { get; init; }
        public required string ProductName { get; init; }
        public required string Url { get; init; }
        public required string Type { get; init; }
        public required string Description { get; init; }

        public bool Keep
        {
            get => _keep;
            set => SetSelection(ref _keep, value);
        }
    }

    public sealed class RadeonScheduledTask : RadeonSelectionItem
    {
        private bool _enabled;

        public required string SourceFile { get; init; }
        public string? Uri { get; init; }
        public required string Description { get; init; }
        public required string Command { get; init; }

        public bool Enabled
        {
            get => _enabled;
            set => SetSelection(ref _enabled, value);
        }
    }

    public sealed class RadeonDisplayComponent : RadeonSelectionItem
    {
        private bool _keep = true;

        public required string DirectoryPath { get; init; }
        public required string Name { get; init; }

        public bool Keep
        {
            get => _keep;
            set => SetSelection(ref _keep, value);
        }
    }
}
