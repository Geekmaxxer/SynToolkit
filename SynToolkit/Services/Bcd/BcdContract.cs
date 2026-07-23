using System;
using System.Globalization;

namespace SynToolkit.Services.Bcd
{
    /// <summary>
    /// Well-known BCD object identifiers documented by Microsoft and used by SynToolkit.
    /// </summary>
    internal static class WellKnownObjectIdentifiers
    {
        internal const string Default = "{1CAE1EB7-A0DF-4D4D-9851-4860E34EF535}";
        internal const string GlobalSettings = "{7EA2E1AC-2E61-4728-AAA3-896D9D0A9F0E}";
        internal const string Current = "{FA926493-6F1C-4193-A414-58F0B2456D1E}";
    }

    /// <summary>
    /// BCD element identifiers used by the boot-configuration UI. Keeping the numeric
    /// identifiers avoids parsing localized BCDEdit output.
    /// </summary>
    internal static class WellKnownElementTypes
    {
        internal const uint AdvancedOptions = 0x16000040;
        internal const uint OptionsEdit = 0x16000041;
        internal const uint HighestMode = 0x16000054;
        internal const uint NoBootUxLogo = 0x16000067;
        internal const uint NoBootUxText = 0x16000068;
        internal const uint NoBootUxProgress = 0x16000069;
        internal const uint SafeBoot = 0x25000080;
        internal const uint SafeBootAlternateShell = 0x26000081;
        internal const uint BootMenuPolicyWinload = 0x250000C2;
        internal const uint BootStatusPolicy = 0x250000E0;
    }

    internal enum BcdElementValueKind
    {
        Integer = 0x5,
        Boolean = 0x6
    }

    /// <summary>
    /// Pure validation and conversion helpers shared by the WMI adapter and tests.
    /// </summary>
    internal static class BcdContract
    {
        internal static string NormalizeObjectIdentifier(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException("A BCD object identifier is required.", nameof(objectId));
            }

            string candidate = objectId.Trim();
            if (!Guid.TryParseExact(candidate, "B", out Guid identifier))
            {
                throw new ArgumentException(
                    "The BCD object identifier must be a GUID surrounded by braces.",
                    nameof(objectId));
            }

            return identifier.ToString("B", CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        internal static BcdElementValueKind GetValueKind(uint elementType)
        {
            uint format = (elementType >> 24) & 0xF;
            return format switch
            {
                (uint)BcdElementValueKind.Integer => BcdElementValueKind.Integer,
                (uint)BcdElementValueKind.Boolean => BcdElementValueKind.Boolean,
                _ => throw new NotSupportedException(
                    $"BCD element type 0x{elementType:X8} uses unsupported value format 0x{format:X}.")
            };
        }

        internal static string EscapeManagementPathKey(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("A WMI key cannot contain a null character.", nameof(value));
            }

            return value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        internal static bool TryConvertToUInt64(object value, out ulong result)
        {
            if (value is null)
            {
                result = 0;
                return false;
            }

            try
            {
                result = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception) when (
                exception is FormatException
                or InvalidCastException
                or OverflowException)
            {
                result = 0;
                return false;
            }
        }
    }
}
