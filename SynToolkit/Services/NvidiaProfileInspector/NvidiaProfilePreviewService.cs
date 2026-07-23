#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace SynToolkit.Services.NvidiaProfileInspector
{
    /// <summary>
    /// A single per-application NVIDIA driver setting from a .nip profile. Named with an
    /// "Nvidia" prefix throughout to avoid colliding with SynToolkit's own, unrelated
    /// Models.Profiles type (SynToolkit's own save/restore tweak-configuration profiles).
    /// </summary>
    public sealed class NvidiaProfileSetting
    {
        public string SettingNameInfo { get; set; } = string.Empty;

        [XmlElement("SettingID")]
        public uint SettingId { get; set; }

        // Lets the profile-creation UI bind a TextBox.Text (string) directly to the
        // underlying uint without a separate IValueConverter.
        [XmlIgnore]
        public string SettingIdText
        {
            get => SettingId.ToString();
            set => SettingId = uint.TryParse(value, out uint parsed) ? parsed : 0;
        }

        public string SettingValue { get; set; } = "0";

        public NvidiaSettingValueType ValueType { get; set; }

        // Lets the profile-creation UI bind a ComboBox of plain x:String items (safe for the
        // WinUI XAML compiler) instead of an x:Array of this project's own enum type, which
        // XamlCompiler.exe fails to compile with no diagnostic output whatsoever — confirmed by
        // direct bisection, not assumed.
        [XmlIgnore]
        public string ValueTypeText
        {
            get => ValueType.ToString();
            set => ValueType = Enum.TryParse(value, out NvidiaSettingValueType parsed) ? parsed : NvidiaSettingValueType.Dword;
        }
    }

    public enum NvidiaSettingValueType
    {
        Dword,
        AnsiString,
        String,
        Binary,
        Qword,
    }

    [XmlType("Profile")]
    public sealed class NvidiaProfile
    {
        public string ProfileName { get; set; } = string.Empty;

        [XmlArrayItem("string")]
        public List<string> Executeables { get; set; } = new();

        [XmlArrayItem("ProfileSetting")]
        public List<NvidiaProfileSetting> Settings { get; set; } = new();

        [XmlIgnore]
        public string ExecutablesSummary => Executeables.Count == 0
            ? "Base profile (applies to all applications)"
            : $"Applies to: {string.Join(", ", Executeables)}";
    }

    /// <summary>
    /// Reads and writes NVIDIA Profile Inspector .nip files. .nip files are a plain
    /// XmlSerializer dump of a List&lt;Profile&gt; with root element "ArrayOfProfile" (verified
    /// against a real-world .nip file, not assumed). Model shape ported from
    /// Orbmu2k/nvidiaProfileInspector's Common/Import types
    /// (https://github.com/Orbmu2k/nvidiaProfileInspector, MIT License). Applying a loaded
    /// profile to the live driver is handled separately by NvidiaProfileApplyService.
    /// </summary>
    public static class NvidiaProfilePreviewService
    {
        private static readonly XmlSerializer Serializer = new(typeof(List<NvidiaProfile>), new XmlRootAttribute("ArrayOfProfile"));

        public static List<NvidiaProfile> LoadProfiles(string nipFilePath)
        {
            if (string.IsNullOrWhiteSpace(nipFilePath) || !File.Exists(nipFilePath))
            {
                throw new FileNotFoundException("The selected .nip file does not exist or cannot be accessed.", nipFilePath);
            }

            List<NvidiaProfile>? profiles;
            try
            {
                using FileStream stream = File.OpenRead(nipFilePath);
                profiles = (List<NvidiaProfile>?)Serializer.Deserialize(stream);
            }
            catch (InvalidOperationException)
            {
                // Some real-world .nip files declare an encoding (typically utf-16) that
                // doesn't match how the file is actually saved (no BOM present), which throws
                // "There is no Unicode byte order mark" from the XML reader — confirmed against
                // an actual community-shared .nip file, not a hypothetical. Re-read as text
                // (auto-detects the real encoding) and parse from that instead.
                using StringReader textReader = new(File.ReadAllText(nipFilePath));
                using XmlReader xmlReader = XmlReader.Create(textReader);
                profiles = (List<NvidiaProfile>?)Serializer.Deserialize(xmlReader);
            }

            return profiles ?? throw new InvalidDataException("The selected file did not contain any NVIDIA Profile Inspector profiles.");
        }

        public static void SaveProfiles(List<NvidiaProfile> profiles, string nipFilePath)
        {
            using FileStream stream = File.Create(nipFilePath);
            using XmlWriter xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = System.Text.Encoding.Unicode });
            Serializer.Serialize(xmlWriter, profiles);
        }
    }
}
