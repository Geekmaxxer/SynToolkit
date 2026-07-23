using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SynToolkit.Utils
{
    /// <summary>
    /// Reads and writes the active Windows power scheme through PowrProf.dll.
    /// These APIs are locale-independent and avoid parsing powercfg output.
    /// </summary>
    public static class PowerSettingsHelper
    {
        private const uint ERROR_SUCCESS = 0;

        public static (uint AcValue, uint DcValue) ReadCurrentValues(Guid subgroup, Guid setting)
        {
            Guid scheme = GetActiveScheme();
            uint acResult = PowerReadACValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out uint acValue);
            ThrowIfFailed(acResult, "read the AC power setting");

            uint dcResult = PowerReadDCValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out uint dcValue);
            ThrowIfFailed(dcResult, "read the DC power setting");

            return (acValue, dcValue);
        }

        public static void WriteCurrentValues(Guid subgroup, Guid setting, uint acValue, uint dcValue)
        {
            Guid scheme = GetActiveScheme();
            (uint originalAcValue, uint originalDcValue) = ReadValues(scheme, subgroup, setting);

            try
            {
                WriteValues(scheme, subgroup, setting, acValue, dcValue);
                ActivateScheme(scheme);

                (uint actualAcValue, uint actualDcValue) = ReadValues(scheme, subgroup, setting);
                if (actualAcValue != acValue || actualDcValue != dcValue)
                {
                    throw new InvalidOperationException("Windows did not retain both requested power-setting values.");
                }
            }
            catch (Exception writeException)
            {
                try
                {
                    WriteValues(scheme, subgroup, setting, originalAcValue, originalDcValue);
                    ActivateScheme(scheme);
                }
                catch (Exception rollbackException)
                {
                    throw new InvalidOperationException(
                        "Unable to update the power setting, and Windows could not restore its previous values.",
                        new AggregateException(writeException, rollbackException));
                }

                throw;
            }
        }

        private static (uint AcValue, uint DcValue) ReadValues(Guid scheme, Guid subgroup, Guid setting)
        {
            uint acResult = PowerReadACValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out uint acValue);
            ThrowIfFailed(acResult, "read the AC power setting");

            uint dcResult = PowerReadDCValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                out uint dcValue);
            ThrowIfFailed(dcResult, "read the DC power setting");

            return (acValue, dcValue);
        }

        private static void WriteValues(
            Guid scheme,
            Guid subgroup,
            Guid setting,
            uint acValue,
            uint dcValue)
        {
            uint acResult = PowerWriteACValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                acValue);
            ThrowIfFailed(acResult, "write the AC power setting");

            uint dcResult = PowerWriteDCValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                dcValue);
            ThrowIfFailed(dcResult, "write the DC power setting");
        }

        private static void ActivateScheme(Guid scheme)
        {
            uint activateResult = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            ThrowIfFailed(activateResult, "activate the updated power scheme");
        }

        public static Guid GetActiveScheme()
        {
            uint result = PowerGetActiveScheme(IntPtr.Zero, out IntPtr schemePointer);
            ThrowIfFailed(result, "read the active power scheme");

            if (schemePointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Windows returned an empty active power scheme.");
            }

            try
            {
                return Marshal.PtrToStructure<Guid>(schemePointer);
            }
            finally
            {
                _ = LocalFree(schemePointer);
            }
        }

        private static void ThrowIfFailed(uint result, string operation)
        {
            if (result != ERROR_SUCCESS)
            {
                throw new Win32Exception(unchecked((int)result), $"Unable to {operation}.");
            }
        }

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid powerSettingGuid,
            out uint acValueIndex);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid powerSettingGuid,
            out uint dcValueIndex);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerWriteACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid powerSettingGuid,
            uint acValueIndex);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerWriteDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid powerSettingGuid,
            uint dcValueIndex);

        [DllImport("PowrProf.dll", SetLastError = true)]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
