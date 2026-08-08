using SynToolkit.Models;
using SynToolkit.Commands;
using SynToolkit.Services;
using SynToolkit.Services.Bcd;
using SynToolkit.Services.NvidiaProfileInspector;
using SynToolkit.Services.RadeonSlimmer;
using SynToolkit.Utils;
using System.ComponentModel;
using System.Text;

namespace SynToolkit.SystemInformationTests;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Run("Official AME registry path", OfficialRegistryPath);
        Run("No AME metadata", NoMetadata);
        Run("Official AME record", OfficialRecord);
        Run("Official AME binary timestamp", OfficialBinaryTimestamp);
        Run("Invalid AME timestamps are rejected", InvalidBinaryTimestamps);
        Run("Newest official AME record", NewestRecordWins);
        Run("OS overhaul precedes newer utility playbook", OverhaulPlaybookWins);
        Run("Fatal AME records do not identify the OS", FatalRecordIsIgnored);
        Run("Mixed timestamp metadata stays conflict-aware", MixedTimestampConflict);
        Run("Tied timestamp metadata stays deterministic", TiedTimestampConflict);
        Run("Official metadata precedes legacy", RegistryPrecedesLegacy);
        Run("Untimestamped conflicts stay explicit", UntimestampedConflict);
        Run("Malformed official entry is isolated", MalformedEntryIsolated);
        Run("Valid legacy playbook.conf", ValidLegacyConfiguration);
        Run("Latest numeric legacy folder wins", LatestLegacyDirectoryWins);
        Run("Invalid latest legacy folder falls back safely", InvalidLegacyDirectoryFallsBack);
        Run("XML external entities are prohibited", ExternalEntityIsProhibited);
        Run("Oversized legacy metadata is rejected", OversizedConfigurationIsRejected);
        Run("Empty normalized versions are omitted", EmptyNormalizedVersionIsOmitted);
        Run("Invalid AME versions are omitted", InvalidVersionsAreOmitted);
        Run("Same Playbook with conflicting versions stays explicit", SameNameVersionConflict);
        Run("BIOS version is not a custom OS", BiosVersionIsNotCustomWindows);
        Run("SynergyOS version is a custom OS", SynergyOsVersionIsCustomWindows);
        Run("Bare AME generations are not Playbooks", BareAmeGenerationIsNotPlaybook);
        Run("Named versioned Playbook is accepted", NamedVersionedPlaybookIsAccepted);
        Run("Current project metadata precedes legacy history", CurrentProjectMetadataPrecedesLegacy);
        Run("BCD identifiers are strict and canonical", BcdIdentifiersAreCanonical);
        Run("BCD element formats are derived from their numeric type", BcdElementFormatsAreTyped);
        Run("BCD WMI path keys are escaped", BcdWmiPathKeysAreEscaped);
        Run("BCD writes are read back and verified", BcdWritesAreVerified);
        Run("Deleting an absent BCD element is idempotent", BcdDeleteIsIdempotent);
        Run("Async commands prevent concurrent execution", AsyncCommandPreventsConcurrentExecution);
        Run("Async command failures are contained and logged", AsyncCommandFailureIsContained);
        Run("Batch file paths with spaces execute correctly", BatchFilePathsWithSpacesExecuteCorrectly);
        Run("Radeon bulk selections notify the UI", RadeonBulkSelectionsNotifyTheUi);
        Run("Legacy profile registration match is exact", LegacyRegistrationMatchIsExact);
        Run("Valid legacy profile JSON is accepted", ValidLegacyProfileIsAccepted);
        Run("Malformed legacy profiles are rejected", MalformedLegacyProfilesAreRejected);
        Run("Oversized legacy profiles are rejected", OversizedLegacyProfileIsRejected);
        Run("NVIDIA profile export preserves imported settings", NvidiaProfileExportRoundTrips);

        Console.WriteLine(_failures == 0
            ? "All SynToolkit service tests passed."
            : $"{_failures} SynToolkit service test(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void OfficialRegistryPath() =>
        Equal(
            @"SOFTWARE\AME\Playbooks\Applied",
            WindowsAmePlaybookMetadataSource.AppliedRegistryPath,
            "The detector must follow AME Wizard's official applied-Playbook hierarchy.");

    private static void BiosVersionIsNotCustomWindows()
    {
        CustomWindowsInformation? result = SystemInformationService.TryParseCustomWindowsMarker(
            "BIOS 1.2",
            "Windows OEM model");
        True(result is null, "A BIOS version must not be presented as a custom Windows build.");
    }

    private static void SynergyOsVersionIsCustomWindows()
    {
        CustomWindowsInformation? result = SystemInformationService.TryParseCustomWindowsMarker(
            "SynergyOS 1.5.1",
            "Windows OEM model");
        Equal("SynergyOS 1.5.1", result?.DisplayName, "A complete SynergyOS marker should be detected.");
        Equal("Windows OEM model", result?.Source, "The custom-Windows source should be preserved.");
    }

    private static void BareAmeGenerationIsNotPlaybook()
    {
        foreach (string candidate in new[] { "AME 10", "AME 11" })
        {
            PlaybookInformation? result = SystemInformationService.TryParseFallbackPlaybookMarker(
                candidate,
                "Windows OEM manufacturer");
            True(result is null, $"Bare OEM marker '{candidate}' must not be guessed as a Playbook.");
        }
    }

    private static void NamedVersionedPlaybookIsAccepted()
    {
        PlaybookInformation? result = SystemInformationService.TryParseFallbackPlaybookMarker(
            "SynergyOS Playbook 1.5.1",
            "Windows OEM model");
        Equal(PlaybookDetectionStatus.Detected, result?.Status, "A literal named and versioned Playbook should be detected.");
        Equal("SynergyOS Playbook", result?.Name, "The Playbook name should exclude trailing version text.");
        Equal("1.5.1", result?.Version, "The Playbook version should be preserved.");
    }

    private static void CurrentProjectMetadataPrecedesLegacy()
    {
        PlaybookInformation current = new(
            PlaybookDetectionStatus.Detected,
            "SynergyOS Playbook",
            "1.5.1",
            @"HKLM\SOFTWARE\SynergyOS\Playbook");
        PlaybookInformation legacy = new(
            PlaybookDetectionStatus.Detected,
            "Legacy AME",
            "0.7",
            @"C:\ProgramData\AME\AppliedPlaybooks\1\playbook.conf");

        PlaybookInformation selected = SystemInformationService.PreferCurrentPlaybook(current, legacy);
        Equal(current, selected, "Current project-specific metadata must take priority over legacy history.");

        PlaybookInformation notDetected = new(PlaybookDetectionStatus.NotDetected, null, null, null);
        PlaybookInformation legacyFallback = SystemInformationService.PreferCurrentPlaybook(notDetected, legacy);
        Equal(legacy, legacyFallback, "Legacy history should remain available when no current marker exists.");
    }

    private static void BcdIdentifiersAreCanonical()
    {
        Equal(
            WellKnownObjectIdentifiers.Current,
            BcdContract.NormalizeObjectIdentifier("{fa926493-6f1c-4193-a414-58f0b2456d1e}"),
            "A valid BCD GUID should be normalized without changing its identity.");
        Throws<ArgumentException>(
            () => BcdContract.NormalizeObjectIdentifier("{current}"),
            "Aliases and path fragments must not reach a WMI instance path.");
    }

    private static void BcdElementFormatsAreTyped()
    {
        Equal(
            BcdElementValueKind.Boolean,
            BcdContract.GetValueKind(WellKnownElementTypes.AdvancedOptions),
            "AdvancedOptions should be a Boolean BCD element.");
        Equal(
            BcdElementValueKind.Integer,
            BcdContract.GetValueKind(WellKnownElementTypes.BootStatusPolicy),
            "BootStatusPolicy should be an integer BCD element.");
        Throws<NotSupportedException>(
            () => BcdContract.GetValueKind(0x12000001),
            "Unsupported BCD value formats should fail closed.");
    }

    private static void BcdWmiPathKeysAreEscaped()
    {
        Equal(
            @"C:\\Boot",
            BcdContract.EscapeManagementPathKey(@"C:\Boot"),
            "Backslashes must be escaped before building a WMI object path.");
        Equal(
            "say \\\"hello\\\"",
            BcdContract.EscapeManagementPathKey("say \"hello\""),
            "Quotes must be escaped before building a WMI object path.");
        Throws<ArgumentException>(
            () => BcdContract.EscapeManagementPathKey("bad\0key"),
            "Null characters must not enter a WMI object path.");
    }

    private static void BcdWritesAreVerified()
    {
        FakeBcdProvider provider = new();
        BcdService service = new(provider);
        service.SetBooleanElement(
            WellKnownObjectIdentifiers.GlobalSettings,
            WellKnownElementTypes.AdvancedOptions,
            true);
        Equal(
            true,
            service.GetElementValue(
                WellKnownObjectIdentifiers.GlobalSettings,
                WellKnownElementTypes.AdvancedOptions),
            "A retained Boolean value should be returned.");

        provider.RetainWrites = false;
        Throws<InvalidOperationException>(
            () => service.SetIntegerElement(
                WellKnownObjectIdentifiers.Current,
                WellKnownElementTypes.BootStatusPolicy,
                1UL),
            "A WMI provider that does not retain the requested value must be rejected.");
    }

    private static void BcdDeleteIsIdempotent()
    {
        FakeBcdProvider provider = new();
        BcdService service = new(provider);
        service.DeleteElement(
            WellKnownObjectIdentifiers.GlobalSettings,
            WellKnownElementTypes.HighestMode);
        Equal(0, provider.DeleteCalls, "An already-absent element should not be deleted again.");
    }

    private static void AsyncCommandPreventsConcurrentExecution()
    {
        SynToolkit.App.logger.ResetErrors();
        GatedAsyncCommand command = new();
        int canExecuteNotifications = 0;
        command.CanExecuteChanged += (_, _) => Interlocked.Increment(ref canExecuteNotifications);

        command.Execute(null);
        True(command.WaitUntilStarted(), "The first asynchronous command invocation should start.");
        True(command.IsExecuting, "A running command should expose its busy state.");
        True(!command.CanExecute(null), "A running command must report that it cannot execute again.");

        command.Execute(null);
        Equal(1, command.ExecutionCount, "A second invocation must be ignored while the first is running.");

        command.Release();
        True(
            SpinWait.SpinUntil(() => !command.IsExecuting, TimeSpan.FromSeconds(2)),
            "The command should leave its busy state after completion.");
        Equal(1, command.ExecutionCount, "Only one asynchronous operation should have run.");
        True(command.CanExecute(null), "A completed command should become executable again.");
        True(canExecuteNotifications >= 2, "The command should notify bindings when execution starts and ends.");
        Equal(0, SynToolkit.App.logger.ErrorCount, "A successful command should not log an error.");
    }

    private static void AsyncCommandFailureIsContained()
    {
        SynToolkit.App.logger.ResetErrors();
        FailingAsyncCommand command = new();

        command.Execute(null);

        True(
            SpinWait.SpinUntil(
                () => !command.IsExecuting && SynToolkit.App.logger.ErrorCount == 1,
                TimeSpan.FromSeconds(2)),
            "A failed command should complete, reset its busy state, and log exactly once.");
        True(command.CanExecute(null), "A failed command should not remain permanently disabled.");
    }

    private static void BatchFilePathsWithSpacesExecuteCorrectly()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SynToolkit Batch Tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string batchFilePath = Path.Combine(directory, "Safe Mode Test.cmd");
        try
        {
            File.WriteAllText(
                batchFilePath,
                "@echo off\r\nif not \"%~1\"==\"first argument\" exit /b 7\r\necho batch-ok\r\nexit /b 0\r\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            CommandResult result = CommandPromptHelper.RunBatchFileResult(
                batchFilePath,
                ["first argument"],
                timeoutMilliseconds: 10_000);

            True(result.Succeeded, $"A quoted batch path should execute successfully: {result.CombinedOutput}");
            True(result.StandardOutput.Contains("batch-ok", StringComparison.Ordinal), "The batch payload must actually run.");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void RadeonBulkSelectionsNotifyTheUi()
    {
        RadeonPackage package = new()
        {
            SourceFile = "manifest.json",
            ProductName = "Package",
            Url = "package.zip",
            Type = "driver",
            Description = "Package description",
        };
        RadeonScheduledTask task = new()
        {
            SourceFile = "task.xml",
            Description = "Scheduled task",
            Command = "task.exe",
        };
        RadeonDisplayComponent component = new()
        {
            DirectoryPath = "DisplayComponent",
            Name = "Display component",
        };

        PropertyChangeIsRaised(package, nameof(RadeonPackage.Keep), () => package.Keep = false);
        PropertyChangeIsRaised(task, nameof(RadeonScheduledTask.Enabled), () => task.Enabled = true);
        PropertyChangeIsRaised(component, nameof(RadeonDisplayComponent.Keep), () => component.Keep = false);
    }

    private static void PropertyChangeIsRaised(
        INotifyPropertyChanged item,
        string expectedPropertyName,
        Action change)
    {
        int notifications = 0;
        item.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == expectedPropertyName)
            {
                notifications++;
            }
        };

        change();
        Equal(1, notifications, $"Changing {expectedPropertyName} must notify its checkbox binding.");
        change();
        Equal(1, notifications, $"Reapplying {expectedPropertyName} must not raise a duplicate notification.");
    }

    private static void LegacyRegistrationMatchIsExact()
    {
        True(
            LegacyProfileMigrationPolicy.IsExactLegacyRegistration(
                " Syntoolkit ",
                "Kwanteks",
                "1.5.0"),
            "The exact legacy Kwanteks registration should be eligible.");
        True(
            !LegacyProfileMigrationPolicy.IsExactLegacyRegistration(
                "Atlas Toolbox",
                "AtlasOS",
                "0.1.13"),
            "The official Atlas registration that shares the old AppId must not be eligible.");
        True(
            !LegacyProfileMigrationPolicy.IsExactLegacyRegistration(
                "Syntoolkit",
                "Kwanteks",
                "1.5.1"),
            "A different SynToolkit version must not be treated as the legacy product.");
    }

    private static void ValidLegacyProfileIsAccepted()
    {
        const string json =
            "{\"Name\":\"Gaming\",\"Config\":[\"Animations\",\"Bluetooth\"]," +
            "\"MultiConfig\":[{\"Key\":\"Mitigations\",\"Value\":\"Default\"}]}";
        byte[] withBom = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(json))
            .ToArray();

        True(
            LegacyProfileMigrationPolicy.TryValidateProfile(
                withBom,
                "Gaming.json",
                out string rejectionReason),
            $"A normal legacy profile should be accepted: {rejectionReason}");
    }

    private static void MalformedLegacyProfilesAreRejected()
    {
        string[] invalidProfiles =
        [
            "not json",
            "{\"Name\":\"Gaming\",\"Config\":[],\"MultiConfig\":[] /* comment */}",
            "{\"Name\":\"DifferentName\",\"Config\":[],\"MultiConfig\":[]}",
            "{\"Name\":\"Gaming\",\"Config\":[],\"MultiConfig\":[],\"Unknown\":true}",
            "{\"Name\":\"Gaming\",\"Config\":[\"Animations\",\"Animations\"],\"MultiConfig\":[]}",
            "{\"Name\":\"Gaming\",\"Config\":[],\"MultiConfig\":[{\"Key\":\"Mitigations\"}]}"
        ];

        foreach (string json in invalidProfiles)
        {
            True(
                !LegacyProfileMigrationPolicy.TryValidateProfile(
                    Encoding.UTF8.GetBytes(json),
                    "Gaming.json",
                    out _),
                $"Invalid legacy profile JSON should be rejected: {json}");
        }

        True(
            !LegacyProfileMigrationPolicy.TryValidateProfile(
                Encoding.UTF8.GetBytes("{\"Name\":\"Gaming\",\"Config\":[],\"MultiConfig\":[]}"),
                "Gaming.txt",
                out _),
            "A profile with a non-JSON filename must be rejected.");
    }

    private static void OversizedLegacyProfileIsRejected()
    {
        byte[] oversized = new byte[LegacyProfileMigrationPolicy.MaximumProfileBytes + 1];
        True(
            !LegacyProfileMigrationPolicy.TryValidateProfile(
                oversized,
                "Gaming.json",
                out _),
            "A profile larger than the migration limit must be rejected before parsing.");
    }

    private static void NvidiaProfileExportRoundTrips()
    {
        List<NvidiaProfile> expected =
        [
            new NvidiaProfile
            {
                ProfileName = "Base Profile",
                Settings =
                [
                    new NvidiaProfileSetting
                    {
                        SettingNameInfo = "Power management mode",
                        SettingId = 274197361,
                        SettingValue = "1",
                        ValueType = NvidiaSettingValueType.Dword,
                    },
                ],
            },
            new NvidiaProfile
            {
                ProfileName = "Game & Launcher",
                Executeables = ["game.exe", "launcher.exe"],
                Settings =
                [
                    new NvidiaProfileSetting
                    {
                        SettingNameInfo = "Application note",
                        SettingId = 550564838,
                        SettingValue = "GPU <preferred>",
                        ValueType = NvidiaSettingValueType.String,
                    },
                ],
            },
        ];

        string exportPath = Path.Combine(Path.GetTempPath(), "SynToolkit-NipTests-" + Guid.NewGuid().ToString("N") + ".nip");
        try
        {
            NvidiaProfilePreviewService.SaveProfiles(expected, exportPath);
            List<NvidiaProfile> actual = NvidiaProfilePreviewService.LoadProfiles(exportPath);

            Equal(2, actual.Count, "Every loaded profile must be exported.");
            Equal("Base Profile", actual[0].ProfileName, "The base profile name must be preserved.");
            Equal(1, actual[0].Settings.Count, "Base-profile settings must be preserved.");
            Equal(274197361U, actual[0].Settings[0].SettingId, "Setting IDs must be preserved.");
            Equal("1", actual[0].Settings[0].SettingValue, "Setting values must be preserved.");
            Equal(2, actual[1].Executeables.Count, "Executable associations must be preserved.");
            Equal("GPU <preferred>", actual[1].Settings[0].SettingValue, "Escaped string values must round-trip.");
        }
        finally
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
        }
    }

    private static void NoMetadata()
    {
        PlaybookInformation result = AmePlaybookDetector.Detect(new FakeSource());
        Equal(PlaybookDetectionStatus.NotDetected, result.Status, "Empty metadata should not be guessed.");
    }

    private static void OfficialRecord()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "AME 11",
                    "v0.8.4",
                    @"HKLM\SOFTWARE\AME\Playbooks\Applied\{9010E718-4B54-443F-8354-D893CD50FDDE}",
                    new DateTime(2026, 2, 11, 12, 0, 0, DateTimeKind.Utc))
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(PlaybookDetectionStatus.Detected, result.Status, "Official metadata should be detected.");
        Equal("AME 11", result.Name, "Stored Playbook name should be preserved.");
        Equal("0.8.4", result.Version, "A leading v should be normalized.");
    }

    private static void NewestRecordWins()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "Older Playbook",
                    "1.0.0",
                    "older",
                    new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new AmePlaybookMarker(
                    "AME 10",
                    "2.5",
                    "newer",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(PlaybookDetectionStatus.Detected, result.Status, "A timestamped record should resolve confidently.");
        Equal("AME 10", result.Name, "The latest AppliedTimeUTC record should win.");
        Equal("2.5", result.Version, "The selected record's version should be returned.");
    }

    private static void OfficialBinaryTimestamp()
    {
        DateTime expected = new(2026, 2, 11, 12, 34, 56, DateTimeKind.Utc);
        DateTime? actual = WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc(expected.ToBinary());
        Equal(expected, actual, "AME stores AppliedTimeUTC using DateTime.UtcNow.ToBinary().");
    }

    private static void InvalidBinaryTimestamps()
    {
        True(
            WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc("not-a-qword") is null,
            "A non-QWORD registry value must be ignored.");
        True(
            WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc(long.MaxValue) is null,
            "An out-of-range DateTime binary value must be ignored.");
        True(
            WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc(
                new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToBinary()) is null,
            "Implausibly old applied timestamps must be ignored.");
        DateTime now = new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
        True(
            WindowsAmePlaybookMetadataSource.TryDecodeAppliedTimeUtc(
                now.AddDays(2).ToBinary(),
                now) is null,
            "Implausibly future applied timestamps must not win forever.");
    }

    private static void OverhaulPlaybookWins()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "SynergyOS Playbook",
                    "1.5.1",
                    "overhaul",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Overhaul: true,
                    ErrorLevel: 0),
                new AmePlaybookMarker(
                    "Utility Playbook",
                    "2.0",
                    "utility",
                    new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Overhaul: false,
                    ErrorLevel: 0)
            ]);

        PlaybookInformation result = AmePlaybookDetector.DetectRegistry(source);
        Equal("SynergyOS Playbook", result.Name, "An add-on Playbook must not replace the OS-overhaul identity.");
    }

    private static void FatalRecordIsIgnored()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "Working Playbook",
                    "1.0",
                    "success",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Overhaul: true,
                    ErrorLevel: 0),
                new AmePlaybookMarker(
                    "Failed Playbook",
                    "2.0",
                    "fatal",
                    new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Overhaul: true,
                    ErrorLevel: 2)
            ]);

        PlaybookInformation result = AmePlaybookDetector.DetectRegistry(source);
        Equal("Working Playbook", result.Name, "A fatal AME attempt must not replace a successfully applied Playbook.");

        PlaybookInformation fatalOnly = AmePlaybookDetector.DetectRegistry(
            new FakeSource(registryMarkers:
            [
                new AmePlaybookMarker("Failed Playbook", "2.0", "fatal", ErrorLevel: 2)
            ]));
        Equal(PlaybookDetectionStatus.NotDetected, fatalOnly.Status, "A fatal-only history must not be presented as current.");
    }

    private static void MixedTimestampConflict()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker(
                    "AME 11",
                    "0.8.4",
                    "timestamped",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new AmePlaybookMarker("Privacy+", "1.0", "untimestamped")
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(
            PlaybookDetectionStatus.Conflicting,
            result.Status,
            "A missing timestamp must not let registry order decide between different Playbooks.");
    }

    private static void TiedTimestampConflict()
    {
        DateTime tie = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker("AME 11", "0.8.4", "first", tie),
                new AmePlaybookMarker("Privacy+", "1.0", "second", tie)
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(
            PlaybookDetectionStatus.Conflicting,
            result.Status,
            "Equal timestamps for different Playbooks must stay explicit.");
    }

    private static void RegistryPrecedesLegacy()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker("Privacy+", "0.8", "registry")
            ],
            legacyMarker: new AmePlaybookMarker("Legacy AME", "0.7", "legacy"));

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal("Privacy+", result.Name, "Current registry metadata must precede legacy history.");
    }

    private static void UntimestampedConflict()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker("AME 10", "2.5", "first"),
                new AmePlaybookMarker("Privacy+", "0.8", "second")
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(PlaybookDetectionStatus.Conflicting, result.Status, "Registry order must not be guessed without timestamps.");
    }

    private static void MalformedEntryIsolated()
    {
        FakeSource source = new(
            registryMarkers:
            [
                new AmePlaybookMarker("https://invalid.example/playbook", "1.0", "invalid"),
                new AmePlaybookMarker("AME 11", "0.8.4", "valid")
            ]);

        PlaybookInformation result = AmePlaybookDetector.Detect(source);
        Equal(PlaybookDetectionStatus.Detected, result.Status, "One corrupt record must not hide valid metadata.");
        Equal("AME 11", result.Name, "The valid record should survive isolation.");
    }

    private static void ValidLegacyConfiguration()
    {
        WithTemporaryConfiguration(
            "<Playbook><Name>AME 10</Name><Version>v2.5</Version></Playbook>",
            path =>
            {
                bool parsed = WindowsAmePlaybookMetadataSource.TryReadPlaybookConfiguration(path, out AmePlaybookMarker? marker);
                True(parsed, "A normal AME playbook.conf should parse.");
                Equal("AME 10", marker?.Name, "Legacy name should be read from XML.");
                Equal("v2.5", marker?.Version, "Legacy version should be read from XML before normalization.");
            });
    }

    private static void ExternalEntityIsProhibited()
    {
        const string xml = "<!DOCTYPE Playbook [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]>"
            + "<Playbook><Name>&xxe;</Name><Version>1.0</Version></Playbook>";
        WithTemporaryConfiguration(
            xml,
            path => True(
                !WindowsAmePlaybookMetadataSource.TryReadPlaybookConfiguration(path, out _),
                "DTD/external-entity metadata must be rejected."));
    }

    private static void LatestLegacyDirectoryWins()
    {
        WithTemporaryLegacyRoot(
            root =>
            {
                WriteLegacyConfiguration(root, "1", "Older AME", "1.0");
                WriteLegacyConfiguration(root, "2", "Newer AME", "2.0");

                WindowsAmePlaybookMetadataSource source = new(root);
                AmePlaybookMarker? marker = source.ReadLatestLegacyMarker();
                Equal("Newer AME", marker?.Name, "The largest numeric AME history folder should win.");
            });
    }

    private static void InvalidLegacyDirectoryFallsBack()
    {
        WithTemporaryLegacyRoot(
            root =>
            {
                WriteLegacyConfiguration(root, "2", "Usable AME", "2.0");
                string invalidDirectory = Path.Combine(root, "3");
                Directory.CreateDirectory(invalidDirectory);
                File.WriteAllText(
                    Path.Combine(invalidDirectory, "playbook.conf"),
                    "<Playbook><Name>https://invalid.example/playbook</Name></Playbook>",
                    new UTF8Encoding(false));

                WindowsAmePlaybookMetadataSource source = new(root);
                AmePlaybookMarker? marker = source.ReadLatestLegacyMarker();
                Equal("Usable AME", marker?.Name, "One malformed history entry must not hide an older valid entry.");
            });
    }

    private static void EmptyNormalizedVersionIsOmitted()
    {
        PlaybookInformation? result = AmePlaybookDetector.TryNormalize(
            new AmePlaybookMarker("AME 11", "v", "test"));
        Equal<string?>(null, result?.Version, "A bare version prefix must not be displayed as an empty version.");
    }

    private static void InvalidVersionsAreOmitted()
    {
        foreach (string version in new[] { "vvvvjunk", "1.2.3.4", "https://invalid.example/version" })
        {
            PlaybookInformation? result = AmePlaybookDetector.TryNormalize(
                new AmePlaybookMarker("AME 11", version, "test"));
            Equal<string?>(null, result?.Version, $"Invalid AME version '{version}' must not be displayed.");
        }
    }

    private static void SameNameVersionConflict()
    {
        PlaybookInformation result = AmePlaybookDetector.ResolveMarkers(
        [
            new PlaybookInformation(PlaybookDetectionStatus.Detected, "AME 11", "0.8.3", "first"),
            new PlaybookInformation(PlaybookDetectionStatus.Detected, "AME 11", "0.8.4", "second")
        ]);
        Equal(
            PlaybookDetectionStatus.Conflicting,
            result.Status,
            "The same Playbook name with different versions must not be selected by insertion order.");
    }

    private static void OversizedConfigurationIsRejected()
    {
        string oversized = "<Playbook><Name>" + new string('A', checked((int)WindowsAmePlaybookMetadataSource.MaximumConfigurationBytes))
            + "</Name><Version>1.0</Version></Playbook>";
        WithTemporaryConfiguration(
            oversized,
            path => True(
                !WindowsAmePlaybookMetadataSource.TryReadPlaybookConfiguration(path, out _),
                "Oversized metadata must be rejected before XML parsing."));
    }

    private static void WithTemporaryConfiguration(string contents, Action<string> assertion)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SynToolkit-AmeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "playbook.conf");
        try
        {
            File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            assertion(path);
        }
        finally
        {
            string resolvedDirectory = Path.GetFullPath(directory);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (resolvedDirectory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(resolvedDirectory))
            {
                Directory.Delete(resolvedDirectory, recursive: true);
            }
        }
    }

    private static void WithTemporaryLegacyRoot(Action<string> assertion)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SynToolkit-AmeLegacyTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            assertion(directory);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void WriteLegacyConfiguration(string root, string folder, string name, string version)
    {
        string directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "playbook.conf"),
            $"<Playbook><Name>{name}</Name><Version>{version}</Version></Playbook>",
            new UTF8Encoding(false));
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        string resolvedDirectory = Path.GetFullPath(directory);
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        if (resolvedDirectory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolvedDirectory))
        {
            Directory.Delete(resolvedDirectory, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL: {name}: {exception.Message}");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private sealed class FakeBcdProvider : IBcdWmiProvider
    {
        private readonly Dictionary<(string ObjectId, uint ElementType), object> _values = new();

        internal bool RetainWrites { get; set; } = true;
        internal int DeleteCalls { get; private set; }

        public object GetElementValue(string objectId, uint elementType) =>
            _values.TryGetValue((objectId, elementType), out object? value) ? value : null!;

        public void DeleteElement(string objectId, uint elementType)
        {
            DeleteCalls++;
            if (RetainWrites)
            {
                _values.Remove((objectId, elementType));
            }
        }

        public void SetBooleanElement(string objectId, uint elementType, bool value)
        {
            if (RetainWrites)
            {
                _values[(objectId, elementType)] = value;
            }
        }

        public void SetIntegerElement(string objectId, uint elementType, ulong value)
        {
            if (RetainWrites)
            {
                _values[(objectId, elementType)] = value;
            }
        }
    }

    private sealed class GatedAsyncCommand : AsyncCommandBase
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executionCount;

        internal int ExecutionCount => Volatile.Read(ref _executionCount);

        internal bool WaitUntilStarted() => _started.Task.Wait(TimeSpan.FromSeconds(2));

        internal void Release() => _release.TrySetResult();

        protected override async Task ExecuteAsync(object? parameter)
        {
            Interlocked.Increment(ref _executionCount);
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }
    }

    private sealed class FailingAsyncCommand : AsyncCommandBase
    {
        protected override Task ExecuteAsync(object? parameter) =>
            Task.FromException(new InvalidOperationException("Expected test failure."));
    }

    private sealed class FakeSource : IAmePlaybookMetadataSource
    {
        private readonly IReadOnlyCollection<AmePlaybookMarker> _registryMarkers;
        private readonly AmePlaybookMarker? _legacyMarker;

        internal FakeSource(
            IReadOnlyCollection<AmePlaybookMarker>? registryMarkers = null,
            AmePlaybookMarker? legacyMarker = null)
        {
            _registryMarkers = registryMarkers ?? Array.Empty<AmePlaybookMarker>();
            _legacyMarker = legacyMarker;
        }

        public IReadOnlyCollection<AmePlaybookMarker> ReadRegistryMarkers() => _registryMarkers;

        public AmePlaybookMarker? ReadLatestLegacyMarker() => _legacyMarker;
    }
}
