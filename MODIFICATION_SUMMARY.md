# Modification Summary

This source package updates the UI and camera/DAQ integration to match the latest requested workflow.

## Applied changes

- Added Dino-Lite SDK-first camera initialization:
  - the app now tries `DNX64.dll` from `ElectrosprayControlSystem/ThirdParty/DinoLite/`,
  - DirectShow is used only as a fallback if SDK initialization fails.
- Removed the need to specify Dino-Lite SDK paths in the UI:
  - `DNX64.dll` and `libusbK.dll` are expected in `ThirdParty/DinoLite`.
- Added `Preview zoom factor` to the settings panel.
- Changed snapshot saving behavior:
  - the midpoint snapshot now uses the same zoomed framing currently shown in the preview.
- Changed the right-side layout:
  - overall structure is now 2 rows,
  - top row is split 3:7,
  - top-left is split into `Voltage (V)` and `Current (nA)` panels,
  - top-right is the Dino-Lite preview,
  - bottom row keeps the two live trend plots.
- Kept the existing apply/measure workflow:
  - `Apply` / `Stop Apply`
  - `Measure` / `Stop Measure`
  - live monitoring begins automatically at startup.

## Main files changed

- `ElectrosprayControlSystem/MainWindow.xaml`
- `ElectrosprayControlSystem/ViewModels/MainViewModel.cs`
- `ElectrosprayControlSystem/Models/MeasurementSettings.cs`
- `ElectrosprayControlSystem/Services/ICameraService.cs`
- `ElectrosprayControlSystem/Services/AForgeCameraService.cs`
- `ElectrosprayControlSystem/Services/AutoSwitchCameraService.cs`
- `ElectrosprayControlSystem/Services/DinoLiteSdkLocator.cs`
- `ElectrosprayControlSystem/Services/DinoLiteSdkNative.cs`
- `ElectrosprayControlSystem/Services/DinoLiteSdkCameraService.cs`
- `ElectrosprayControlSystem/Services/HardwareFactory.cs`
- `ElectrosprayControlSystem/ElectrosprayControlSystem.csproj`

## Important note

This package is the source state before build. A Windows WPF build and live Dino-Lite SDK verification were not executed in this environment.


Latest update:
- Imoni is now configured as a fixed USB-6001 differential pair using ai0/ai4, ai1/ai5, ai2/ai6, or ai3/ai7.
- Dino-Lite light control now uses a single toggle button that starts in the Light Off state because the preview is assumed to start with the light on.
- DirectShow preview now selects the maximum available camera resolution automatically.
