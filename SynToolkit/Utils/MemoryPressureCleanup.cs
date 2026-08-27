using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Utils
{
    internal static class MemoryPressureCleanup
    {
        private const int NavigationCountBeforeCollectionAttempt = 4;
        private const long PrivateBytesThreshold = 230L * 1024 * 1024;
        private const long ManagedBytesThreshold = 96L * 1024 * 1024;
        private const int CollectionCooldownMilliseconds = 10_000;
        private const int IdleDelayMilliseconds = 750;

        private static int _navigationCount;
        private static int _collectionScheduled;
        private static long _lastCollectionAttemptTickCount;

        public static bool TryScheduleAfterNavigation()
        {
            if (Interlocked.Increment(ref _navigationCount) < NavigationCountBeforeCollectionAttempt)
            {
                return false;
            }

            Interlocked.Exchange(ref _navigationCount, 0);
            return TryScheduleIfOverBudget();
        }

        public static bool TryScheduleIfOverBudget()
        {
            if (!IsMemoryPressureHigh())
            {
                return false;
            }

            long now = Environment.TickCount64;
            long previousAttempt = Volatile.Read(ref _lastCollectionAttemptTickCount);
            if (now - previousAttempt < CollectionCooldownMilliseconds ||
                Interlocked.CompareExchange(ref _collectionScheduled, 1, 0) != 0)
            {
                return false;
            }

            Volatile.Write(ref _lastCollectionAttemptTickCount, now);
            _ = CollectAfterIdleAsync();
            return true;
        }

        private static bool IsMemoryPressureHigh()
        {
            if (GC.GetTotalMemory(forceFullCollection: false) >= ManagedBytesThreshold)
            {
                return true;
            }

            try
            {
                using Process process = Process.GetCurrentProcess();
                return process.PrivateMemorySize64 >= PrivateBytesThreshold;
            }
            catch
            {
                return false;
            }
        }

        private static async Task CollectAfterIdleAsync()
        {
            try
            {
                await Task.Delay(IdleDelayMilliseconds).ConfigureAwait(false);
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
            }
            finally
            {
                Volatile.Write(ref _collectionScheduled, 0);
            }
        }
    }
}
