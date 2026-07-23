#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using NvAPIWrapper.Native.DRS;

namespace SynToolkit.Services.NvidiaProfileInspector
{
    public sealed record NvidiaSettingApplyResult(uint SettingId, string SettingName, bool Applied, string? SkipReason);

    public sealed record NvidiaProfileApplyResult(string ProfileName, bool ProfileCreated, IReadOnlyList<NvidiaSettingApplyResult> Settings);

    /// <summary>
    /// Applies parsed .nip profiles to the live NVIDIA driver via NvAPIWrapper.Net
    /// (https://github.com/falahati/NvAPIWrapper, LGPL-3.0), a managed wrapper around NVIDIA's
    /// own (undocumented) NVAPI Driver Settings (DRS) API. This mirrors what NVIDIA Profile
    /// Inspector's own import does (find-or-create profile by name, add missing executables, set
    /// each setting, save), verified against Orbmu2k/nvidiaProfileInspector's
    /// Common/DrsImportService.cs and Common/Import/ImportExportUitl.cs.
    /// <para>
    /// NvAPIWrapper.Net's <see cref="DRSSettingType" /> only has 4 members (Integer, Binary,
    /// String, UnicodeString) — the real native NVAPI type NVDRS_QWORD_TYPE (64-bit values) has
    /// no equivalent here and cannot be written through this library. Its "String" (ANSI) setter
    /// path also unconditionally calls SetCurrentValueAsUnicodeString internally, which throws
    /// for anything that isn't UnicodeString. Both cases are confirmed by reading NvAPIWrapper's
    /// own DRSSettingV1.cs source, not assumed. Settings of these two kinds are skipped (not
    /// applied) and reported back rather than silently truncated or allowed to throw and abort
    /// the whole profile.
    /// </para>
    /// </summary>
    public static class NvidiaProfileApplyService
    {
        public static List<NvidiaProfileApplyResult> Apply(IReadOnlyList<NvidiaProfile> profiles)
        {
            List<NvidiaProfileApplyResult> results = new();

            NVIDIA.Initialize();
            try
            {
                using DriverSettingsSession session = DriverSettingsSession.CreateAndLoad();

                foreach (NvidiaProfile nvidiaProfile in profiles)
                {
                    DriverSettingsProfile? profile = session.FindProfileByName(nvidiaProfile.ProfileName);
                    bool profileCreated = false;
                    if (profile is null)
                    {
                        profile = DriverSettingsProfile.CreateProfile(session, nvidiaProfile.ProfileName);
                        profileCreated = true;
                    }

                    foreach (string executable in nvidiaProfile.Executeables)
                    {
                        if (profile.GetApplicationByName(executable) is null)
                        {
                            ProfileApplication.CreateApplication(profile, executable);
                        }
                    }

                    List<NvidiaSettingApplyResult> settingResults = new();
                    foreach (NvidiaProfileSetting setting in nvidiaProfile.Settings)
                    {
                        settingResults.Add(ApplySetting(profile, setting));
                    }

                    session.Save();
                    results.Add(new NvidiaProfileApplyResult(nvidiaProfile.ProfileName, profileCreated, settingResults));
                }
            }
            finally
            {
                NVIDIA.Unload();
            }

            return results;
        }

        private static NvidiaSettingApplyResult ApplySetting(DriverSettingsProfile profile, NvidiaProfileSetting setting)
        {
            string settingName = string.IsNullOrWhiteSpace(setting.SettingNameInfo)
                ? $"#{setting.SettingId:X}"
                : setting.SettingNameInfo;

            try
            {
                switch (setting.ValueType)
                {
                    case NvidiaSettingValueType.Dword:
                        profile.SetSetting(setting.SettingId, DRSSettingType.Integer, uint.Parse(setting.SettingValue, CultureInfo.InvariantCulture));
                        return new NvidiaSettingApplyResult(setting.SettingId, settingName, true, null);

                    case NvidiaSettingValueType.String:
                        profile.SetSetting(setting.SettingId, DRSSettingType.UnicodeString, setting.SettingValue);
                        return new NvidiaSettingApplyResult(setting.SettingId, settingName, true, null);

                    case NvidiaSettingValueType.Binary:
                        profile.SetSetting(setting.SettingId, DRSSettingType.Binary, Convert.FromBase64String(setting.SettingValue));
                        return new NvidiaSettingApplyResult(setting.SettingId, settingName, true, null);

                    case NvidiaSettingValueType.AnsiString:
                        return new NvidiaSettingApplyResult(
                            setting.SettingId,
                            settingName,
                            false,
                            "Skipped: 8-bit ANSI string settings can't be written through the NVIDIA API wrapper this feature uses (only Unicode string settings can).");

                    case NvidiaSettingValueType.Qword:
                        return new NvidiaSettingApplyResult(
                            setting.SettingId,
                            settingName,
                            false,
                            "Skipped: 64-bit (Qword) settings aren't supported by the NVIDIA API wrapper this feature uses.");

                    default:
                        return new NvidiaSettingApplyResult(setting.SettingId, settingName, false, "Skipped: unrecognized setting value type.");
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Applying NVIDIA setting {0:X} on profile '{1}' failed.", setting.SettingId, profile.Name);
                return new NvidiaSettingApplyResult(setting.SettingId, settingName, false, exception.Message);
            }
        }
    }
}
