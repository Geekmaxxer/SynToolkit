#nullable enable

using System;
using System.Runtime.InteropServices;

namespace SynToolkit.Services
{
    internal readonly record struct CpuLiveMetrics(uint? UtilizationPercent, uint? AverageFrequencyMHz);

    /// <summary>
    /// Samples only Windows' aggregate CPU counters. It does not start a process, issue WMI
    /// queries, or change any power setting, so it is safe to call from the Specs timer.
    /// </summary>
    internal sealed class CpuUsageSampler
    {
        private const int ProcessorInformation = 11;
        private ulong? _previousIdleTime;
        private ulong? _previousKernelTime;
        private ulong? _previousUserTime;

        internal CpuLiveMetrics Sample() => new(ReadUtilizationPercent(), ReadAverageFrequencyMHz());

        private uint? ReadUtilizationPercent()
        {
            if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
            {
                return null;
            }

            ulong idle = ToUInt64(idleTime);
            ulong kernel = ToUInt64(kernelTime);
            ulong user = ToUInt64(userTime);
            if (!_previousIdleTime.HasValue || !_previousKernelTime.HasValue || !_previousUserTime.HasValue)
            {
                _previousIdleTime = idle;
                _previousKernelTime = kernel;
                _previousUserTime = user;
                return null;
            }

            ulong totalDelta = (kernel - _previousKernelTime.Value) + (user - _previousUserTime.Value);
            ulong idleDelta = idle - _previousIdleTime.Value;
            _previousIdleTime = idle;
            _previousKernelTime = kernel;
            _previousUserTime = user;
            if (totalDelta == 0 || idleDelta > totalDelta)
            {
                return null;
            }

            return (uint)Math.Clamp(Math.Round((totalDelta - idleDelta) * 100d / totalDelta), 0, 100);
        }

        private static uint? ReadAverageFrequencyMHz()
        {
            int processorCount = Math.Max(Environment.ProcessorCount, 1);
            int structureSize = Marshal.SizeOf<ProcessorPowerInformation>();
            IntPtr buffer = Marshal.AllocHGlobal(checked(structureSize * processorCount));
            try
            {
                if (CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, buffer, checked((uint)(structureSize * processorCount))) != 0)
                {
                    return null;
                }

                ulong totalMHz = 0;
                int validProcessors = 0;
                for (int index = 0; index < processorCount; index++)
                {
                    IntPtr current = IntPtr.Add(buffer, checked(index * structureSize));
                    ProcessorPowerInformation processor = Marshal.PtrToStructure<ProcessorPowerInformation>(current);
                    if (processor.CurrentMhz == 0)
                    {
                        continue;
                    }

                    totalMHz += processor.CurrentMhz;
                    validProcessors++;
                }

                return validProcessors == 0 ? null : (uint)(totalMHz / (uint)validProcessors);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static ulong ToUInt64(FileTime value) => ((ulong)value.HighDateTime << 32) | value.LowDateTime;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

        [DllImport("PowrProf.dll")]
        private static extern uint CallNtPowerInformation(
            int informationLevel,
            IntPtr inputBuffer,
            uint inputBufferLength,
            IntPtr outputBuffer,
            uint outputBufferLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            internal uint LowDateTime;
            internal uint HighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessorPowerInformation
        {
            internal uint Number;
            internal uint MaxMhz;
            internal uint CurrentMhz;
            internal uint MhzLimit;
            internal uint MaxIdleState;
            internal uint CurrentIdleState;
        }
    }
}