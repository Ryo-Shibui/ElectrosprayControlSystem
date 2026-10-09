# NE-1000 Pump Integration Design

Goal: Add direct RS-232 NE-1000 syringe pump control to the existing WPF electrospray control app without coupling it to DAQ or camera hardware.

Key requirements:
- DAQ, camera, and syringe pump must initialize and recover independently.
- If one device is disconnected, connected devices remain usable.
- Each device area needs a refresh/reconnect path after startup.
- NE-1000 control uses direct serial communication, not DAQ.
- Start in NE-1000 Basic mode with commands terminated by CR and responses parsed from STX/ETX packets.
- Keep GUI compact by showing routine pump controls and folding connection details/logs.

UI design:
- Add a compact Liquid Supply section in the left column.
- Always show rate, unit, direction, Start/Stop/Purge, connection state, pump state, dispensed volume, alarm.
- Put COM port, Refresh, Connect, baud, address, diameter, volume/continuous, and log in an Expander.
- Add Refresh Hardware to retry DAQ and camera independently.

Code design:
- Add SerialTransport for COM port enumeration, connection lifecycle, queued command/response exchange, and STX/ETX parsing.
- Add Ne1000Controller for Basic mode command semantics.
- Add SyringePumpViewModel for UI state, validation, commands, polling, and log presentation.
- MainViewModel owns DAQ/camera as before and exposes Pump as an independent child view model.
