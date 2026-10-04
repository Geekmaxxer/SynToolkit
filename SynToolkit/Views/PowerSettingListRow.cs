#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Linq;

namespace SynToolkit.Views
{
    internal abstract class PowerSettingListRow : ObservableObject
    {
        private Visibility _groupHeaderVisibility = Visibility.Collapsed;
        private string _groupCountLabel = string.Empty;
        public abstract string GroupName { get; }
        public Visibility GroupHeaderVisibility
        {
            get => _groupHeaderVisibility;
            private set => SetProperty(ref _groupHeaderVisibility, value);
        }
        public string GroupCountLabel
        {
            get => _groupCountLabel;
            private set => SetProperty(ref _groupCountLabel, value);
        }

        // Keep a flat source so virtualization applies to settings rather than entire groups.
        public static void UpdateHeaders<T>(IReadOnlyList<T> rows) where T : PowerSettingListRow
        {
            foreach (var group in rows.GroupBy(row => row.GroupName))
            {
                int count = group.Count();
                bool first = true;
                foreach (T row in group)
                {
                    row.GroupHeaderVisibility = first ? Visibility.Visible : Visibility.Collapsed;
                    row.GroupCountLabel = first ? $"{count} setting{(count == 1 ? "" : "s")}" : string.Empty;
                    first = false;
                }
            }
        }
    }
}
