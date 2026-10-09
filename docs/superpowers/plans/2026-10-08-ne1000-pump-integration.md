# NE-1000 Pump Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add compact, independent NE-1000 RS-232 pump control to the existing electrospray WPF app.

**Architecture:** Keep DAQ, camera, and pump as independent device surfaces. Implement serial transport and NE-1000 command semantics below a dedicated pump view model, then bind that view model into the existing left-column UI.

**Tech Stack:** C#, WPF, .NET Framework 4.8.1, System.IO.Ports, existing MVVM helpers.

**Spec:** docs/superpowers/specs/2026-10-08-ne1000-pump-integration-design.md

## Global Constraints

- Do not route syringe pump control through DAQ.
- A missing DAQ, camera, or pump must not block connected devices from working.
- Keep pump details/logs folded by default.
- Build must remain Visual Studio compatible for Release|x64 and Debug|x64.

## Review Focus

- Serial response parsing with STX/ETX frames and noisy bytes.
- Independent startup and refresh behavior when DAQ/camera are absent.
- UI command validation so Start/Purge cannot send incomplete pump settings.
- Shutdown closes the serial port without affecting DAQ/camera cleanup.
- GUI remains usable in the existing 340px left column.

---

### Task 1: Pump Protocol Core

**Files:** Create `ElectrosprayControlSystem/Services/SyringePump/SerialTransport.cs`, `Ne1000Controller.cs`, model helpers, and a lightweight test project.

**Interfaces:** Produce `ISerialTransport`, `SerialTransport`, `Ne1000Controller`, `Ne1000Controller.FormatBasicCommand`, `SerialTransport.ExtractPacketsForTest`.

- [ ] Write failing parser/command tests.
- [ ] Implement serial transport and controller.
- [ ] Run tests and confirm pass.

### Task 2: Pump View Model

**Files:** Create `ElectrosprayControlSystem/ViewModels/SyringePumpViewModel.cs`; modify `MainViewModel.cs` for child lifecycle.

**Interfaces:** Produce `Pump` property and commands for RefreshPorts, Connect, Start, Stop, Purge, RefreshStatus.

- [ ] Add validation tests for pump start settings.
- [ ] Implement view model.
- [ ] Run tests and confirm pass.

### Task 3: GUI Integration and Device Refresh

**Files:** Modify `MainWindow.xaml`, `MainViewModel.cs`, docs.

**Interfaces:** Bind compact Liquid Supply section and hardware refresh command.

- [ ] Add UI bindings without changing existing DAQ/camera layout semantics.
- [ ] Add Refresh Hardware for DAQ/camera retry.
- [ ] Build solution.
- [ ] Manually test pump connection on available COM port.
