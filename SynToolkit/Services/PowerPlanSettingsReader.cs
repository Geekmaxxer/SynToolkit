#nullable enable

using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SynToolkit.Utils;

namespace SynToolkit.Services
{
    public sealed record PowerPlanSettingInspection(
        Guid SubgroupId,
        Guid SettingId,
        string GroupName,
        string Name,
        string Description,
        uint? AcIndex,
        uint? DcIndex,
        string AcText,
        string DcText,
        uint? CurrentAcIndex,
        uint? CurrentDcIndex,
        string CurrentAcText,
        string CurrentDcText)
    {
        public bool IsDifferent =>
            (AcIndex.HasValue && CurrentAcIndex.HasValue && AcIndex != CurrentAcIndex) ||
            (DcIndex.HasValue && CurrentDcIndex.HasValue && DcIndex != CurrentDcIndex);

        public bool IsAbsentFromFile => AcIndex is null && DcIndex is null;
    }

    public sealed record PowerPlanInspection(
        string Name,
        string Description,
        IReadOnlyList<PowerPlanSettingInspection> Settings);

    public sealed record PowerSettingChoice(uint Value, string Label);

    public sealed record PowerSettingValueEditor(
        IReadOnlyList<PowerSettingChoice> Choices, uint Minimum, uint Maximum,
        uint Increment, string Units, bool HasRange);

    /// <summary>
    /// Based on the Power Settings Explorer tool made by Lumin.
    /// Reads a .pow export as a private registry hive. Opening the viewer does not
    /// import, install, or activate the scheme. Windows power APIs provide the same
    /// localized setting names and value labels used by Power Settings Explorer.
    /// </summary>
    public static class PowerPlanSettingsReader
    {
        private const uint KeyRead = 0x20019;
        private const uint ErrorSuccess = 0;
        private const uint ErrorFileNotFound = 2;
        private const uint ErrorMoreData = 234;
        private const uint ErrorNoMoreItems = 259;
        private const uint ErrorInsufficientBuffer = 122;
        private const uint AccessSubgroup = 17;
        private const uint AccessIndividualSetting = 18;
        private const string SosResourceName = "SynToolkit.PowerPlans.SOS.pow";
        private static readonly Guid NoSubgroupId = new("fea3413e-7e05-4911-9a71-700331f1c294");
        private static readonly BoundedCache<(Guid Group, Guid Setting), PowerSettingValueEditor> EditorMetadata = new(512);
        private static readonly BoundedCache<(Guid Group, Guid Setting), bool> RangedSettings = new(512);
        private static readonly BoundedCache<(NativeTextKind Kind, Guid Group, Guid Setting, int Index), string?> NativeText = new(4096);

        public static PowerPlanInspection ReadFile(
            string filePath,
            Guid? currentSchemeId,
            CancellationToken cancellationToken)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!string.Equals(Path.GetExtension(fullPath), ".pow", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Choose a Windows .pow power-plan file.");
            }

            FileInfo file = new(fullPath);
            if (!file.Exists || file.Length < 4 || file.Length > PowerPlanService.MaximumPlanFileBytes)
            {
                throw new InvalidDataException("The power-plan file is missing, empty, or larger than 64 MB.");
            }

            using (FileStream header = file.OpenRead())
            {
                Span<byte> signature = stackalloc byte[4];
                if (header.Read(signature) != 4 ||
                    signature[0] != (byte)'r' || signature[1] != (byte)'e' ||
                    signature[2] != (byte)'g' || signature[3] != (byte)'f')
                {
                    throw new InvalidDataException("This file is not a Windows power-plan export.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            // RegLoadAppKey may update hive container metadata even with read-only keys.
            // Use a private copy so inspecting a user's .pow cannot alter that file.
            string privatePath = Path.Combine(Path.GetTempPath(), $"SynToolkit-Inspect-{Guid.NewGuid():N}.pow");
            File.Copy(fullPath, privatePath);
            try
            {
                return ReadHive(privatePath, Path.GetFileNameWithoutExtension(fullPath),
                    currentSchemeId, cancellationToken);
            }
            finally
            {
                File.Delete(privatePath);
            }
        }

        private static PowerPlanInspection ReadHive(string filePath, string fallbackName,
            Guid? currentSchemeId, CancellationToken cancellationToken)
        {
            int result = RegLoadAppKey(filePath, out SafeRegistryHandle handle, KeyRead, 0, 0);
            if (result != 0)
            {
                throw new Win32Exception(result, "Windows could not read this power-plan file.");
            }

            using RegistryKey hive = RegistryKey.FromHandle(handle);
            string name = (hive.GetValue("FriendlyName") as string)?.Trim() ?? fallbackName;
            string description = (hive.GetValue("Description") as string)?.Trim() ?? string.Empty;
            var settings = new List<PowerPlanSettingInspection>();

            foreach (string keyName in hive.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(keyName, out Guid subgroupId))
                {
                    continue;
                }

                using RegistryKey? groupKey = hive.OpenSubKey(keyName, writable: false);
                if (groupKey is null)
                {
                    continue;
                }

                // Some exported settings (for example, plan personality) live
                // directly under the hive root rather than in a subgroup.
                if (HasValueIndex(groupKey))
                {
                    settings.Add(ReadSetting(groupKey, NoSubgroupId, subgroupId,
                        "Plan properties", currentSchemeId));
                }

                string groupName = ReadNativeText(NativeTextKind.GroupName, subgroupId, Guid.Empty)
                    ?? subgroupId.ToString("D");
                foreach (string settingKeyName in groupKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Guid.TryParse(settingKeyName, out Guid settingId))
                    {
                        continue;
                    }

                    using RegistryKey? settingKey = groupKey.OpenSubKey(settingKeyName, writable: false);
                    if (settingKey is not null && HasValueIndex(settingKey))
                    {
                        settings.Add(ReadSetting(settingKey, subgroupId, settingId,
                            groupName, currentSchemeId));
                    }
                }
            }

            if (settings.Count == 0)
            {
                throw new InvalidDataException("No power settings were found in this .pow file.");
            }

            HashSet<(Guid SubgroupId, Guid SettingId)> included = settings
                .Select(setting => (setting.SubgroupId, setting.SettingId))
                .ToHashSet();
            Guid? catalogSchemeId = currentSchemeId ?? TryGetActiveSchemeId();
            if (catalogSchemeId is Guid installedId)
            {
                foreach ((Guid subgroupId, Guid settingId, string groupName) in
                    EnumerateCatalog(installedId, cancellationToken))
                {
                    if (included.Add((subgroupId, settingId)))
                    {
                        settings.Add(CreateSetting(subgroupId, settingId, groupName,
                            null, null, currentSchemeId));
                    }
                }
            }

            settings = settings.Select(setting => setting with
            {
                AcText = setting.AcIndex.HasValue ? setting.AcText : "Not in .pow",
                DcText = setting.DcIndex.HasValue ? setting.DcText : "Not in .pow"
            }).ToList();

            return new PowerPlanInspection(name, description, settings
                .OrderBy(setting => GetGroupOrder(setting.GroupName))
                .ThenBy(setting => setting.GroupName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(setting => setting.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
        }

        public static PowerPlanInspection ReadInstalledScheme(
            Guid schemeId, string schemeName, CancellationToken cancellationToken)
        {
            List<PowerPlanSettingInspection> settings = new();
            foreach ((Guid subgroupId, Guid settingId, string groupName) in
                EnumerateCatalog(schemeId, cancellationToken))
            {
                uint? ac = ReadInstalledIndex(schemeId, subgroupId, settingId, onAc: true);
                uint? dc = ReadInstalledIndex(schemeId, subgroupId, settingId, onAc: false);
                settings.Add(CreateSetting(subgroupId, settingId, groupName,
                    ac, dc, null));
            }

            if (settings.Count == 0)
            {
                throw new InvalidOperationException("Windows did not return any settings for this plan.");
            }

            return new PowerPlanInspection(schemeName, string.Empty, settings
                .OrderBy(setting => GetGroupOrder(setting.GroupName))
                .ThenBy(setting => setting.GroupName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(setting => setting.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
        }

        private static IEnumerable<(Guid SubgroupId, Guid SettingId, string GroupName)> EnumerateCatalog(
            Guid schemeId, CancellationToken cancellationToken)
        {
            List<Guid> subgroupIds = new() { NoSubgroupId };
            subgroupIds.AddRange(EnumeratePowerGuids(schemeId, null, AccessSubgroup, cancellationToken));

            foreach (Guid subgroupId in subgroupIds.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string groupName = subgroupId == NoSubgroupId
                    ? "Plan properties"
                    : ReadNativeText(NativeTextKind.GroupName, subgroupId, Guid.Empty)
                        ?? subgroupId.ToString("D");
                foreach (Guid settingId in EnumeratePowerGuids(
                    schemeId, subgroupId, AccessIndividualSetting, cancellationToken))
                {
                    yield return (subgroupId, settingId, groupName);
                }
            }
        }

        private static Guid? TryGetActiveSchemeId()
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pointer) != ErrorSuccess ||
                pointer == IntPtr.Zero) return null;
            try { return Marshal.PtrToStructure<Guid>(pointer); }
            finally { LocalFree(pointer); }
        }

        private static IEnumerable<Guid> EnumeratePowerGuids(
            Guid schemeId, Guid? subgroupId, uint access, CancellationToken cancellationToken)
        {
            for (uint index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid resultGuid = Guid.Empty;
                uint size = (uint)Marshal.SizeOf<Guid>();
                uint result;
                if (subgroupId is Guid group)
                {
                    result = PowerEnumerateSetting(IntPtr.Zero, ref schemeId,
                        ref group, access, index, out resultGuid, ref size);
                }
                else
                {
                    result = PowerEnumerateSubgroup(IntPtr.Zero, ref schemeId,
                        IntPtr.Zero, access, index, out resultGuid, ref size);
                }
                if (result is ErrorNoMoreItems or ErrorFileNotFound) yield break;
                if (result != ErrorSuccess || size != Marshal.SizeOf<Guid>())
                {
                    throw new Win32Exception((int)result,
                        "Windows could not enumerate all power settings.");
                }
                yield return resultGuid;
            }
        }

        private static int GetGroupOrder(string name)
        {
            if (name.Equals("Plan properties", StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.Contains("Processor", StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.Contains("Display", StringComparison.OrdinalIgnoreCase)) return 2;
            if (name.Contains("Sleep", StringComparison.OrdinalIgnoreCase)) return 3;
            if (name.Contains("Hard disk", StringComparison.OrdinalIgnoreCase)) return 4;
            if (name.Contains("PCI Express", StringComparison.OrdinalIgnoreCase)) return 5;
            if (name.Contains("USB", StringComparison.OrdinalIgnoreCase)) return 6;
            if (name.Contains("Wireless", StringComparison.OrdinalIgnoreCase)) return 7;
            if (name.Contains("Multimedia", StringComparison.OrdinalIgnoreCase)) return 8;
            if (name.Contains("Battery", StringComparison.OrdinalIgnoreCase)) return 9;
            return 10;
        }

        public static PowerPlanInspection ReadBuiltIn(Guid? currentSchemeId, CancellationToken cancellationToken)
        {
            using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(SosResourceName)
                ?? throw new FileNotFoundException("The bundled SOS.pow resource is missing.");
            string tempPath = Path.Combine(Path.GetTempPath(), $"SynToolkit-Inspect-{Guid.NewGuid():N}.pow");
            try
            {
                using (FileStream destination = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    resource.CopyTo(destination);
                }
                return ReadFile(tempPath, currentSchemeId, cancellationToken);
            }
            finally
            {
                try { File.Delete(tempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static bool HasValueIndex(RegistryKey key) =>
            key.GetValue("ACSettingIndex") is not null || key.GetValue("DCSettingIndex") is not null;

        private static PowerPlanSettingInspection ReadSetting(
            RegistryKey key,
            Guid subgroupId,
            Guid settingId,
            string groupName,
            Guid? currentSchemeId)
        {
            uint? ac = ReadIndex(key, "ACSettingIndex");
            uint? dc = ReadIndex(key, "DCSettingIndex");
            return CreateSetting(subgroupId, settingId, groupName, ac, dc, currentSchemeId);
        }

        private static PowerPlanSettingInspection CreateSetting(
            Guid subgroupId, Guid settingId, string groupName,
            uint? ac, uint? dc, Guid? currentSchemeId)
        {
            string name = ReadNativeText(NativeTextKind.SettingName, subgroupId, settingId)
                ?? settingId.ToString("D");
            string description = ReadNativeText(NativeTextKind.Description, subgroupId, settingId)
                ?? string.Empty;
            string units = ReadNativeText(NativeTextKind.Units, subgroupId, settingId) ?? string.Empty;
            bool isRanged = IsRangedSetting(subgroupId, settingId);
            uint? currentAc = currentSchemeId is Guid schemeId
                ? ReadCurrentIndex(schemeId, subgroupId, settingId, onAc: true)
                : null;
            uint? currentDc = currentSchemeId is Guid currentId
                ? ReadCurrentIndex(currentId, subgroupId, settingId, onAc: false)
                : null;

            return new PowerPlanSettingInspection(
                subgroupId, settingId, groupName, name, description,
                ac, dc, FormatValue(subgroupId, settingId, ac, units, isRanged, name),
                FormatValue(subgroupId, settingId, dc, units, isRanged, name),
                currentAc, currentDc,
                FormatValue(subgroupId, settingId, currentAc, units, isRanged, name),
                FormatValue(subgroupId, settingId, currentDc, units, isRanged, name));
        }

        private static uint? ReadIndex(RegistryKey key, string name) => key.GetValue(name) switch
        {
            int value => unchecked((uint)value),
            long value when value >= 0 && value <= uint.MaxValue => (uint)value,
            byte[] bytes when bytes.Length >= sizeof(uint) => BitConverter.ToUInt32(bytes, 0),
            _ => null
        };

        private static uint? ReadCurrentIndex(Guid schemeId, Guid subgroupId, Guid settingId, bool onAc)
        {
            uint value = 0;
            uint result = onAc
                ? PowerReadACValueIndex(IntPtr.Zero, ref schemeId, ref subgroupId, ref settingId, ref value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref schemeId, ref subgroupId, ref settingId, ref value);
            return result == ErrorSuccess ? value : null;
        }

        private static uint? ReadInstalledIndex(Guid schemeId, Guid subgroupId, Guid settingId, bool onAc)
        {
            if (ReadCurrentIndex(schemeId, subgroupId, settingId, onAc) is uint value)
            {
                return value;
            }
            uint defaultValue = 0;
            uint result = onAc
                ? PowerReadACDefaultIndex(IntPtr.Zero, ref schemeId, ref subgroupId,
                    ref settingId, ref defaultValue)
                : PowerReadDCDefaultIndex(IntPtr.Zero, ref schemeId, ref subgroupId,
                    ref settingId, ref defaultValue);
            return result == ErrorSuccess ? defaultValue : null;
        }

        private static bool IsRangedSetting(Guid subgroupId, Guid settingId)
            => RangedSettings.GetOrAdd((subgroupId, settingId),
                () => ReadIsRangedSetting(subgroupId, settingId));

        private static bool ReadIsRangedSetting(Guid subgroupId, Guid settingId)
        {
            uint min = 0, max = 0, increment = 0;
            return PowerReadValueMin(IntPtr.Zero, ref subgroupId, ref settingId, ref min) == ErrorSuccess &&
                PowerReadValueMax(IntPtr.Zero, ref subgroupId, ref settingId, ref max) == ErrorSuccess &&
                PowerReadValueIncrement(IntPtr.Zero, ref subgroupId, ref settingId, ref increment) == ErrorSuccess;
        }

        public static PowerSettingValueEditor GetValueEditor(Guid subgroupId, Guid settingId)
            => EditorMetadata.GetOrAdd((subgroupId, settingId),
                () => ReadValueEditor(subgroupId, settingId));

        private static PowerSettingValueEditor ReadValueEditor(Guid subgroupId, Guid settingId)
        {
            string units = ReadNativeText(NativeTextKind.Units, subgroupId, settingId) ?? string.Empty;
            List<PowerSettingChoice> choices = new();
            for (int index = 0; index < 256; index++)
            {
                byte[] buffer = new byte[sizeof(uint)];
                uint size = (uint)buffer.Length;
                uint type;
                uint result = PowerReadPossibleValue(IntPtr.Zero, ref subgroupId,
                    ref settingId, out type, index, buffer, ref size);
                if (result != ErrorSuccess || type != (uint)RegistryValueKind.DWord ||
                    size != sizeof(uint)) break;

                uint value = BitConverter.ToUInt32(buffer, 0);
                string label = ReadNativeText(NativeTextKind.PossibleValue,
                    subgroupId, settingId, index) ?? value.ToString();
                choices.Add(new PowerSettingChoice(value, $"{label} ({value})"));
            }

            uint minimum = 0, maximum = uint.MaxValue, increment = 1;
            bool hasRange = false;
            if (choices.Count == 0)
            {
                hasRange = PowerReadValueMin(IntPtr.Zero, ref subgroupId, ref settingId, ref minimum) == ErrorSuccess &&
                    PowerReadValueMax(IntPtr.Zero, ref subgroupId, ref settingId, ref maximum) == ErrorSuccess &&
                    PowerReadValueIncrement(IntPtr.Zero, ref subgroupId, ref settingId, ref increment) == ErrorSuccess;
                if (!hasRange) { minimum = 0; maximum = uint.MaxValue; increment = 1; }
                if (maximum < minimum) maximum = uint.MaxValue;
                if (increment == 0) increment = 1;
            }
            return new PowerSettingValueEditor(choices.ToArray(), minimum, maximum, increment, units, hasRange);
        }

        private static string FormatValue(
            Guid subgroupId, Guid settingId, uint? value, string units, bool isRanged, string settingName)
        {
            if (value is not uint index)
            {
                return "—";
            }

            string? label = !isRanged && index <= int.MaxValue
                ? ReadNativeText(NativeTextKind.PossibleValue, subgroupId, settingId, (int)index)
                : null;
            if (!string.IsNullOrWhiteSpace(label) &&
                !string.Equals(label, settingName, StringComparison.OrdinalIgnoreCase))
            {
                return $"{label} ({index})";
            }

            return string.IsNullOrWhiteSpace(units) ? index.ToString() : $"{index} {units}";
        }

        private enum NativeTextKind { GroupName, SettingName, Description, Units, PossibleValue }

        private static string? ReadNativeText(
            NativeTextKind kind, Guid subgroupId, Guid settingId, int possibleIndex = 0)
            => NativeText.GetOrAdd((kind, subgroupId, settingId, possibleIndex),
                () => ReadNativeTextCore(kind, subgroupId, settingId, possibleIndex));

        private static string? ReadNativeTextCore(
            NativeTextKind kind, Guid subgroupId, Guid settingId, int possibleIndex)
        {
            byte[] buffer = new byte[512];
            uint byteCount = (uint)buffer.Length;
            uint result = CallNativeText(kind, ref subgroupId, ref settingId,
                possibleIndex, buffer, ref byteCount);
            if ((result == ErrorMoreData || result == ErrorInsufficientBuffer) &&
                byteCount > buffer.Length && byteCount <= 65536)
            {
                buffer = new byte[byteCount];
                result = CallNativeText(kind, ref subgroupId, ref settingId,
                    possibleIndex, buffer, ref byteCount);
            }

            if (result != ErrorSuccess || byteCount < 2)
            {
                return null;
            }

            string value = Encoding.Unicode.GetString(buffer, 0,
                (int)Math.Min(byteCount, (uint)buffer.Length)).TrimEnd('\0').Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static uint CallNativeText(
            NativeTextKind kind,
            ref Guid subgroupId,
            ref Guid settingId,
            int possibleIndex,
            byte[] buffer,
            ref uint byteCount) => kind switch
        {
            NativeTextKind.GroupName => PowerReadGroupName(IntPtr.Zero, IntPtr.Zero,
                ref subgroupId, IntPtr.Zero, buffer, ref byteCount),
            NativeTextKind.SettingName => PowerReadSettingName(IntPtr.Zero, IntPtr.Zero,
                ref subgroupId, ref settingId, buffer, ref byteCount),
            NativeTextKind.Description => PowerReadDescription(IntPtr.Zero, IntPtr.Zero,
                ref subgroupId, ref settingId, buffer, ref byteCount),
            NativeTextKind.Units => PowerReadValueUnitsSpecifier(IntPtr.Zero,
                ref subgroupId, ref settingId, buffer, ref byteCount),
            NativeTextKind.PossibleValue => PowerReadPossibleFriendlyName(IntPtr.Zero,
                ref subgroupId, ref settingId, possibleIndex, buffer, ref byteCount),
            _ => 1
        };

        [DllImport("advapi32.dll", EntryPoint = "RegLoadAppKeyW", CharSet = CharSet.Unicode)]
        private static extern int RegLoadAppKey(
            string file, out SafeRegistryHandle key, uint access, uint options, uint reserved);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadGroupName(
            IntPtr root, IntPtr scheme, ref Guid subgroup, IntPtr setting,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadSettingName(
            IntPtr root, IntPtr scheme, ref Guid subgroup, ref Guid setting,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadDescription", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadDescription(
            IntPtr root, IntPtr scheme, ref Guid subgroup, ref Guid setting,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadValueUnitsSpecifier", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadValueUnitsSpecifier(
            IntPtr root, ref Guid subgroup, ref Guid setting,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadPossibleFriendlyName", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadPossibleFriendlyName(
            IntPtr root, ref Guid subgroup, ref Guid setting, int index,
            byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadPossibleValue")]
        private static extern uint PowerReadPossibleValue(
            IntPtr root, ref Guid subgroup, ref Guid setting, out uint type,
            int index, byte[] buffer, ref uint byteCount);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadValueMin(
            IntPtr root, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadValueMax(
            IntPtr root, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadValueIncrement(
            IntPtr root, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(
            IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(
            IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadACDefaultIndex")]
        private static extern uint PowerReadACDefaultIndex(
            IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadDCDefaultIndex")]
        private static extern uint PowerReadDCDefaultIndex(
            IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, ref uint value);

        [DllImport("powrprof.dll", EntryPoint = "PowerEnumerate")]
        private static extern uint PowerEnumerateSubgroup(
            IntPtr root, ref Guid scheme, IntPtr subgroup, uint access, uint index,
            out Guid value, ref uint byteCount);

        [DllImport("powrprof.dll", EntryPoint = "PowerEnumerate")]
        private static extern uint PowerEnumerateSetting(
            IntPtr root, ref Guid scheme, ref Guid subgroup, uint access, uint index,
            out Guid value, ref uint byteCount);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr schemeId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr pointer);
    }
}
