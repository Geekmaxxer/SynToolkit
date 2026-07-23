using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    public class WindowsFirewallConfigurationService : IConfigurationService
    {
        private const string BFE_SERVICE_NAME = "BFE";
        private const string MPSSVC_SERVICE_NAME = "mpssvc";

        private readonly ConfigurationStore _windowsFirewallConfigurationStore;

        public WindowsFirewallConfigurationService(
            [FromKeyedServices("WindowsFirewall")] ConfigurationStore windowsFirewallConfigurationStore)
        {
            _windowsFirewallConfigurationStore = windowsFirewallConfigurationStore;
        }

        public void Disable()
        {
            // Microsoft explicitly recommends leaving BFE and mpssvc running. Disable the
            // firewall profiles instead of breaking the services and their dependants.
            WindowsFirewallPolicyHelper.SetAllProfilesEnabled(false);

            _windowsFirewallConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            ServiceHelper.SetStartupType(BFE_SERVICE_NAME, ServiceStartMode.Automatic);
            ServiceHelper.SetStartupType(MPSSVC_SERVICE_NAME, ServiceStartMode.Automatic);
            ServiceHelper.StartService(BFE_SERVICE_NAME);
            ServiceHelper.StartService(MPSSVC_SERVICE_NAME);

            WindowsFirewallPolicyHelper.SetAllProfilesEnabled(true);

            _windowsFirewallConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            try
            {
                bool servicesAvailable =
                    ServiceHelper.TryGetStartupType(BFE_SERVICE_NAME, out ServiceStartMode bfeStart)
                    && bfeStart != ServiceStartMode.Disabled
                    && ServiceHelper.TryGetStartupType(MPSSVC_SERVICE_NAME, out ServiceStartMode mpsStart)
                    && mpsStart != ServiceStartMode.Disabled;

                bool servicesRunning =
                    ServiceHelper.TryGetStatus(BFE_SERVICE_NAME, out ServiceControllerStatus bfeStatus)
                    && bfeStatus == ServiceControllerStatus.Running
                    && ServiceHelper.TryGetStatus(MPSSVC_SERVICE_NAME, out ServiceControllerStatus mpsStatus)
                    && mpsStatus == ServiceControllerStatus.Running;

                return servicesAvailable
                    && servicesRunning
                    && WindowsFirewallPolicyHelper.AreAllProfilesEnabled();
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to inspect Windows Firewall state.");
                return false;
            }
        }
    }

    /// <summary>
    /// Uses the documented INetFwPolicy2 automation object. This reports the effective
    /// firewall policy and the invariant rule-group identifiers on every Windows language.
    /// </summary>
    internal static class WindowsFirewallPolicyHelper
    {
        private static readonly int[] AllProfiles = { 1, 2, 4 }; // Domain, Private, Public

        public static bool AreAllProfilesEnabled()
        {
            return AreAllProfilesInState(true);
        }

        private static bool AreAllProfilesInState(bool expectedEnabled)
        {
            object policyObject = CreatePolicy();
            try
            {
                dynamic policy = policyObject;
                foreach (int profile in AllProfiles)
                {
                    if ((bool)policy.FirewallEnabled[profile] != expectedEnabled)
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                ReleaseComObject(policyObject);
            }
        }

        public static void SetAllProfilesEnabled(bool enabled)
        {
            object policyObject = CreatePolicy();
            try
            {
                dynamic policy = policyObject;
                foreach (int profile in AllProfiles)
                {
                    policy.FirewallEnabled[profile] = enabled;
                }
            }
            finally
            {
                ReleaseComObject(policyObject);
            }

            if (!AreAllProfilesInState(enabled))
            {
                throw new InvalidOperationException("Windows Firewall profiles did not retain the requested state.");
            }
        }

        public static bool AreRuleGroupsEnabled(params string[] groupIdentifiers)
        {
            return AreRuleGroupsInState(true, groupIdentifiers);
        }

        public static bool AreRuleGroupsDisabled(params string[] groupIdentifiers)
        {
            return AreRuleGroupsInState(false, groupIdentifiers);
        }

        public static void SetRuleGroupsEnabled(bool enabled, params string[] groupIdentifiers)
        {
            HashSet<string> requestedGroups = new(groupIdentifiers, StringComparer.OrdinalIgnoreCase);
            HashSet<string> discoveredGroups = new(StringComparer.OrdinalIgnoreCase);
            object policyObject = CreatePolicy();
            object rulesObject = null;

            try
            {
                dynamic policy = policyObject;
                rulesObject = policy.Rules;

                foreach (object ruleObject in (dynamic)rulesObject)
                {
                    try
                    {
                        dynamic rule = ruleObject;
                        string grouping = rule.Grouping as string;
                        if (grouping is null || !requestedGroups.Contains(grouping))
                        {
                            continue;
                        }

                        discoveredGroups.Add(grouping);
                        rule.Enabled = enabled;
                    }
                    finally
                    {
                        ReleaseComObject(ruleObject);
                    }
                }
            }
            finally
            {
                ReleaseComObject(rulesObject);
                ReleaseComObject(policyObject);
            }

            if (!discoveredGroups.SetEquals(requestedGroups)
                || !AreRuleGroupsInState(enabled, groupIdentifiers))
            {
                throw new InvalidOperationException("Windows Firewall rule groups did not retain the requested state.");
            }
        }

        private static bool AreRuleGroupsInState(bool expectedEnabled, params string[] groupIdentifiers)
        {
            HashSet<string> requestedGroups = new(groupIdentifiers, StringComparer.OrdinalIgnoreCase);
            HashSet<string> discoveredGroups = new(StringComparer.OrdinalIgnoreCase);
            bool allMatchingRulesHaveExpectedState = true;

            object policyObject = CreatePolicy();
            object rulesObject = null;

            try
            {
                dynamic policy = policyObject;
                rulesObject = policy.Rules;

                foreach (object ruleObject in (dynamic)rulesObject)
                {
                    try
                    {
                        dynamic rule = ruleObject;
                        string grouping = rule.Grouping as string;
                        if (grouping is null || !requestedGroups.Contains(grouping))
                        {
                            continue;
                        }

                        discoveredGroups.Add(grouping);
                        if ((bool)rule.Enabled != expectedEnabled)
                        {
                            allMatchingRulesHaveExpectedState = false;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(ruleObject);
                    }
                }

                return allMatchingRulesHaveExpectedState
                    && discoveredGroups.SetEquals(requestedGroups);
            }
            finally
            {
                ReleaseComObject(rulesObject);
                ReleaseComObject(policyObject);
            }
        }

        private static object CreatePolicy()
        {
            Type policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                ?? throw new PlatformNotSupportedException("Windows Firewall policy is unavailable.");

            return Activator.CreateInstance(policyType)
                ?? throw new InvalidOperationException("Unable to create the Windows Firewall policy object.");
        }

        private static void ReleaseComObject(object value)
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
    }
}
