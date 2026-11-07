# CA2 Industrial IIoT Dashboard — Integrated Software Edition

## What was integrated
- Original Blazor .NET 10 dashboard and local simulation (NP9, PNP, temperature, adjustable alarm, event history).
- MQTT telemetry subscription; START/STOP control publication; command ID tracking and 5-second acknowledgement timeout.
- **NEW:** MQTT heartbeat subscription (`ca2/conveyor/heartbeat`) with device ID filter (`WAGO_SIM_01`).
- **NEW:** UI heartbeat monitoring (2-second expected cadence; 6-second missing-message timeout; recovery event).
- **NEW:** MQTT broker automatic reconnect checks every 3 seconds and automatic topic re-subscription after each connection.
- **NEW:** `SimulatedWagoDevice.ps1` combines heartbeat publisher and device command responder.

## Launch in Visual Studio (Windows)
1. Open `CA2_Project File.slnx` from solution root. Install .NET 10 SDK and restore NuGet dependencies, including MQTTnet 5.2.0.1603.
2. Ensure Mosquitto runs on `127.0.0.1:1883`.
3. Build Solution. Start app. Open `/industrial-dashboard` and select **MQTT Monitoring Mode**.
4. Run `SimulatedWagoDevice.ps1` from PowerShell (see script header). PowerShell publishes heartbeats and answers START/STOP with matching command IDs.
5. Test START, STOP, ACK and PENDING -> TIMEOUT by stopping the simulator. While stopped, MQTT heartbeat should change to `HEARTBEAT TIMEOUT` after about 6 s. Restart to observe `DEVICE ONLINE` recovery.
6. To test broker reconnect: stop the Mosquitto broker; the page should show `MQTT DISCONNECTED`. Restart broker. The background service automatically reconnects/re-subscribes; restart the simulator if necessary and observe fresh data.

## Configuration
Edit `appsettings.json` -> `Mqtt` section for Host, Port, DeviceId, AckTimeoutSeconds, ReconnectSeconds. The dashboard presently uses a fixed 6-second heartbeat timeout. Use secure MQTT transport and credentials before any production use. Do not commit credentials.

## Important boundaries
- **Not a verified WAGO hardware integration.** The PowerShell simulator is *not* a PLC. NP9 input, RS485/Modbus register mapping, temperature sensor and field wiring require hardware specifications and physical trials.
- MQTT feedback proves a simulated application responded; it does not provide safety integrity, motor interlocks or a guaranteed physical stop. Implement machine safety within the real PLC/hardwired safety circuit.
- Automatic reconnection may temporarily show stale telemetry; the existing 30-second freshness guard marks it invalid. MQTT heartbeat is separate from the original local fault simulation.
- This archive was source-inspected and updated, but **could not be compiled here** because the container has no .NET SDK. Build and test it in Visual Studio. Report any error line and message for corrections.

## Suggested CA2 validation evidence
1. MQTT connected and subscribed, temperature value/PNP update and high temperature alarm.
2. START and STOP acknowledged with matching command IDs.
3. Command acknowledgement timeout when simulator is stopped.
4. Heartbeat online / heartbeat timeout / heartbeat recovery sequence.
5. Mosquitto disconnected / automatic reconnect / resubscribe confirmation Event History.
6. Hardware validation table should remain **NOT TESTED** until WAGO, Modbus, NP9 and sensor equipment is used.
