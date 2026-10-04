#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Views
{
    internal sealed class PowerSettingEditRow
    {
        public required PowerPlanSettingInspection Setting { get; init; }
        public required IReadOnlyList<PowerSettingChoice> Choices { get; init; }
        public required double Minimum { get; init; }
        public required double Maximum { get; init; }
        public required double Increment { get; init; }
        public required string RangeHint { get; init; }
        public required bool HasAc { get; init; }
        public required bool HasDc { get; init; }
        public required uint? SavedAc { get; set; }
        public required uint? SavedDc { get; set; }
        public PowerSettingChoice? AcChoice { get; set; }
        public PowerSettingChoice? DcChoice { get; set; }
        public double AcNumber { get; set; }
        public double DcNumber { get; set; }

        public string Name => Setting.Name;
        public string Description => Setting.Description;
        public string SettingId => Setting.SettingId.ToString("D");
        public Visibility ChoiceVisibility => Choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NumberVisibility => Choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public uint? GetValue(bool onAc)
        {
            if (!(onAc ? HasAc : HasDc)) return null;
            if (Choices.Count > 0) return (onAc ? AcChoice : DcChoice)?.Value;
            double value = onAc ? AcNumber : DcNumber;
            if (double.IsNaN(value) || value < Minimum || value > Maximum ||
                value != Math.Truncate(value) || value > uint.MaxValue ||
                (value - Minimum) % Increment != 0)
            {
                throw new InvalidOperationException($"Enter a valid whole-number value for {Name}.");
            }
            return (uint)value;
        }
    }

    internal sealed record PowerSettingEditGroup(string Name, IReadOnlyList<PowerSettingEditRow> Rows);

    public sealed partial class PowerPlanEditorDialog : ContentDialog
    {
        private readonly PowerPlanService _service;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private readonly CancellationTokenSource _loadCancellation = new();
        private readonly List<PowerSettingEditRow> _rows = new();
        private Guid _schemeId;
        private string _schemeName;
        private readonly bool _autoSave;
        private bool _isOpen;
        private bool _loading;
        private bool _updatingCategories;

        public bool PlanChanged { get; private set; }

        public PowerPlanEditorDialog(PowerPlanService service, InstalledPowerPlan plan, bool autoSave)
        {
            InitializeComponent();
            _service = service;
            _schemeId = plan.SchemeId;
            _schemeName = plan.Name;
            _autoSave = autoSave;

            bool lightTheme = Application.Current.RequestedTheme == ApplicationTheme.Light;
            Resources["ContentDialogSmokeFill"] = new AcrylicBrush
            {
                TintColor = lightTheme
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 246)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 32, 35, 43),
                TintOpacity = lightTheme ? 0.50 : 0.66,
                FallbackColor = lightTheme
                    ? Microsoft.UI.ColorHelper.FromArgb(184, 246, 246, 246)
                    : Microsoft.UI.ColorHelper.FromArgb(217, 32, 35, 43)
            };
            UpdateHeader();
        }

        private async void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            _isOpen = true;
            if (XamlRoot is not null)
            {
                DialogContent.Width = Math.Min(900, Math.Max(320, XamlRoot.Size.Width - 56));
                DialogContent.Height = Math.Min(640, Math.Max(340, XamlRoot.Size.Height - 120));
            }
            await LoadSettingsAsync();
            if (_isOpen) SearchBox.Focus(FocusState.Programmatic);
        }

        private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            _isOpen = false;
            _loadCancellation.Cancel();
        }

        private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            if (_saveGate.CurrentCount == 0)
            {
                args.Cancel = true;
                ErrorText.Text = "Wait for the current save to finish.";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

        private async Task LoadSettingsAsync()
        {
            _loading = true;
            BusyRing.IsActive = true;
            BusyRing.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = false;
            SaveAsButton.IsEnabled = false;
            try
            {
                CancellationToken token = _loadCancellation.Token;
                PowerPlanInspection inspection = await _service.ReadInstalledPlanAsync(_schemeId, token);
                List<PowerSettingEditRow> rows = await Task.Run(
                    () => BuildRows(inspection, token), token);
                if (!_isOpen) return;
                _rows.Clear();
                _rows.AddRange(rows);
                CategoryList.ItemsSource = new[]
                {
                    new PowerSettingCategoryView("All settings", _rows.Count, IsAll: true)
                }.Concat(_rows.GroupBy(row => row.Setting.GroupName)
                    .Select(group => new PowerSettingCategoryView(group.Key, group.Count())))
                    .ToArray();
                CategoryList.SelectedIndex = 0;
                StatusText.Text = $"{_rows.Count} settings · {CategoryList.Items.Count - 1} categories";
                RenderRows();
                RefreshSaveState();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Unable to load editable power settings.");
                if (_isOpen) ErrorText.Text = exception.Message;
            }
            finally
            {
                _loading = false;
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                RefreshSaveState();
            }
        }

        private static List<PowerSettingEditRow> BuildRows(
            PowerPlanInspection inspection, CancellationToken cancellationToken)
        {
            List<PowerSettingEditRow> rows = new(inspection.Settings.Count);
            foreach (PowerPlanSettingInspection setting in inspection.Settings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PowerSettingValueEditor editor = PowerPlanSettingsReader.GetValueEditor(
                    setting.SubgroupId, setting.SettingId);
                List<PowerSettingChoice> choices = editor.Choices.ToList();
                foreach (uint? existing in new[] { setting.AcIndex, setting.DcIndex })
                {
                    if (existing is uint value && choices.Count > 0 &&
                        choices.All(choice => choice.Value != value))
                    {
                        choices.Add(new PowerSettingChoice(value, $"Current value ({value})"));
                    }
                }
                rows.Add(new PowerSettingEditRow
                {
                    Setting = setting,
                    Choices = choices,
                    Minimum = editor.Minimum,
                    Maximum = editor.Maximum,
                    Increment = editor.Increment,
                    RangeHint = choices.Count > 0 ? string.Empty : !editor.HasRange
                        ? "Windows does not report a range for this setting."
                        : $"Allowed: {editor.Minimum}–{editor.Maximum}" +
                          (string.IsNullOrWhiteSpace(editor.Units) ? "" : $" {editor.Units}") +
                          $", step {editor.Increment}",
                    HasAc = setting.AcIndex.HasValue,
                    HasDc = setting.DcIndex.HasValue,
                    SavedAc = setting.AcIndex,
                    SavedDc = setting.DcIndex,
                    AcChoice = choices.FirstOrDefault(choice => choice.Value == setting.AcIndex),
                    DcChoice = choices.FirstOrDefault(choice => choice.Value == setting.DcIndex),
                    AcNumber = (double?)setting.AcIndex ?? double.NaN,
                    DcNumber = (double?)setting.DcIndex ?? double.NaN
                });
            }
            return rows;
        }

        private void UpdateHeader()
        {
            EditorTitle.Text = $"Edit {_schemeName}";
            EditorHint.Text = PowerPlanService.IsWindowsDefaultScheme(_schemeId)
                ? "Windows default plans are protected. Adjust values, then use Save as to create your own plan."
                : _autoSave
                    ? "Changes to this new plan save automatically. Save as makes another copy."
                    : "Adjust AC and battery values, then save this plan or create a copy.";
            CopyNameBox.Text = $"{_schemeName} copy";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text) && CategoryList.SelectedIndex > 0)
            {
                _updatingCategories = true;
                CategoryList.SelectedIndex = 0;
                _updatingCategories = false;
            }
            RenderRows();
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading && !_updatingCategories) RenderRows();
        }

        private void RenderRows()
        {
            string query = SearchBox.Text.Trim();
            IEnumerable<PowerSettingEditRow> matches = _rows;
            if (query.Length == 0 && CategoryList.SelectedItem is PowerSettingCategoryView { IsAll: false } category)
            {
                matches = matches.Where(row => row.Setting.GroupName == category.Name);
            }
            if (query.Length > 0)
            {
                matches = matches.Where(row =>
                    row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Setting.GroupName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.SettingId.Contains(query, StringComparison.OrdinalIgnoreCase));
            }
            PowerSettingEditRow[] visible = matches.ToArray();
            GroupsList.ItemsSource = visible.GroupBy(row => row.Setting.GroupName)
                .Select(group => new PowerSettingEditGroup(group.Key, group.ToArray()))
                .ToArray();
            SettingsScroll.ChangeView(null, 0, null);
            if (!_loading) StatusText.Text = $"{visible.Length} of {_rows.Count} settings";
        }

        private async void Choice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || sender is not ComboBox box ||
                box.DataContext is not PowerSettingEditRow row) return;
            bool onAc = box.Name == "AcChoiceBox";
            if (onAc) row.AcChoice = box.SelectedItem as PowerSettingChoice;
            else row.DcChoice = box.SelectedItem as PowerSettingChoice;
            await HandleChangedValueAsync(row, onAc);
        }

        private async void Number_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading || sender is not NumberBox box ||
                box.DataContext is not PowerSettingEditRow row) return;
            bool onAc = box.Name == "AcNumberBox";
            if (onAc) row.AcNumber = box.Value;
            else row.DcNumber = box.Value;
            await HandleChangedValueAsync(row, onAc);
        }

        private async Task HandleChangedValueAsync(PowerSettingEditRow row, bool onAc)
        {
            ErrorText.Text = string.Empty;
            try
            {
                PowerPlanValueChange? change = BuildChange(row, onAc);
                if (change is null) return;
                if (_autoSave && !PowerPlanService.IsWindowsDefaultScheme(_schemeId))
                {
                    await SaveChangesAsync(new[] { change });
                }
                else
                {
                    RefreshSaveState();
                }
            }
            catch (Exception exception)
            {
                ErrorText.Text = exception.Message;
            }
        }

        private static PowerPlanValueChange? BuildChange(PowerSettingEditRow row, bool onAc)
        {
            uint? oldValue = onAc ? row.SavedAc : row.SavedDc;
            uint? newValue = row.GetValue(onAc);
            if (oldValue is not uint previous || newValue is not uint value || previous == value)
                return null;
            return new PowerPlanValueChange(row.Setting.SubgroupId, row.Setting.SettingId,
                onAc, previous, value);
        }

        private List<PowerPlanValueChange> GetPendingChanges()
        {
            List<PowerPlanValueChange> changes = new();
            foreach (PowerSettingEditRow row in _rows)
            {
                if (BuildChange(row, true) is PowerPlanValueChange ac) changes.Add(ac);
                if (BuildChange(row, false) is PowerPlanValueChange dc) changes.Add(dc);
            }
            return changes;
        }

        private void RefreshSaveState()
        {
            SaveAsButton.IsEnabled = !_loading && _rows.Count > 0;
            try
            {
                int pending = GetPendingChanges().Count;
                SaveButton.IsEnabled = !_loading && pending > 0 &&
                    !PowerPlanService.IsWindowsDefaultScheme(_schemeId);
                SaveButton.Content = _autoSave ? "Save pending" : "Save changes";
                if (pending > 0) StatusText.Text = $"{pending} unsaved change{(pending == 1 ? "" : "s")}";
            }
            catch (Exception exception)
            {
                SaveButton.IsEnabled = false;
                ErrorText.Text = exception.Message;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveChangesAsync(GetPendingChanges()); }
            catch (Exception exception) { ErrorText.Text = exception.Message; }
        }

        private async Task SaveChangesAsync(IReadOnlyList<PowerPlanValueChange> requested)
        {
            if (requested.Count == 0) return;
            await _saveGate.WaitAsync();
            try
            {
                List<PowerPlanValueChange> changes = new();
                foreach (PowerPlanValueChange change in requested)
                {
                    PowerSettingEditRow? row = _rows.FirstOrDefault(item =>
                        item.Setting.SubgroupId == change.SubgroupId &&
                        item.Setting.SettingId == change.SettingId);
                    uint? saved = change.OnAc ? row?.SavedAc : row?.SavedDc;
                    if (saved is uint previous && previous != change.Value)
                    {
                        changes.Add(change with { PreviousValue = previous });
                    }
                }
                if (changes.Count == 0) return;
                BusyRing.IsActive = true;
                BusyRing.Visibility = Visibility.Visible;
                StatusText.Text = "Saving changes...";
                await _service.SavePlanValuesAsync(_schemeId, changes);
                foreach (PowerPlanValueChange change in changes)
                {
                    PowerSettingEditRow row = _rows.First(item =>
                        item.Setting.SubgroupId == change.SubgroupId &&
                        item.Setting.SettingId == change.SettingId);
                    if (change.OnAc) row.SavedAc = change.Value;
                    else row.SavedDc = change.Value;
                }
                PlanChanged = true;
                ErrorText.Text = string.Empty;
                StatusText.Text = "Saved to Windows";
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Could not save power-plan settings.");
                ErrorText.Text = exception.Message;
            }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                RefreshSaveState();
                _saveGate.Release();
            }
        }

        private async void ConfirmSaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            string name = CopyNameBox.Text.Trim();
            if (name.Length == 0)
            {
                ErrorText.Text = "Enter a name for the new plan.";
                return;
            }
            SaveAsFlyout.Hide();
            await _saveGate.WaitAsync();
            try
            {
                List<PowerPlanValueChange> changes = GetPendingChanges();
                BusyRing.IsActive = true;
                BusyRing.Visibility = Visibility.Visible;
                StatusText.Text = "Creating a copy...";
                InstalledPowerPlan copy = await _service.DuplicatePlanAsync(_schemeId, name, activate: true);
                _schemeId = copy.SchemeId;
                _schemeName = copy.Name;
                PlanChanged = true;
                UpdateHeader();
                if (changes.Count > 0)
                {
                    await _service.SavePlanValuesAsync(_schemeId, changes);
                    foreach (PowerPlanValueChange change in changes)
                    {
                        PowerSettingEditRow row = _rows.First(item =>
                            item.Setting.SubgroupId == change.SubgroupId &&
                            item.Setting.SettingId == change.SettingId);
                        if (change.OnAc) row.SavedAc = change.Value;
                        else row.SavedDc = change.Value;
                    }
                }
                ErrorText.Text = string.Empty;
                StatusText.Text = $"Saved as {_schemeName} and activated";
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Could not save a copy of the power plan.");
                ErrorText.Text = exception.Message;
            }
            finally
            {
                BusyRing.IsActive = false;
                BusyRing.Visibility = Visibility.Collapsed;
                RefreshSaveState();
                _saveGate.Release();
            }
        }
    }
}
