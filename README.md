# Industrial IIoT Monitoring Dashboard

### Smart Conveyor Weight Monitoring and Overload Detection System

An Industrial Internet of Things (IIoT) application developed using **WAGO Compact Controller 100, CODESYS, MQTT, Beaver IoT, C# .NET and Blazor**.

## 1. Introduction

This project focuses on the development of an Industrial Internet of Things (IIoT) monitoring and control system for a smart conveyor application. The system integrates industrial sensing devices, a programmable logic controller (PLC), MQTT communication and a web-based dashboard to support real-time monitoring, remote control and fault detection.

The WAGO Compact Controller 100 (751-9301) acts as the edge controller, collecting sensor information and processing control logic through CODESYS. Sensor data is transmitted using MQTT and visualised through a C# .NET Blazor dashboard.

The system includes simulated product weight monitoring, configurable overload detection, conveyor visualisation, remote START/STOP commands and communication fault monitoring. A software simulation mode is also provided to demonstrate system behaviour without requiring continuous access to the physical PLC.

## 2. Project Objectives

- Develop an IIoT application integrating industrial sensors, PLC control and MQTT communication.
- Monitor sensor data and operating conditions through a web-based dashboard.
- Implement MQTT Subscribe and Publish for bidirectional communication.
- Provide configurable weight thresholds and overweight alerts.
- Simulate conveyor operation and product inspection.
- Detect communication failures using network fault and heartbeat monitoring.
- Record system events, alarms and recovery activities.

## 3. System Architecture

The system consists of the following main components:

**Industrial Hardware:** WAGO Compact Controller 100, PNP proximity sensor, NP9 push button and associated communication modules.

**PLC Programming:** CODESYS for sensor processing, control logic and MQTT communication.

**Communication:** MQTT broker integrated with Beaver IoT for exchanging telemetry and control messages.

**Web Application:** C# .NET Blazor dashboard for real-time visualisation, remote commands, alarms and system diagnostics.

**Simulation:** Software-based conveyor, weight monitoring, heartbeat timeout and network fault demonstrations.

## 4. Key Features

### Real-Time Monitoring
Displays PNP sensor detection, voltage-derived simulated weight, PLC RunEnable state and telemetry freshness.

### Weight Monitoring and Overload Detection
Converts incoming voltage data into an engineering weight value and compares it against a configurable threshold.

### MQTT Communication
Supports MQTT Subscribe for receiving sensor telemetry and MQTT Publish for sending remote START/STOP commands.

### Conveyor Visualisation
Provides an animated conveyor interface to demonstrate product detection, normal-weight acceptance and overweight rejection conditions.

### Fault Detection and Heartbeat Monitoring
Includes simulated network disconnection, heartbeat timeout detection, communication status warnings and recovery monitoring.

### Event History
Records system events, sensor changes, alarms, communication faults and recovery activities.

## 5. Technologies Used

| Technology | Purpose |
|---|---|
| WAGO CC100 (751-9301) | Industrial edge controller |
| CODESYS | PLC programming and control logic |
| MQTT | Publish/Subscribe communication |
| Beaver IoT | MQTT integration and device management |
| C# / .NET | Application development |
| Blazor | Interactive web-based dashboard |
| MQTTnet | MQTT client communication library |

## 6. Operation Modes

**WAGO MQTT Monitoring Mode**

Receives real-time telemetry from the WAGO controller through the MQTT broker. The dashboard displays live sensor information and provides MQTT-based remote control functions.

**Simulation Mode**

Allows users to demonstrate conveyor operation, weight monitoring, sensor detection, network faults and heartbeat timeout behaviour without connecting to the physical WAGO PLC.

## 7. Testing and Validation

The application supports functional testing of sensor monitoring, configurable weight thresholds, MQTT communication, simulated faults and system recovery.

Software-level fault simulation demonstrates how the dashboard responds to unavailable telemetry, network fault conditions and heartbeat timeouts.

## 8. Security Considerations

MQTT integration is configured with username and password credentials. Additional security improvements for industrial deployment include MQTT over TLS, network segmentation and topic-level access control.

## 9. Project Scope

This system was developed as an educational IIoT prototype for industrial monitoring and control.

The product weight is an engineering simulation derived from an adjustable voltage input. No calibrated physical load cell is used, and the remote START/STOP function controls the PLC's internal test state rather than a physical conveyor motor.
