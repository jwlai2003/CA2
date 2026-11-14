
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CA2_Project.Components.Pages;

public partial class IndustrialDashboard : IAsyncDisposable
{
    // ==========================================
    // 1. DATA SOURCE AND SYSTEM STATUS
    // ==========================================

    private bool UseMqttMode = true;

    private bool SimulationRun = false;
    private bool SimulationPnp = false;

    private bool HasLiveData =>
        Mqtt.IsConnected &&
        Mqtt.HasReceivedData &&
        Mqtt.LastUpdated.HasValue &&
        DateTime.Now - Mqtt.LastUpdated.Value
            <= TimeSpan.FromSeconds(30);

    // Simulated Network Loss affects simulated
    // data only, not real MQTT communication.
    private bool DataAvailable =>
        UseMqttMode
            ? HasLiveData
            : !SimulatedNetworkLoss;

    private bool DisplayRun =>
        UseMqttMode
            ? Mqtt.RunEnable
            : SimulationRun;

    private bool DisplayPnp =>
        UseMqttMode
            ? Mqtt.SensorDetected
            : SimulationPnp;

    // ==========================================
    // 2. WEIGHT MONITORING
    // ==========================================

    private double WeightKg = 2.5;
    private double WeightThresholdKg = 5.0;

    private double LiveWeightKg =>
        Mqtt.Voltage / 10.0;

    private bool HasLiveWeightData =>
        HasLiveData &&
        Mqtt.HasVoltage &&
        !double.IsNaN(Mqtt.Voltage) &&
        !double.IsInfinity(Mqtt.Voltage);

    private bool HasWeightData =>
        UseMqttMode
            ? HasLiveWeightData
            : !SimulatedNetworkLoss;

    private double DisplayWeightKg =>
        UseMqttMode
            ? LiveWeightKg
            : WeightKg;

    private bool IsOverweight =>
        HasWeightData &&
        DisplayWeightKg > WeightThresholdKg;

    private double ExcessWeightKg =>
        HasWeightData
            ? Math.Max(
                0.0,
                DisplayWeightKg - WeightThresholdKg)
            : 0.0;

    // ==========================================
    // 3. WEIGHT ALARM STATE
    // ==========================================

    private bool WeightBaselineReady = false;
    private bool PreviousOverweight = false;
    private bool WeightDataWasAvailable = false;

    private void EvaluateWeight()
    {
        if (!HasWeightData)
        {
            if (WeightDataWasAvailable)
            {
                AddEvent(
                    "WEIGHT DATA",
                    "Weight data unavailable. " +
                    "Alarm evaluation suspended.");
            }

            WeightDataWasAvailable = false;
            WeightBaselineReady = false;
            return;
        }

        bool current = IsOverweight;

        if (!WeightBaselineReady)
        {
            WeightBaselineReady = true;
            WeightDataWasAvailable = true;
            PreviousOverweight = current;

            if (current)
            {
                AddEvent(
                    "WEIGHT ALARM",
                    $"OVERWEIGHT at initial reading: " +
                    $"{DisplayWeightKg:F2} kg. " +
                    $"Limit: {WeightThresholdKg:F2} kg.");
            }

            return;
        }

        if (current != PreviousOverweight)
        {
            if (current)
            {
                AddEvent(
                    "WEIGHT ALARM",
                    $"OVERWEIGHT ALERT: " +
                    $"{DisplayWeightKg:F2} kg exceeds " +
                    $"{WeightThresholdKg:F2} kg.");
            }
            else
            {
                AddEvent(
                    "WEIGHT RECOVERY",
                    $"Weight returned to normal: " +
                    $"{DisplayWeightKg:F2} kg. " +
                    $"Limit: {WeightThresholdKg:F2} kg.");
            }
        }

        PreviousOverweight = current;
        WeightDataWasAvailable = true;
    }

    private void ResetWeight()
    {
        if (UseMqttMode || SimulatedNetworkLoss)
            return;

        WeightKg = 0.0;
        EvaluateWeight();
    }

    // ==========================================
    // 4. MONITORING MODES
    // ==========================================

    private void ResetWeightBaseline()
    {
        WeightBaselineReady = false;
        WeightDataWasAvailable = false;
        PreviousOverweight = false;
    }

    private void EnableSimulationMode()
    {
        if (!UseMqttMode)
            return;

        UseMqttMode = false;

        // Reset simulation fault state.
        SimulatedNetworkLoss = false;
        HeartbeatPaused = false;
        SimulationHeartbeatFault = false;

        LastSimulationHeartbeat = DateTime.Now;

        ResetWeightBaseline();

        AddEvent(
            "SYSTEM",
            "Simulation Mode enabled.");

        EvaluateWeight();
    }

    private void EnableMqttMode()
    {
        if (UseMqttMode)
            return;

        UseMqttMode = true;

        ResetWeightBaseline();

        AddEvent(
            "SYSTEM",
            "WAGO MQTT Monitoring enabled. " +
            "Weight source: PLC Voltage / 10.");

        EvaluateWeight();
        CheckTelemetryHealth();
    }

    // ==========================================
    // 5. NP9 / PNP SIMULATION
    // ==========================================

    private void SimulateNp9()
    {
        if (UseMqttMode || SimulatedNetworkLoss)
            return;

        SimulationRun = !SimulationRun;

        AddEvent(
            "SIMULATION NP9",
            SimulationRun
                ? "NP9 pressed. Simulation RUNNING."
                : "NP9 pressed. Simulation STOPPED.");
    }

    private void ToggleSimulatedPnp()
    {
        if (UseMqttMode || SimulatedNetworkLoss)
            return;

        SimulationPnp = !SimulationPnp;

        AddEvent(
            "SIMULATION PNP",
            SimulationPnp
                ? "Simulated object detected."
                : "Simulated object removed.");
    }

    // ==========================================
    // 6. MQTT CONNECTION
    // ==========================================

    private async Task ConnectMqttAsync()
    {
        try
        {
            await Mqtt.ConnectAsync();

            AddEvent(
                "MQTT",
                Mqtt.IsConnected
                    ? "Connected to Beaver IoT broker."
                    : "MQTT connection not established.");

            CheckTelemetryHealth();
        }
        catch (Exception ex)
        {
            AddEvent(
                "MQTT ERROR",
                ex.Message);
        }
    }

    // ==========================================
    // 7. REMOTE START / STOP
    // ==========================================

    private async Task SendCommandAsync(string command)
    {
        if (!Mqtt.IsConnected)
        {
            AddEvent(
                "MQTT COMMAND",
                "Command not sent. Broker disconnected.");
            return;
        }

        try
        {
            AddEvent(
                "MQTT COMMAND",
                $"{command} requested from Dashboard.");

            await Mqtt.PublishCommandAsync(command);
        }
        catch (Exception ex)
        {
            AddEvent(
                "MQTT ERROR",
                $"Command failed: {ex.Message}");
        }
    }

    // ==========================================
    // 8. PHYSICAL WAGO EVENT MONITORING
    // ==========================================

    private bool PhysicalBaselineReady = false;

    private bool PreviousPhysicalRun = false;
    private bool PreviousPhysicalPnp = false;
    private bool PreviousCountersAvailable = false;

    private uint PreviousNP9Count = 0;
    private uint PreviousDetectCount = 0;
    private uint PreviousRemoveCount = 0;

    private double PreviousVoltage = 0.0;

    private DateTime? LastProcessedTelemetry = null;

    private const double VoltageLogStep = 1.0;

    private void ProcessPhysicalTelemetry()
    {
        if (!Mqtt.HasReceivedData ||
            !Mqtt.LastUpdated.HasValue)
        {
            return;
        }

        // Process each MQTT update only once.
        if (LastProcessedTelemetry.HasValue &&
            LastProcessedTelemetry.Value ==
                Mqtt.LastUpdated.Value)
        {
            return;
        }

        LastProcessedTelemetry = Mqtt.LastUpdated;

        // Establish initial PLC baseline.
        if (!PhysicalBaselineReady)
        {
            PreviousPhysicalRun = Mqtt.RunEnable;
            PreviousPhysicalPnp = Mqtt.SensorDetected;

            PreviousCountersAvailable =
                Mqtt.HasPhysicalEventCounters;

            PreviousNP9Count = Mqtt.NP9PressCount;
            PreviousDetectCount = Mqtt.PNPDetectCount;
            PreviousRemoveCount = Mqtt.PNPRemoveCount;

            PreviousVoltage = Mqtt.Voltage;

            PhysicalBaselineReady = true;

            AddEvent(
                "WAGO TELEMETRY",
                "Initial WAGO telemetry baseline received.");

            return;
        }

        // Physical event counters.
        if (Mqtt.HasPhysicalEventCounters &&
            PreviousCountersAvailable)
        {
            if (Mqtt.NP9PressCount > PreviousNP9Count)
            {
                AddEvent(
                    "PHYSICAL NP9",
                    $"NP9 button pressed " +
                    $"{Mqtt.NP9PressCount - PreviousNP9Count} " +
                    "time(s).");
            }

            if (Mqtt.PNPDetectCount > PreviousDetectCount)
            {
                AddEvent(
                    "PHYSICAL PNP",
                    $"Object detected " +
                    $"{Mqtt.PNPDetectCount - PreviousDetectCount} " +
                    "time(s).");
            }

            if (Mqtt.PNPRemoveCount > PreviousRemoveCount)
            {
                AddEvent(
                    "PHYSICAL PNP",
                    $"Object removed " +
                    $"{Mqtt.PNPRemoveCount - PreviousRemoveCount} " +
                    "time(s).");
            }

            if (Mqtt.NP9PressCount < PreviousNP9Count ||
                Mqtt.PNPDetectCount < PreviousDetectCount ||
                Mqtt.PNPRemoveCount < PreviousRemoveCount)
            {
                AddEvent(
                    "WAGO COUNTER",
                    "PLC physical event counter reset detected.");
            }
        }

        // PLC run state change.
        if (Mqtt.RunEnable != PreviousPhysicalRun)
        {
            AddEvent(
                "WAGO RUN STATE",
                Mqtt.RunEnable
                    ? "PLC RunEnable changed to RUNNING."
                    : "PLC RunEnable changed to STOPPED.");
        }

        // Physical PNP change.
        if (Mqtt.SensorDetected != PreviousPhysicalPnp)
        {
            AddEvent(
                "WAGO PNP",
                Mqtt.SensorDetected
                    ? "Physical PNP: OBJECT DETECTED."
                    : "Physical PNP: OBJECT REMOVED.");
        }

        // Analog input change.
        if (Mqtt.HasVoltage &&
            !double.IsNaN(Mqtt.Voltage) &&
            !double.IsInfinity(Mqtt.Voltage) &&
            Math.Abs(Mqtt.Voltage - PreviousVoltage)
                >= VoltageLogStep)
        {
            AddEvent(
                "WAGO ANALOG INPUT",
                $"Voltage changed from " +
                $"{PreviousVoltage:F2} to " +
                $"{Mqtt.Voltage:F2}. " +
                $"Simulated Weight: {LiveWeightKg:F2} kg.");

            PreviousVoltage = Mqtt.Voltage;
        }

        // Update baseline values.
        PreviousPhysicalRun = Mqtt.RunEnable;
        PreviousPhysicalPnp = Mqtt.SensorDetected;

        PreviousCountersAvailable =
            Mqtt.HasPhysicalEventCounters;

        PreviousNP9Count = Mqtt.NP9PressCount;
        PreviousDetectCount = Mqtt.PNPDetectCount;
        PreviousRemoveCount = Mqtt.PNPRemoveCount;
    }

    // ==========================================
    // 9. REAL MQTT TELEMETRY HEALTH
    // ==========================================

    private bool TelemetryTimeoutLogged = false;
    private bool PreviousBrokerConnected = false;

    private void CheckTelemetryHealth()
    {
        bool connected = Mqtt.IsConnected;

        if (connected != PreviousBrokerConnected)
        {
            AddEvent(
                "MQTT CONNECTION",
                connected
                    ? "MQTT broker connected."
                    : "MQTT broker disconnected.");

            PreviousBrokerConnected = connected;
        }

        // Only evaluate timeout after valid data.
        if (!Mqtt.HasReceivedData)
            return;

        if (!HasLiveData && !TelemetryTimeoutLogged)
        {
            AddEvent(
                "TELEMETRY TIMEOUT",
                "No fresh WAGO telemetry received " +
                "within the 30-second monitoring window.");

            TelemetryTimeoutLogged = true;
        }

        if (HasLiveData && TelemetryTimeoutLogged)
        {
            AddEvent(
                "TELEMETRY RECOVERY",
                "Fresh WAGO telemetry received again.");

            TelemetryTimeoutLogged = false;
        }
    }

    // ==========================================
    // 10. SIMULATED HEARTBEAT
    // ==========================================

    private bool HeartbeatPaused = false;
    private bool SimulationHeartbeatFault = false;

    private DateTime LastSimulationHeartbeat = DateTime.Now;

    private void PauseHeartbeat()
    {
        if (UseMqttMode || HeartbeatPaused)
            return;

        HeartbeatPaused = true;

        AddEvent(
            "HEARTBEAT",
            "Simulation heartbeat paused.");
    }

    private void ResumeHeartbeat()
    {
        if (UseMqttMode || !HeartbeatPaused)
            return;

        HeartbeatPaused = false;

        // Network Fault still blocks heartbeat.
        if (SimulatedNetworkLoss)
        {
            AddEvent(
                "HEARTBEAT",
                "Manual pause cleared, but simulated " +
                "network is still offline.");

            return;
        }

        LastSimulationHeartbeat = DateTime.Now;

        ClearHeartbeatFaultIfRecovered();

        AddEvent(
            "HEARTBEAT",
            "Simulation heartbeat resumed.");
    }

    private void ClearHeartbeatFaultIfRecovered()
    {
        if (!SimulationHeartbeatFault)
            return;

        SimulationHeartbeatFault = false;

        AddEvent(
            "HEARTBEAT RECOVERY",
            "Simulation heartbeat restored.");
    }

    private void UpdateSimulationHeartbeat()
    {
        // Heartbeat updates only when:
        // - simulation is active
        // - heartbeat is not manually paused
        // - network is healthy

        if (UseMqttMode)
            return;

        if (!HeartbeatPaused && !SimulatedNetworkLoss)
        {
            if (DateTime.Now - LastSimulationHeartbeat
                >= TimeSpan.FromSeconds(2))
            {
                LastSimulationHeartbeat = DateTime.Now;
            }
        }

        // Six-second timeout.
        bool timeout =
            DateTime.Now - LastSimulationHeartbeat
            > TimeSpan.FromSeconds(6);

        if (timeout && !SimulationHeartbeatFault)
        {
            SimulationHeartbeatFault = true;

            AddEvent(
                "HEARTBEAT TIMEOUT",
                "Simulation heartbeat missing " +
                "for more than six seconds.");
        }

        if (!timeout && SimulationHeartbeatFault)
        {
            SimulationHeartbeatFault = false;

            AddEvent(
                "HEARTBEAT RECOVERY",
                "Simulation heartbeat recovered.");
        }
    }

    // ==========================================
    // 11. NETWORK FAULT SIMULATION
    // ==========================================

    private bool SimulatedNetworkLoss = false;

    private void SimulateNetworkLoss()
    {
        if (UseMqttMode || SimulatedNetworkLoss)
            return;

        // Simulate loss of PLC communication.
        SimulatedNetworkLoss = true;

        // No simulated heartbeat is generated while
        // network fault remains active.
        // The timeout is counted from the last heartbeat.

        AddEvent(
            "NETWORK FAULT",
            "SIMULATED NETWORK CONNECTION LOST. " +
            "Simulated telemetry is unavailable.");

        // Suspend weight alarm due to missing data.
        EvaluateWeight();
    }

    private void RestoreNetwork()
    {
        if (UseMqttMode || !SimulatedNetworkLoss)
            return;

        // Restore simulated communication.
        SimulatedNetworkLoss = false;

        // Keep manually paused heartbeat independent.
        if (!HeartbeatPaused)
        {
            LastSimulationHeartbeat = DateTime.Now;

            ClearHeartbeatFaultIfRecovered();
        }

        AddEvent(
            "NETWORK RECOVERY",
            "Simulated network connection restored. " +
            "Simulated telemetry is available.");

        // Re-evaluate weight after data recovery.
        EvaluateWeight();
    }

    // ==========================================
    // 12. EVENT HISTORY
    // ==========================================

    private sealed record DashboardEvent(
        DateTime Time,
        string Category,
        string Message
    );

    private readonly List<DashboardEvent> Events = new();

    private void AddEvent(
        string category,
        string message)
    {
        Events.Insert(
            0,
            new DashboardEvent(
                DateTime.Now,
                category,
                message));

        // Keep latest 100 events.
        if (Events.Count > 100)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    }

    private void ClearHistory()
    {
        Events.Clear();
    }

    // ==========================================
    // 13. MQTT EVENT CALLBACKS
    // ==========================================

    private void OnMqttDataChanged()
    {
        _ = InvokeAsync(() =>
        {
            ProcessPhysicalTelemetry();

            CheckTelemetryHealth();

            if (UseMqttMode)
            {
                EvaluateWeight();
            }

            StateHasChanged();
        });
    }

    private void OnCommandEvent(
        string category,
        string message)
    {
        _ = InvokeAsync(() =>
        {
            AddEvent(category, message);

            StateHasChanged();
        });
    }

    // ==========================================
    // 14. BACKGROUND MONITORING LOOP
    // ==========================================

    private CancellationTokenSource? MonitorCancellation;
    private Task? MonitorTask;

    protected override void OnInitialized()
    {
        Mqtt.DataChanged += OnMqttDataChanged;
        Mqtt.CommandEvent += OnCommandEvent;

        PreviousBrokerConnected = Mqtt.IsConnected;
        LastSimulationHeartbeat = DateTime.Now;

        AddEvent(
            "SYSTEM",
            "Industrial IIoT Dashboard initialized.");

        AddEvent(
            "WEIGHT MONITORING",
            "Weight Monitoring initialized. " +
            "Default threshold: 5.00 kg.");

        EvaluateWeight();
        ProcessPhysicalTelemetry();

        MonitorCancellation =
            new CancellationTokenSource();

        MonitorTask =
            MonitorAsync(MonitorCancellation.Token);
    }

    private async Task MonitorAsync(
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(
                cancellationToken))
            {
                await InvokeAsync(() =>
                {
                    // Simulated heartbeat detection.
                    UpdateSimulationHeartbeat();

                    // Real MQTT health monitoring.
                    CheckTelemetryHealth();

                    // Re-check weight on data timeout.
                    if (UseMqttMode)
                    {
                        EvaluateWeight();
                    }

                    // Refresh dashboard every second.
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when page is closed.
        }
    }

    // ==========================================
    // 15. CLEANUP
    // ==========================================

    public async ValueTask DisposeAsync()
    {
        Mqtt.DataChanged -= OnMqttDataChanged;
        Mqtt.CommandEvent -= OnCommandEvent;

        if (MonitorCancellation is not null)
        {
            await MonitorCancellation.CancelAsync();

            if (MonitorTask is not null)
            {
                try
                {
                    await MonitorTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown.
                }
            }

            MonitorCancellation.Dispose();
        }
    }
}
