# Setup Guide

## 1. Install Visual Studio

Install Visual Studio with:

- `.NET desktop development`

## 2. Install .NET Framework 4.8.1 Developer Pack

Install the .NET Framework 4.8.1 Developer Pack if Visual Studio does not already offer `net481` targeting.

## 3. Install NI-DAQmx

Install NI-DAQmx after Visual Studio is installed. During NI-DAQmx installation, keep the .NET / API support enabled.

NI notes that the .NET support can be installed separately under Application Development Support, and the NI-DAQmx .NET assembly is installed with NI driver support. NI also documents that the DAQmx assembly can live in Measurement Studio-named folders even if Measurement Studio itself is not installed.

## 4. Optional local fallback for NI DLLs

If your PC already has the NI DLLs but the app still cannot find them, create this folder in the project:

- `ElectrosprayControlSystem\ThirdParty\NI`

Then copy your own files there:

- `NationalInstruments.DAQmx.dll`
- `NationalInstruments.Common.dll`

After copying, rebuild the solution. The project is configured to copy those DLLs next to the EXE automatically.

## 5. Place Dino-Lite SDK runtime files

Create or use this folder in the project:

- `ElectrosprayControlSystem\ThirdParty\DinoLite`

Then copy these files there:

- `DNX64.dll`
- `libusbK.dll`

No UI path entry is required. The application loads them automatically from the project/output folder structure.

## 6. Open the solution

Open:

- `ElectrosprayControlSystem.sln`

## 7. Restore NuGet packages

Allow NuGet restore for:

- `AForge.Video`
- `AForge.Video.DirectShow`
- `ScottPlot.WPF`

## 8. Build settings

Use:

- Configuration: `Release`
- Platform: `x64`

## 9. Runtime checks

1. Confirm the USB-6001 appears in NI MAX.
2. Confirm NI-DAQmx .NET support is installed.
3. Confirm the app is using the correct device name (`Dev1`, etc.).
4. Confirm the Dino-Lite appears as a Windows camera device.
5. If Dino-Lite SDK loading fails, verify both `DNX64.dll` and `libusbK.dll` are present under `ThirdParty\DinoLite` and rebuild.


Latest update:
- Imoni is now configured as a fixed USB-6001 differential pair using ai0/ai4, ai1/ai5, ai2/ai6, or ai3/ai7.
- Dino-Lite light control now uses a single toggle button that starts in the Light Off state because the preview is assumed to start with the light on.
- DirectShow preview now selects the maximum available camera resolution automatically.
