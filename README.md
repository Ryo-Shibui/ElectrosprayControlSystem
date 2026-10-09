# Electrospray Control System

This is a Windows desktop application for automating an electrospray experiment. It combines voltage control, voltage/current monitoring, camera preview and image capture, and syringe-pump control in one WPF application.

The goal of this README is that a person starting from a new Windows PC, without Visual Studio or device drivers installed yet, can understand what to prepare and how to run the program.

## What This Program Does

- Sends an analog control voltage to a high-voltage module through a National Instruments DAQ analog output
- Continuously monitors Vmoni and Imoni through DAQ analog input channels
- Shows a live Dino-Lite or DirectShow camera preview
- Records a timed measurement to CSV and metadata files
- Saves one camera image at the midpoint of a measurement
- Controls an NE-1000 syringe pump over a Windows COM port
- Keeps DAQ, camera, and pump recovery mostly independent, so one missing device does not necessarily block the others

## Hardware Compatibility

The developer tested this workflow with an NI USB-6001, a Dino-Lite camera, and an NE-1000 syringe pump. The code is written around capabilities rather than USB-6001-only identification, so compatible alternatives may work if they expose the same interfaces.

### DAQ

The DAQ path uses the NI-DAQmx .NET API. The software should work with National Instruments DAQ hardware that:

- is supported by NI-DAQmx on Windows;
- appears in NI MAX;
- has at least one analog output channel for the high-voltage module control signal;
- has analog input channels for Vmoni and Imoni;
- supports the selected voltage ranges;
- supports RSE input for Vmoni;
- supports one of the differential input pairs used for Imoni.

The default channel settings were developed for the tested USB-6001 setup:

- DAQ device name: `Dev3`
- Analog output channel: `ao0`
- Vmoni input channel: `ai3`
- Imoni differential pair: `ai0` / `ai4`

On another PC or another NI DAQ, use NI MAX to find the actual device name and channel names, then update them in the GUI.

### Camera

The camera path tries the Dino-Lite SDK first and then falls back to a normal Windows DirectShow camera. This means:

- Dino-Lite cameras can use the SDK path when `DNX64.dll` and `libusbK.dll` are available.
- A camera that appears as a normal Windows video device may work through the DirectShow fallback.
- Dino-Lite-specific functions, such as SDK light control behavior, require the Dino-Lite SDK runtime.

### Syringe Pump

The pump panel was developed for an NE-1000 syringe pump using its serial command set. It should work with an NE-1000-compatible pump that:

- uses the same command protocol;
- is reachable through a Windows COM port;
- can communicate at 19200 baud, 8 data bits, no parity, 1 stop bit, and no flow control.

Other pump models will require code changes unless they intentionally implement the same serial protocol.

### High-Voltage Module

The high-voltage module does not have to be one exact model if it accepts an analog control voltage. The GUI maps desired kV to DAQ output voltage using:

- HV minimum
- HV maximum
- Control signal maximum

The tested default is 0 to 6 kV mapped to 0 to 5 V. Change these values to match the actual high-voltage module before applying output.

## Prepare These Items

- 64-bit Windows 10 or Windows 11 PC
- Visual Studio (for now "10/09/2026", Visual Studio 2026)
- .NET Framework 4.8.1 Developer Pack
- NI-DAQmx with .NET/API support
- NI DAQ device with the analog I/O capabilities described above
- High-voltage module and safe wiring to the DAQ analog output
- Vmoni and Imoni wiring to DAQ analog inputs
- Dino-Lite camera or another Windows-visible camera
- Dino-Lite SDK runtime files if SDK features are needed
- NE-1000 or compatible syringe pump if pump control is needed
- USB-RS232 adapter or serial port for the pump
- Folder for measurement data

## Install on a Fresh PC

1. Run Windows Update.
2. Install Visual Studio.
3. In Visual Studio Installer, select the `.NET desktop development` workload.
4. Install the .NET Framework 4.8.1 Developer Pack if Visual Studio cannot target `net481`.
5. Install NI-DAQmx after Visual Studio. Keep .NET/API support enabled.
6. Open NI MAX and confirm that the DAQ device is visible.
7. Install the camera driver and Dino-Lite SDK if using the Dino-Lite SDK path.
8. Connect the USB-RS232 adapter for the pump and confirm that Windows shows a COM port.

## Vendor DLLs

This public repository does not include NI or Dino-Lite vendor DLLs. Obtain them from the official driver or SDK installers for your own PC.

If NI-DAQmx .NET assemblies are not found automatically, copy your installed files to:

```text
ElectrosprayControlSystem\ThirdParty\NI\
```

Expected files:

```text
NationalInstruments.DAQmx.dll
NationalInstruments.Common.dll
```

If using the Dino-Lite SDK path, copy the SDK runtime files to:

```text
ElectrosprayControlSystem\ThirdParty\DinoLite\
```

Expected files:

```text
DNX64.dll
libusbK.dll
```

If these Dino-Lite files are absent, the application can still try DirectShow if the camera appears as a normal Windows camera.

## Build and Run

1. Download the repository from GitHub with `Code` -> `Download ZIP`, or clone it with Git.
2. If you downloaded a ZIP file, extract it to a normal working folder.
3. Place vendor DLLs under `ThirdParty` if your setup needs local copies.
4. Open `ElectrosprayControlSystem.sln` in Visual Studio 2022.
5. Allow NuGet restore.
6. Select `Release` and `x64`.
7. Run `Build` -> `Build Solution`.
8. Start the program with `Start` or `F5`.

You can also build from PowerShell:

```powershell
.\build-release.ps1
```

The built application is normally created at:

```text
ElectrosprayControlSystem\bin\x64\Release\net481\ElectrosprayControlSystem.exe
```

## DAQ Settings

The GUI exposes the important DAQ settings. Confirm them before applying voltage:

- `DAQ device name`
- `Analog output channel`
- `Vmoni input channel`
- `Imoni differential pair`
- `Vmoni scale`
- `Vmoni offset`
- `Imoni scale`
- `Imoni offset`

The Imoni differential input selector supports these USB-6001-style differential pairs:

- `ai0` / `ai4`
- `ai1` / `ai5`
- `ai2` / `ai6`
- `ai3` / `ai7`

If a different NI DAQ uses different valid differential channel naming or pairing, the code may need to be adjusted.

## Basic Operation

1. Connect the DAQ, camera, and pump devices that you plan to use.
2. Use NI MAX to confirm the DAQ device name and channel names.
3. Start the application.
4. Confirm or edit the DAQ settings in `USB-6001 Channels`.
5. Confirm the HV range, control voltage maximum, and Vmoni/Imoni scaling.
6. Click `Refresh Hardware` to retry DAQ and camera discovery.
7. Enter the target voltage in `Applied voltage [kV]`.
8. Click `Apply` to output the corresponding DAQ control voltage.
9. Click `Stop Apply` to return the DAQ output to 0 V.
10. Set measurement duration, sampling interval, and save path.
11. Click `Measure` to save CSV, metadata, and the midpoint camera image.
12. Click `Stop Measure` if you need to end recording early.

Measurement files are written to a timestamped session folder inside the selected save folder.

## NE-1000 Pump Operation

Use the `Liquid Supply` panel for pump control.

1. Connect the USB-RS232 adapter.
2. Open `Pump Settings`.
3. Click `Refresh` to update COM ports.
4. Select the pump COM port.
5. Confirm the serial settings. The default is 19200 baud, 8 data bits, no parity, 1 stop bit, no flow control.
6. Click `Connect`.
7. Set syringe diameter, flow rate, unit, direction, and volume or continuous mode.
8. Click `Start` to begin pumping.
9. Click `Stop` to stop pumping.
10. Use `Purge` only after confirming the physical setup is safe.

## Before Using Experimental Data

Check the following on the actual setup:

- The DAQ device is visible in NI MAX.
- The GUI device name matches NI MAX.
- The analog output is wired to the high-voltage module control input correctly.
- The high-voltage system is safe before clicking `Apply`.
- Vmoni and Imoni wiring and scaling are correct.
- The camera preview is visible.
- A midpoint image is saved during measurement.
- The pump responds on the selected COM port.
- CSV, metadata, and image files are created in the selected save folder.

## Troubleshooting

- DAQ not found: check NI-DAQmx, NI MAX, USB connection, and device name.
- `NationalInstruments.DAQmx.dll` not found: install NI-DAQmx .NET support or place the DLLs under `ThirdParty\NI`.
- Dino-Lite SDK not found: place `DNX64.dll` and `libusbK.dll` under `ThirdParty\DinoLite`.
- Camera not visible: confirm that Windows sees it as a camera device.
- Pump not responding: check COM port, baud rate, pump address, and RS-232 cabling.
- No measurement files: check write permission for the save folder.
- Applied high voltage is wrong: check HV minimum, HV maximum, control signal maximum, and the high-voltage module control-input specification.

## More Setup Notes

`SETUP_GUIDE.md` contains additional setup notes. Use this README as the main guide, then refer to `SETUP_GUIDE.md` for extra detail.
