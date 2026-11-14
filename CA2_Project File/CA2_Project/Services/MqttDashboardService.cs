
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;

namespace CA2_Project.Services
{
    public sealed class MqttDashboardService : IDisposable
    {
        // ==========================================
        // MQTT CONFIGURATION
        // ==========================================

        private const string BrokerHost =
            "192.168.101.59";

        private const int BrokerPort = 1883;

        private const string BrokerUsername =
            "mqtt@default";

        public const string TelemetryTopic =
            "beaver-iot/mqtt@default/mqtt-device/LAIJIUNNWEI_CA2";

        public const string CommandTopic =
            "ca2/conveyor/command";

        public const string StatusTopic =
            "ca2/conveyor/status";

        private const int AckTimeoutSeconds = 5;

        private readonly MqttClientFactory factory = new();
        private readonly IMqttClient client;
        private readonly object commandLock = new();

        private string? pendingCommandId;
        private bool disposed;

        // ==========================================
        // CONNECTION STATUS
        // ==========================================

        public bool IsConnected => client.IsConnected;

        public string LastError { get; private set; } = "";

        public string DeviceStatus { get; private set; } =
            "NO DATA";

        public string LastMessage { get; private set; } =
            "No telemetry message received";

        public DateTime? LastMessageReceivedAt
        {
            get;
            private set;
        }

        // ==========================================
        // TELEMETRY DIAGNOSTICS
        // ==========================================

        public long TelemetryReceivedCount
        {
            get;
            private set;
        }

        public long TelemetryErrorCount
        {
            get;
            private set;
        }

        public string LastTelemetryError
        {
            get;
            private set;
        } = "";

        // ==========================================
        // WAGO TELEMETRY
        // ==========================================

        public string DeviceId { get; private set; } = "";

        public string DeviceName { get; private set; } = "";

        public double Temperature { get; private set; }

        public double Voltage { get; private set; }

        public bool HasVoltage { get; private set; }

        public bool SensorDetected { get; private set; }

        public bool RunEnable { get; private set; }

        public bool HasReceivedData { get; private set; }

        public DateTime? LastUpdated { get; private set; }

        // ==========================================
        // PHYSICAL EVENT COUNTERS
        // ==========================================

        public bool HasPhysicalEventCounters
        {
            get;
            private set;
        }

        public uint NP9PressCount { get; private set; }

        public uint PNPDetectCount { get; private set; }

        public uint PNPRemoveCount { get; private set; }

        // ==========================================
        // COMMAND ACK
        // ==========================================

        public string LastCommandId { get; private set; } =
            "None";

        public string LastCommand { get; private set; } =
            "None";

        public string CommandState { get; private set; } =
            "NO COMMAND";

        public string CommandDetail { get; private set; } =
            "No command has been sent.";

        public bool IsCommandPending
        {
            get
            {
                lock (commandLock)
                {
                    return pendingCommandId != null;
                }
            }
        }

        // ==========================================
        // EVENTS
        // ==========================================

        public event Action? DataChanged;

        public event Action<string, string>? CommandEvent;

        // ==========================================
        // CONSTRUCTOR
        // ==========================================

        public MqttDashboardService()
        {
            client = factory.CreateMqttClient();

            client.ApplicationMessageReceivedAsync += e =>
            {
                string topic = e.ApplicationMessage.Topic;

                string payload =
                    e.ApplicationMessage.ConvertPayloadToString();

                try
                {
                    if (topic == TelemetryTopic)
                    {
                        LastMessage = payload;
                        LastMessageReceivedAt = DateTime.Now;

                        TelemetryReceivedCount++;

                        ProcessTelemetry(payload);

                        LastTelemetryError = "";
                        LastError = "";
                    }
                    else if (topic == StatusTopic)
                    {
                        ProcessStatus(payload);
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;

                    if (topic == TelemetryTopic)
                    {
                        TelemetryErrorCount++;

                        LastTelemetryError = ex.Message;
                    }
                }
                finally
                {
                    DataChanged?.Invoke();
                }

                return Task.CompletedTask;
            };

            client.ConnectedAsync += e =>
            {
                DataChanged?.Invoke();
                return Task.CompletedTask;
            };

            client.DisconnectedAsync += e =>
            {
                DataChanged?.Invoke();
                return Task.CompletedTask;
            };
        }

        // ==========================================
        // CONNECT TO MQTT BROKER
        // ==========================================

        public async Task ConnectAsync()
        {
            if (client.IsConnected || disposed)
                return;

            try
            {
                string password =
                    Environment.GetEnvironmentVariable(
                        "BEAVER_MQTT_PASSWORD")
                    ?? throw new InvalidOperationException(
                        "BEAVER_MQTT_PASSWORD is not configured.");

                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer(BrokerHost, BrokerPort)
                    .WithCredentials(
                        BrokerUsername,
                        password)
                    .WithClientId(
                        "CA2_Blazor_" +
                        Guid.NewGuid().ToString("N")[..8])
                    .Build();

                await client.ConnectAsync(options);

                var subscribeOptions = factory
                    .CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(TelemetryTopic)
                    .WithTopicFilter(StatusTopic)
                    .Build();

                await client.SubscribeAsync(subscribeOptions);

                LastError = "";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            DataChanged?.Invoke();
        }

        // ==========================================
        // PROCESS TELEMETRY
        // ==========================================

        private void ProcessTelemetry(string payload)
        {
            using var document = JsonDocument.Parse(payload);

            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(
                    "Telemetry root must be a JSON object.");
            }

            // Device identification is mandatory.
            if (!root.TryGetProperty(
                "device_id", out var deviceElement))
            {
                throw new JsonException(
                    "Telemetry missing device_id.");
            }

            string deviceId =
                deviceElement.GetString() ?? "";

            if (deviceId != "LAIJIUNNWEI_CA2")
            {
                throw new JsonException(
                    $"Unexpected device_id: {deviceId}");
            }

            // Read voltage independently of temperature.
            bool hasVoltage = TryReadDouble(
                root,
                "voltage",
                out double newVoltage
            );

            if (!hasVoltage)
            {
                throw new JsonException(
                    "Voltage field missing or invalid.");
            }

            // Read optional telemetry fields.
            string deviceName = DeviceName;

            if (root.TryGetProperty(
                "device_name", out var nameElement) &&
                nameElement.ValueKind ==
                    JsonValueKind.String)
            {
                deviceName =
                    nameElement.GetString() ?? "";
            }

            bool newSensor = SensorDetected;

            if (root.TryGetProperty(
                "sensor_pnp", out var sensorElement))
            {
                if (sensorElement.ValueKind ==
                    JsonValueKind.Number &&
                    sensorElement.TryGetInt32(
                        out int sensorValue))
                {
                    newSensor = sensorValue != 0;
                }
                else if (sensorElement.ValueKind ==
                    JsonValueKind.True)
                {
                    newSensor = true;
                }
                else if (sensorElement.ValueKind ==
                    JsonValueKind.False)
                {
                    newSensor = false;
                }
            }

            bool newRunEnable = RunEnable;

            if (root.TryGetProperty(
                "run_enable", out var runElement))
            {
                if (runElement.ValueKind ==
                    JsonValueKind.True)
                {
                    newRunEnable = true;
                }
                else if (runElement.ValueKind ==
                    JsonValueKind.False)
                {
                    newRunEnable = false;
                }
            }

            double newTemperature = Temperature;

            if (TryReadDouble(
                root,
                "temperature",
                out double temperatureValue))
            {
                newTemperature = temperatureValue;
            }

            bool hasNP9 = TryReadUInt(
                root,
                "np9_press_count",
                out uint np9Count
            );

            bool hasDetect = TryReadUInt(
                root,
                "pnp_detect_count",
                out uint detectCount
            );

            bool hasRemove = TryReadUInt(
                root,
                "pnp_remove_count",
                out uint removeCount
            );

            // ======================================
            // UPDATE DASHBOARD DATA
            // ======================================

            DeviceId = deviceId;
            DeviceName = deviceName;

            Voltage = newVoltage;
            HasVoltage = true;

            Temperature = newTemperature;

            SensorDetected = newSensor;
            RunEnable = newRunEnable;

            HasPhysicalEventCounters =
                hasNP9 && hasDetect && hasRemove;

            if (HasPhysicalEventCounters)
            {
                NP9PressCount = np9Count;
                PNPDetectCount = detectCount;
                PNPRemoveCount = removeCount;
            }

            HasReceivedData = true;
            LastUpdated = DateTime.Now;
        }

        // ==========================================
        // JSON NUMERIC HELPERS
        // ==========================================

        private static bool TryReadDouble(
            JsonElement root,
            string propertyName,
            out double value)
        {
            value = 0;

            if (!root.TryGetProperty(
                propertyName, out var element))
            {
                return false;
            }

            if (element.ValueKind !=
                JsonValueKind.Number)
            {
                return false;
            }

            if (!element.TryGetDouble(out value))
            {
                return false;
            }

            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        private static bool TryReadUInt(
            JsonElement root,
            string propertyName,
            out uint value)
        {
            value = 0;

            if (!root.TryGetProperty(
                propertyName, out var element))
            {
                return false;
            }

            return element.ValueKind ==
                       JsonValueKind.Number &&
                   element.TryGetUInt32(out value);
        }

        // ==========================================
        // PROCESS COMMAND ACK
        // ==========================================

        private void ProcessStatus(string payload)
        {
            DeviceStatus = payload;

            using var document = JsonDocument.Parse(payload);

            var root = document.RootElement;

            if (!root.TryGetProperty(
                "command_id", out var idElement))
            {
                return;
            }

            string receivedId =
                idElement.GetString() ?? "";

            string command = root.TryGetProperty(
                "command", out var commandElement)
                ? commandElement.GetString() ?? ""
                : "";

            string result = root.TryGetProperty(
                "result", out var resultElement)
                ? resultElement.GetString() ?? ""
                : "";

            bool matched = false;

            lock (commandLock)
            {
                if (pendingCommandId != null &&
                    pendingCommandId == receivedId &&
                    command == LastCommand)
                {
                    pendingCommandId = null;

                    if (result == "SIMULATED_EXECUTION")
                    {
                        CommandState = "ACKNOWLEDGED";

                        CommandDetail =
                            "Matching WAGO acknowledgement received.";
                    }
                    else
                    {
                        CommandState = "DEVICE RESPONSE";

                        CommandDetail =
                            $"Device returned result: {result}";
                    }

                    matched = true;
                }
            }

            if (matched)
            {
                CommandEvent?.Invoke(
                    result == "SIMULATED_EXECUTION"
                        ? "RECOVERY"
                        : "WARNING",
                    $"WAGO response: {command}, " +
                    $"ID: {receivedId}, Result: {result}"
                );
            }
            else
            {
                CommandEvent?.Invoke(
                    "INFO",
                    $"Unmatched WAGO device response. " +
                    $"Command ID: {receivedId}"
                );
            }
        }

        // ==========================================
        // PUBLISH START / STOP COMMAND
        // ==========================================

        public async Task PublishCommandAsync(string command)
        {
            if (!client.IsConnected)
            {
                throw new InvalidOperationException(
                    "MQTT Broker is disconnected.");
            }

            if (command != "START" && command != "STOP")
            {
                throw new ArgumentException(
                    "Only START and STOP are supported.");
            }

            string commandId =
                Guid.NewGuid().ToString("N");

            lock (commandLock)
            {
                if (pendingCommandId != null)
                {
                    throw new InvalidOperationException(
                        "Another command is awaiting acknowledgement.");
                }

                pendingCommandId = commandId;

                LastCommandId = commandId;
                LastCommand = command;

                CommandState = "PENDING";

                CommandDetail =
                    "Waiting for WAGO acknowledgement.";
            }

            DataChanged?.Invoke();

            string payload = JsonSerializer.Serialize(new
            {
                command_id = commandId,
                command,
                timestamp = DateTimeOffset.UtcNow
            });

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(CommandTopic)
                .WithPayload(payload)
                .Build();

            try
            {
                await client.PublishAsync(message);
            }
            catch (Exception ex)
            {
                lock (commandLock)
                {
                    if (pendingCommandId == commandId)
                    {
                        pendingCommandId = null;

                        CommandState = "PUBLISH FAILED";
                        CommandDetail = ex.Message;
                    }
                }

                CommandEvent?.Invoke(
                    "WARNING",
                    $"MQTT Publish failed: {ex.Message}"
                );

                DataChanged?.Invoke();

                throw;
            }

            CommandEvent?.Invoke(
                "INFO",
                $"MQTT {command} command sent. " +
                $"Command ID: {commandId}"
            );

            _ = WatchAcknowledgementAsync(commandId);
        }

        // ==========================================
        // ACK TIMEOUT
        // ==========================================

        private async Task WatchAcknowledgementAsync(
            string commandId)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(AckTimeoutSeconds)
            );

            if (disposed)
                return;

            bool timedOut = false;

            lock (commandLock)
            {
                if (pendingCommandId == commandId)
                {
                    pendingCommandId = null;

                    CommandState = "TIMEOUT";

                    CommandDetail =
                        "No matching WAGO acknowledgement " +
                        "received within 5 seconds.";

                    timedOut = true;
                }
            }

            if (timedOut)
            {
                CommandEvent?.Invoke(
                    "ALARM",
                    "WAGO command acknowledgement timeout. " +
                    $"ID: {commandId}"
                );

                DataChanged?.Invoke();
            }
        }

        // ==========================================
        // CLEANUP
        // ==========================================

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            client.Dispose();
        }
    }
}
