#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SynToolkit.Services
{
    /// <summary>
    /// Pure validation rules for importing profiles from the legacy Kwanteks
    /// Syntoolkit 1.5.0 installation. This class never reads the registry,
    /// touches the file system, or applies a profile.
    /// </summary>
    public static class LegacyProfileMigrationPolicy
    {
        public const int MaximumProfileBytes = 1024 * 1024;
        private const int MaximumProfileNameLength = 128;
        private const int MaximumConfigurationEntries = 512;
        private const int MaximumMultiConfigurationEntries = 128;
        private const int MaximumSettingTextLength = 256;

        private static readonly HashSet<string> RootPropertyNames = new(StringComparer.Ordinal)
        {
            "Name",
            "Config",
            "MultiConfig"
        };

        private static readonly HashSet<string> MultiConfigurationPropertyNames = new(StringComparer.Ordinal)
        {
            "Key",
            "Value"
        };

        public static bool IsExactLegacyRegistration(
            string? displayName,
            string? publisher,
            string? displayVersion)
        {
            return string.Equals(displayName?.Trim(), "Syntoolkit", StringComparison.OrdinalIgnoreCase)
                && string.Equals(publisher?.Trim(), "Kwanteks", StringComparison.OrdinalIgnoreCase)
                && string.Equals(displayVersion?.Trim(), "1.5.0", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryValidateProfile(
            byte[] utf8Json,
            string fileName,
            out string rejectionReason)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);

            rejectionReason = string.Empty;
            if (!IsPlainJsonFileName(fileName))
            {
                rejectionReason = "the file does not have a plain .json filename";
                return false;
            }

            if (utf8Json.Length == 0 || utf8Json.Length > MaximumProfileBytes)
            {
                rejectionReason = "the file is empty or exceeds the profile size limit";
                return false;
            }

            ReadOnlyMemory<byte> json = utf8Json;
            if (utf8Json.Length >= 3
                && utf8Json[0] == 0xEF
                && utf8Json[1] == 0xBB
                && utf8Json[2] == 0xBF)
            {
                json = utf8Json.AsMemory(3);
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    json,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 16
                    });

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    rejectionReason = "the JSON root is not an object";
                    return false;
                }

                if (!TryReadExactProperties(
                        document.RootElement,
                        RootPropertyNames,
                        out Dictionary<string, JsonElement> properties,
                        out rejectionReason))
                {
                    return false;
                }

                string? profileName = properties["Name"].ValueKind == JsonValueKind.String
                    ? properties["Name"].GetString()
                    : null;
                if (!IsSafeText(profileName, MaximumProfileNameLength)
                    || profileName!.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    rejectionReason = "the profile name is missing or invalid";
                    return false;
                }

                string expectedName = Path.GetFileNameWithoutExtension(fileName);
                if (!string.Equals(profileName, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    rejectionReason = "the profile name does not match its filename";
                    return false;
                }

                if (!TryValidateConfigurationArray(properties["Config"], out rejectionReason))
                {
                    return false;
                }

                return TryValidateMultiConfigurationArray(
                    properties["MultiConfig"],
                    out rejectionReason);
            }
            catch (JsonException)
            {
                rejectionReason = "the file is not well-formed JSON";
                return false;
            }
            catch (ArgumentException)
            {
                rejectionReason = "the profile contains an invalid value";
                return false;
            }
        }

        private static bool IsPlainJsonFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName)
                && string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
                && string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryValidateConfigurationArray(
            JsonElement element,
            out string rejectionReason)
        {
            rejectionReason = string.Empty;
            if (element.ValueKind != JsonValueKind.Array
                || element.GetArrayLength() > MaximumConfigurationEntries)
            {
                rejectionReason = "Config is not a bounded string array";
                return false;
            }

            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (!IsSafeText(value, MaximumSettingTextLength) || !seen.Add(value!))
                {
                    rejectionReason = "Config contains an invalid or duplicate entry";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateMultiConfigurationArray(
            JsonElement element,
            out string rejectionReason)
        {
            rejectionReason = string.Empty;
            if (element.ValueKind != JsonValueKind.Array
                || element.GetArrayLength() > MaximumMultiConfigurationEntries)
            {
                rejectionReason = "MultiConfig is not a bounded array";
                return false;
            }

            HashSet<string> seenKeys = new(StringComparer.Ordinal);
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !TryReadExactProperties(
                        item,
                        MultiConfigurationPropertyNames,
                        out Dictionary<string, JsonElement> properties,
                        out _))
                {
                    rejectionReason = "MultiConfig contains an invalid object";
                    return false;
                }

                string? key = properties["Key"].ValueKind == JsonValueKind.String
                    ? properties["Key"].GetString()
                    : null;
                string? value = properties["Value"].ValueKind == JsonValueKind.String
                    ? properties["Value"].GetString()
                    : null;
                if (!IsSafeText(key, MaximumSettingTextLength)
                    || !IsSafeText(value, MaximumSettingTextLength)
                    || !seenKeys.Add(key!))
                {
                    rejectionReason = "MultiConfig contains an invalid or duplicate setting";
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadExactProperties(
            JsonElement element,
            HashSet<string> expectedNames,
            out Dictionary<string, JsonElement> properties,
            out string rejectionReason)
        {
            properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            rejectionReason = string.Empty;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!expectedNames.Contains(property.Name)
                    || !properties.TryAdd(property.Name, property.Value))
                {
                    rejectionReason = "the JSON schema contains an unknown or duplicate property";
                    return false;
                }
            }

            if (properties.Count != expectedNames.Count)
            {
                rejectionReason = "the JSON schema is incomplete";
                return false;
            }

            foreach (string expectedName in expectedNames)
            {
                if (!properties.ContainsKey(expectedName))
                {
                    rejectionReason = "the JSON schema is incomplete";
                    return false;
                }
            }

            return true;
        }

        private static bool IsSafeText(string? value, int maximumLength)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= maximumLength
                && string.Equals(value, value.Trim(), StringComparison.Ordinal)
                && !value.Any(char.IsControl);
        }
    }
}
