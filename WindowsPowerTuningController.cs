using System.Runtime.InteropServices;

namespace VictusModeSwitch;

internal static class WindowsPowerTuningController
{
    private const int BackupRevision = 2;

    private static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcessorMinimum = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    private static readonly Guid ProcessorMaximum = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private static readonly Guid ProcessorEpp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    private static readonly Guid ProcessorEppClass1 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");
    private static readonly Guid ProcessorEppClass2 = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6865");
    private static readonly Guid CoolingPolicy = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");
    private static readonly Guid PcieSubgroup = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid PcieLinkState = new("ee12f906-d277-404b-b6da-e5fa1a576df5");
    private static readonly Guid WirelessSubgroup = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");
    private static readonly Guid WirelessPowerSaving = new("12bbebe6-58d6-4636-95bb-3217ef867c1a");
    private static readonly Guid UsbSubgroup = new("2a737441-1930-4402-8d77-b2bebba308a3");
    private static readonly Guid UsbSelectiveSuspend = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
    private static readonly Guid BestPerformancePowerMode = new("ded574b5-45a0-4f42-8737-46345c09c238");
    private static readonly Guid BestEfficiencyPowerMode = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    public static WindowsPowerBackup Capture()
    {
        var scheme = GetActiveScheme();
        var backup = new WindowsPowerBackup
        {
            Valid = true,
            Revision = BackupRevision,
            Scheme = scheme,
            ProcessorMinimum = CaptureValue(
                scheme, ProcessorSubgroup, ProcessorMinimum, "минимум процессора"),
            ProcessorMaximum = CaptureValue(
                scheme, ProcessorSubgroup, ProcessorMaximum, "предел процессора"),
            ProcessorEpp = CaptureValue(
                scheme, ProcessorSubgroup, ProcessorEpp, "EPP процессора"),
            ProcessorEppClass1 = CaptureValue(
                scheme, ProcessorSubgroup, ProcessorEppClass1, "EPP эффективного класса 1"),
            ProcessorEppClass2 = CaptureValue(
                scheme, ProcessorSubgroup, ProcessorEppClass2, "EPP эффективного класса 2"),
            CoolingPolicy = CaptureValue(
                scheme, ProcessorSubgroup, CoolingPolicy, "политику охлаждения"),
            PcieLinkState = CaptureValue(
                scheme, PcieSubgroup, PcieLinkState, "энергосбережение PCI Express"),
            WirelessPowerSaving = CaptureValue(
                scheme, WirelessSubgroup, WirelessPowerSaving, "энергосбережение Wi-Fi"),
            UsbSelectiveSuspend = CaptureValue(
                scheme, UsbSubgroup, UsbSelectiveSuspend, "выборочную приостановку USB"),
            PowerMode = CapturePowerMode()
        };

        if (!HasSupportedValues(backup))
        {
            throw new InvalidOperationException("Windows не предоставила поддерживаемые параметры питания.");
        }

        return backup;
    }

    public static bool UsesCurrentScheme(WindowsPowerBackup backup) =>
        backup.Valid && backup.Revision == BackupRevision && backup.Scheme == GetActiveScheme();

    public static void Apply(AppMode mode, WindowsPowerBackup backup, PowerTuningSettings settings)
    {
        if (!backup.Valid)
        {
            throw new InvalidOperationException("Нет резервной копии параметров питания Windows.");
        }

        Guid powerMode;
        switch (mode)
        {
            case AppMode.Eco:
                ApplyPowerValues(
                    backup,
                    processorMinimumAc: 5,
                    processorMinimumDc: 5,
                    processorMaximumAc: (uint)settings.EcoAcMaximumProcessor,
                    processorMaximumDc: (uint)settings.EcoBatteryMaximumProcessor,
                    processorEppAc: 60,
                    processorEppDc: 85,
                    coolingAc: 1,
                    coolingDc: 0,
                    pcieLinkState: 2,
                    wirelessPowerSaving: 3,
                    usbSelectiveSuspend: 1);
                powerMode = BestEfficiencyPowerMode;
                break;
            case AppMode.Performance:
                ApplyPowerValues(
                    backup,
                    processorMinimumAc: 100,
                    processorMinimumDc: 100,
                    processorMaximumAc: 100,
                    processorMaximumDc: 100,
                    processorEppAc: 0,
                    processorEppDc: 0,
                    coolingAc: 1,
                    coolingDc: 1,
                    pcieLinkState: 0,
                    wirelessPowerSaving: 0,
                    usbSelectiveSuspend: 0);
                powerMode = BestPerformancePowerMode;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        ActivateIfCurrent(backup.Scheme);
        SetPowerModeIfValid(backup.PowerMode, powerMode, powerMode);
        Log.Info($"Windows power tuning: профиль применён для {mode}");
    }

    public static void Restore(WindowsPowerBackup backup)
    {
        if (!backup.Valid)
        {
            return;
        }

        RestoreValue(backup.Scheme, ProcessorSubgroup, ProcessorMinimum, backup.ProcessorMinimum,
            "восстановление минимума процессора");
        RestoreValue(backup.Scheme, ProcessorSubgroup, ProcessorMaximum, backup.ProcessorMaximum,
            "восстановление предела процессора");
        RestoreValue(backup.Scheme, ProcessorSubgroup, ProcessorEpp, backup.ProcessorEpp,
            "восстановление EPP процессора");
        RestoreValue(backup.Scheme, ProcessorSubgroup, ProcessorEppClass1, backup.ProcessorEppClass1,
            "восстановление EPP класса 1");
        RestoreValue(backup.Scheme, ProcessorSubgroup, ProcessorEppClass2, backup.ProcessorEppClass2,
            "восстановление EPP класса 2");
        RestoreValue(backup.Scheme, ProcessorSubgroup, CoolingPolicy, backup.CoolingPolicy,
            "восстановление охлаждения");
        RestoreValue(backup.Scheme, PcieSubgroup, PcieLinkState, backup.PcieLinkState,
            "восстановление PCI Express");
        RestoreValue(backup.Scheme, WirelessSubgroup, WirelessPowerSaving, backup.WirelessPowerSaving,
            "восстановление Wi-Fi");
        RestoreValue(backup.Scheme, UsbSubgroup, UsbSelectiveSuspend, backup.UsbSelectiveSuspend,
            "восстановление USB");
        ActivateIfCurrent(backup.Scheme);
        RestorePowerModeIfValid(backup.PowerMode);
        Log.Info("Windows power tuning: исходные параметры восстановлены");
    }

    private static void ApplyPowerValues(
        WindowsPowerBackup backup,
        uint processorMinimumAc,
        uint processorMinimumDc,
        uint processorMaximumAc,
        uint processorMaximumDc,
        uint processorEppAc,
        uint processorEppDc,
        uint coolingAc,
        uint coolingDc,
        uint pcieLinkState,
        uint wirelessPowerSaving,
        uint usbSelectiveSuspend)
    {
        WriteIfValid(backup.Scheme, ProcessorSubgroup, ProcessorMinimum, backup.ProcessorMinimum,
            processorMinimumAc, processorMinimumDc, "минимум процессора");
        WriteIfValid(backup.Scheme, ProcessorSubgroup, ProcessorMaximum, backup.ProcessorMaximum,
            processorMaximumAc, processorMaximumDc, "предел процессора");
        WriteIfValid(backup.Scheme, ProcessorSubgroup, ProcessorEpp, backup.ProcessorEpp,
            processorEppAc, processorEppDc, "EPP процессора");
        WriteIfValid(backup.Scheme, ProcessorSubgroup, ProcessorEppClass1, backup.ProcessorEppClass1,
            processorEppAc, processorEppDc, "EPP класса 1");
        WriteIfValid(backup.Scheme, ProcessorSubgroup, ProcessorEppClass2, backup.ProcessorEppClass2,
            processorEppAc, processorEppDc, "EPP класса 2");
        WriteIfValid(backup.Scheme, ProcessorSubgroup, CoolingPolicy, backup.CoolingPolicy,
            coolingAc, coolingDc, "политика охлаждения");
        WriteIfValid(backup.Scheme, PcieSubgroup, PcieLinkState, backup.PcieLinkState,
            pcieLinkState, pcieLinkState, "энергосбережение PCI Express");
        WriteIfValid(backup.Scheme, WirelessSubgroup, WirelessPowerSaving, backup.WirelessPowerSaving,
            wirelessPowerSaving, wirelessPowerSaving, "энергосбережение Wi-Fi");
        WriteIfValid(backup.Scheme, UsbSubgroup, UsbSelectiveSuspend, backup.UsbSelectiveSuspend,
            usbSelectiveSuspend, usbSelectiveSuspend, "выборочная приостановка USB");
    }

    private static bool HasSupportedValues(WindowsPowerBackup backup) =>
        backup.ProcessorMinimum.Valid ||
        backup.ProcessorMaximum.Valid ||
        backup.ProcessorEpp.Valid ||
        backup.ProcessorEppClass1.Valid ||
        backup.ProcessorEppClass2.Valid ||
        backup.CoolingPolicy.Valid ||
        backup.PcieLinkState.Valid ||
        backup.WirelessPowerSaving.Valid ||
        backup.UsbSelectiveSuspend.Valid ||
        backup.PowerMode.Valid;

    private static PowerValueBackup CaptureValue(Guid scheme, Guid subgroup, Guid setting, string name)
    {
        try
        {
            Check(PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var ac),
                $"чтение {name} для сети");
            Check(PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var dc),
                $"чтение {name} для батареи");
            return new PowerValueBackup { Valid = true, AcValue = ac, DcValue = dc };
        }
        catch (Exception exception)
        {
            Log.Warning($"Windows power tuning: параметр '{name}' недоступен: {exception.Message}");
            return new PowerValueBackup();
        }
    }

    private static PowerModeBackup CapturePowerMode()
    {
        try
        {
            Check(PowerGetUserConfiguredACPowerMode(out var ac), "чтение режима питания Windows для сети");
            Check(PowerGetUserConfiguredDCPowerMode(out var dc), "чтение режима питания Windows для батареи");
            return new PowerModeBackup { Valid = true, AcMode = ac, DcMode = dc };
        }
        catch (Exception exception)
        {
            Log.Warning($"Windows power tuning: режим питания Windows недоступен: {exception.Message}");
            return new PowerModeBackup();
        }
    }

    private static void RestoreValue(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        PowerValueBackup backup,
        string operation) =>
        WriteIfValid(scheme, subgroup, setting, backup, backup.AcValue, backup.DcValue, operation);

    private static void WriteIfValid(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        PowerValueBackup backup,
        uint ac,
        uint dc,
        string operation)
    {
        if (!backup.Valid)
        {
            return;
        }

        Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, ac),
            $"{operation} для сети");
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, dc),
            $"{operation} для батареи");
    }

    private static void SetPowerModeIfValid(PowerModeBackup backup, Guid acMode, Guid dcMode)
    {
        if (!backup.Valid)
        {
            return;
        }

        Check(PowerSetUserConfiguredACPowerMode(ref acMode), "запись режима питания Windows для сети");
        Check(PowerSetUserConfiguredDCPowerMode(ref dcMode), "запись режима питания Windows для батареи");
    }

    private static void RestorePowerModeIfValid(PowerModeBackup backup)
    {
        if (!backup.Valid)
        {
            return;
        }

        var ac = backup.AcMode;
        var dc = backup.DcMode;
        SetPowerModeIfValid(backup, ac, dc);
    }

    private static Guid GetActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer), "чтение активной схемы питания");
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    private static void ActivateIfCurrent(Guid scheme)
    {
        if (GetActiveScheme() == scheme)
        {
            Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme), "применение схемы питания");
        }
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"Ошибка Windows при операции '{operation}', код {result}.");
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupGuid,
        ref Guid settingGuid,
        out uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupGuid,
        ref Guid settingGuid,
        out uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupGuid,
        ref Guid settingGuid,
        uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupGuid,
        ref Guid settingGuid,
        uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetUserConfiguredACPowerMode(out Guid powerModeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid powerModeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid powerModeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid powerModeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
