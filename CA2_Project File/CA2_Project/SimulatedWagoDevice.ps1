# CA2 simulated WAGO device (educational testing only).
# Requires Mosquitto clients installed at C:\Program Files\mosquitto.
# Run in Windows PowerShell: powershell -ExecutionPolicy Bypass -File .\SimulatedWagoDevice.ps1
# Stops both jobs when you press Ctrl+C.
$mosquitto = 'C:\Program Files\mosquitto'
$sub = Join-Path $mosquitto 'mosquitto_sub.exe'
$pub = Join-Path $mosquitto 'mosquitto_pub.exe'
if (!(Test-Path $sub) -or !(Test-Path $pub)) { throw 'Mosquitto client programs not found.' }

# Command responder is separate from heartbeat sender.
$commandJob = Start-Job -ArgumentList $sub,$pub -ScriptBlock {
    param($subPath,$pubPath)
    & $subPath -h 127.0.0.1 -p 1883 -t 'ca2/conveyor/command' |
        ForEach-Object {
            try {
                $msg = $_ | ConvertFrom-Json -ErrorAction Stop
                if ($msg.command -notin @('START','STOP')) { return }
                if (-not $msg.command_id) { return }
                $running = $msg.command -eq 'START'
                Start-Sleep -Milliseconds 300
                $ack = @{
                    command_id = [string]$msg.command_id
                    command = [string]$msg.command
                    result = 'SIMULATED_EXECUTION'
                    run_enable = $running
                    timestamp = (Get-Date).ToUniversalTime().ToString('o')
                } | ConvertTo-Json -Compress
                $ack | & $pubPath -h 127.0.0.1 -p 1883 -t 'ca2/conveyor/status' -s
                $telemetry = @{
                    temperature = 25.0
                    sensor_pnp = 0
                    run_enable = $running
                } | ConvertTo-Json -Compress
                $telemetry | & $pubPath -h 127.0.0.1 -p 1883 -t 'ca2/conveyor/telemetry' -s
                Write-Output "ACK $($msg.command) ID $($msg.command_id)"
            } catch { Write-Output "Processing error: $_" }
        }
}
Write-Host 'Simulated WAGO device started (commands + heartbeat).'
Write-Host 'Press Ctrl+C to simulate device failure.'
try {
    while ($true) {
        $beat = @{
            device_id = 'WAGO_SIM_01'
            status = 'ONLINE'
            timestamp = (Get-Date).ToUniversalTime().ToString('o')
        } | ConvertTo-Json -Compress
        $beat | & $pub -h 127.0.0.1 -p 1883 -t 'ca2/conveyor/heartbeat' -s
        Write-Host "Heartbeat $(Get-Date -Format HH:mm:ss)"
        Receive-Job $commandJob | ForEach-Object { Write-Host $_ }
        Start-Sleep -Seconds 2
    }
} finally {
    Stop-Job $commandJob -ErrorAction SilentlyContinue
    Remove-Job $commandJob -Force -ErrorAction SilentlyContinue
}
