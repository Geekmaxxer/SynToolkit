using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace SynToolkit.Utils
{
    public class ServiceHelper
    {
        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SERVICE_QUERY_CONFIG = 0x0001;
        private const uint SERVICE_CHANGE_CONFIG = 0x0002;
        private const uint SERVICE_START = 0x0010;
        private const uint SERVICE_STOP = 0x0020;
        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        /// <summary>
        /// Returns the startup type of a service
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <returns></returns>
        public static ServiceStartMode GetStartupType(string serviceName)
        {
            using ServiceController serviceController = new(serviceName);
            return serviceController.StartType;
        }

        public static bool TryGetStartupType(string serviceName, out ServiceStartMode startupType)
        {
            try
            {
                startupType = GetStartupType(serviceName);
                return true;
            }
            catch (InvalidOperationException)
            {
                startupType = default;
                return false;
            }
            catch (Win32Exception)
            {
                startupType = default;
                return false;
            }
        }

        public static bool TryGetStatus(string serviceName, out ServiceControllerStatus status)
        {
            try
            {
                using ServiceController serviceController = new(serviceName);
                status = serviceController.Status;
                return true;
            }
            catch (InvalidOperationException)
            {
                status = default;
                return false;
            }
            catch (Win32Exception)
            {
                status = default;
                return false;
            }
        }

        public static bool ServiceExists(string serviceName)
        {
            return TryGetStartupType(serviceName, out _);
        }

        /// <summary>
        /// Sets the startup type
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <param name="startupType">Startup type of the service</param>
        public static void SetStartupType(string serviceName, ServiceStartMode startupType)
        {
            IntPtr serviceManager = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (serviceManager == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the Service Control Manager.");
            }

            try
            {
                IntPtr service = OpenService(serviceManager, serviceName, SERVICE_CHANGE_CONFIG | SERVICE_QUERY_CONFIG);
                if (service == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open service '{serviceName}'.");
                }

                try
                {
                    if (!ChangeServiceConfig(
                        service,
                        SERVICE_NO_CHANGE,
                        (uint)startupType,
                        SERVICE_NO_CHANGE,
                        null,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        null,
                        null))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to configure service '{serviceName}'.");
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(serviceManager);
            }

            if (!TryGetStartupType(serviceName, out ServiceStartMode actual) || actual != startupType)
            {
                throw new InvalidOperationException($"Service '{serviceName}' did not retain startup type '{startupType}'.");
            }

            App.logger.Info($"Set {serviceName} startup type to {startupType}");
        }

        public static void SetDelayedAutoStart(string serviceName, bool delayed)
        {
            if (!TryGetStartupType(serviceName, out ServiceStartMode startupType)
                || startupType != ServiceStartMode.Automatic)
            {
                throw new InvalidOperationException(
                    $"Service '{serviceName}' must use Automatic startup before delayed auto-start can be configured.");
            }

            IntPtr serviceManager = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (serviceManager == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the Service Control Manager.");
            }

            try
            {
                IntPtr service = OpenService(serviceManager, serviceName, SERVICE_CHANGE_CONFIG);
                if (service == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open service '{serviceName}'.");
                }

                try
                {
                    ServiceDelayedAutoStartInfo delayedInfo = new() { DelayedAutoStart = delayed };
                    if (!ChangeServiceConfig2(service, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref delayedInfo))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            $"Unable to configure delayed auto-start for service '{serviceName}'.");
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(serviceManager);
            }

            if (GetDelayedAutoStart(serviceName) != delayed)
            {
                throw new InvalidOperationException(
                    $"Service '{serviceName}' did not retain delayed auto-start state '{delayed}'.");
            }
        }

        public static bool GetDelayedAutoStart(string serviceName)
        {
            if (!TryGetStartupType(serviceName, out ServiceStartMode startupType) ||
                startupType != ServiceStartMode.Automatic)
            {
                return false;
            }

            IntPtr serviceManager = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (serviceManager == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the Service Control Manager.");
            }

            try
            {
                IntPtr service = OpenService(serviceManager, serviceName, SERVICE_QUERY_CONFIG);
                if (service == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open service '{serviceName}'.");
                }

                try
                {
                    QueryServiceConfig2(
                        service,
                        SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                        IntPtr.Zero,
                        0,
                        out uint bytesNeeded);
                    int queryError = Marshal.GetLastWin32Error();
                    if (bytesNeeded == 0 || queryError != ERROR_INSUFFICIENT_BUFFER)
                    {
                        throw new Win32Exception(queryError, $"Unable to inspect delayed auto-start for service '{serviceName}'.");
                    }

                    IntPtr buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
                    try
                    {
                        if (!QueryServiceConfig2(
                            service,
                            SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                            buffer,
                            bytesNeeded,
                            out _))
                        {
                            throw new Win32Exception(
                                Marshal.GetLastWin32Error(),
                                $"Unable to inspect delayed auto-start for service '{serviceName}'.");
                        }

                        return Marshal.PtrToStructure<ServiceDelayedAutoStartInfo>(buffer).DelayedAutoStart;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(serviceManager);
            }
        }

        public static void StartService(string serviceName, TimeSpan? timeout = null)
        {
            using ServiceController serviceController = new(serviceName);
            serviceController.Refresh();

            if (serviceController.Status == ServiceControllerStatus.Running)
            {
                return;
            }

            if (serviceController.Status != ServiceControllerStatus.StartPending)
            {
                serviceController.Start();
            }

            serviceController.WaitForStatus(ServiceControllerStatus.Running, timeout ?? TimeSpan.FromSeconds(30));
            serviceController.Refresh();

            if (serviceController.Status != ServiceControllerStatus.Running)
            {
                throw new InvalidOperationException($"Service '{serviceName}' did not reach the Running state.");
            }
        }

        public static void StopService(string serviceName, TimeSpan? timeout = null)
        {
            using ServiceController serviceController = new(serviceName);
            serviceController.Refresh();

            if (serviceController.Status == ServiceControllerStatus.Stopped)
            {
                return;
            }

            if (serviceController.Status != ServiceControllerStatus.StopPending)
            {
                serviceController.Stop();
            }

            serviceController.WaitForStatus(ServiceControllerStatus.Stopped, timeout ?? TimeSpan.FromSeconds(30));
            serviceController.Refresh();

            if (serviceController.Status != ServiceControllerStatus.Stopped)
            {
                throw new InvalidOperationException($"Service '{serviceName}' did not reach the Stopped state.");
            }
        }

        /// <summary>
        /// Checks for a match
        /// </summary>
        /// <param name="serviceName">Name of the service to match</param>
        /// <param name="startupType">Startup type to match</param>
        /// <returns></returns>
        public static bool IsStartupTypeMatch(string serviceName, ServiceStartMode startupType)
        {
            return TryGetStartupType(serviceName, out ServiceStartMode actual) && actual == startupType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceDelayedAutoStartInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool DelayedAutoStart;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr serviceManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig(
            IntPtr service,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string loadOrderGroup,
            IntPtr tagId,
            string dependencies,
            string serviceStartName,
            string password,
            string displayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig2(
            IntPtr service,
            uint infoLevel,
            ref ServiceDelayedAutoStartInfo info);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceConfig2(
            IntPtr service,
            uint infoLevel,
            IntPtr buffer,
            uint bufferSize,
            out uint bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr serviceHandle);
    }
}
