using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ElectrosprayControlSystem.Models;

namespace ElectrosprayControlSystem.Services
{
    public class NiUsb6001DaqService : IDaqService
    {
        private Assembly _daqmxAssembly;
        private object _aoTask;
        private object _vmoniTask;
        private object _imoniTask;
        private object _aoWriter;
        private object _vmoniReader;
        private object _imoniReader;
        private MeasurementSettings _settings;

        public string BackendName => "NI-DAQmx (.NET API)";

        public Task InitializeAsync(MeasurementSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ValidateDifferentialPair(settings);
            _daqmxAssembly = LoadDaqmxAssembly();

            Type taskType = GetTypeOrThrow("NationalInstruments.DAQmx.Task");
            Type aoUnitsType = GetTypeOrThrow("NationalInstruments.DAQmx.AOVoltageUnits");
            Type aiUnitsType = GetTypeOrThrow("NationalInstruments.DAQmx.AIVoltageUnits");
            Type aiTermType = GetTypeOrThrow("NationalInstruments.DAQmx.AITerminalConfiguration");
            Type writerType = GetTypeOrThrow("NationalInstruments.DAQmx.AnalogSingleChannelWriter");
            Type readerType = GetTypeOrThrow("NationalInstruments.DAQmx.AnalogSingleChannelReader");

            _aoTask = Activator.CreateInstance(taskType);
            object aoChannels = taskType.GetProperty("AOChannels").GetValue(_aoTask);
            string physicalAo = BuildPhysicalChannel(settings.DaqDeviceName, settings.AnalogOutputChannel);
            object aoUnits = Enum.Parse(aoUnitsType, "Volts");
            InvokeCreateAoVoltageChannel(aoChannels, physicalAo, settings.ControlSignalMaximumV, aoUnits);
            object aoStream = taskType.GetProperty("Stream").GetValue(_aoTask);
            _aoWriter = Activator.CreateInstance(writerType, aoStream);

            object aiUnits = Enum.Parse(aiUnitsType, "Volts");
            object rseTerm = Enum.Parse(aiTermType, "Rse");
            object diffTerm = Enum.Parse(aiTermType, "Differential");

            _vmoniTask = Activator.CreateInstance(taskType);
            object vmoniChannels = taskType.GetProperty("AIChannels").GetValue(_vmoniTask);
            string physicalVmoni = BuildPhysicalChannel(settings.DaqDeviceName, settings.VmoniChannel);
            InvokeCreateAiVoltageChannel(vmoniChannels, physicalVmoni, rseTerm, aiUnits);
            object vmoniStream = taskType.GetProperty("Stream").GetValue(_vmoniTask);
            _vmoniReader = Activator.CreateInstance(readerType, vmoniStream);

            _imoniTask = Activator.CreateInstance(taskType);
            object imoniChannels = taskType.GetProperty("AIChannels").GetValue(_imoniTask);
            string physicalImoniPositive = BuildPhysicalChannel(settings.DaqDeviceName, settings.ImoniPositiveChannel);
            InvokeCreateAiVoltageChannel(imoniChannels, physicalImoniPositive, diffTerm, aiUnits);
            object imoniStream = taskType.GetProperty("Stream").GetValue(_imoniTask);
            _imoniReader = Activator.CreateInstance(readerType, imoniStream);

            return Task.CompletedTask;
        }

        public Task SetControlVoltageAsync(double voltageV, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();
            double clamped = Math.Max(0.0, Math.Min(_settings.ControlSignalMaximumV, voltageV));
            InvokeMethod(_aoWriter, "WriteSingleSample", true, clamped);
            return Task.CompletedTask;
        }

        public Task<DaqReading> ReadMonitorsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            double vmoniValue = Convert.ToDouble(InvokeMethod(_vmoniReader, "ReadSingleSample"), CultureInfo.InvariantCulture);
            double imoniValue = Convert.ToDouble(InvokeMethod(_imoniReader, "ReadSingleSample"), CultureInfo.InvariantCulture);

            return Task.FromResult(new DaqReading
            {
                VmoniV = vmoniValue,
                ImoniV = imoniValue
            });
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            SafeDispose(_aoTask);
            SafeDispose(_vmoniTask);
            SafeDispose(_imoniTask);
            _aoTask = null;
            _vmoniTask = null;
            _imoniTask = null;
            _aoWriter = null;
            _vmoniReader = null;
            _imoniReader = null;
            return Task.CompletedTask;
        }

        private void EnsureInitialized()
        {
            if (_daqmxAssembly == null || _aoWriter == null || _vmoniReader == null || _imoniReader == null)
            {
                throw new InvalidOperationException("DAQ service is not initialized.");
            }
        }

        private Type GetTypeOrThrow(string fullName)
        {
            Type type = _daqmxAssembly.GetType(fullName, throwOnError: false);
            if (type == null)
            {
                throw new InvalidOperationException($"Could not find type '{fullName}' in NationalInstruments.DAQmx assembly.");
            }
            return type;
        }

        private static string BuildPhysicalChannel(string deviceName, string channel)
        {
            return $"{deviceName.Trim()}/{channel.Trim()}";
        }

        private static void SafeDispose(object obj)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                obj.GetType().GetMethod("Dispose")?.Invoke(obj, null);
            }
            catch
            {
            }
        }

        private static void ValidateDifferentialPair(MeasurementSettings settings)
        {
            string ch = (settings?.ImoniPositiveChannel ?? string.Empty).Trim().ToLowerInvariant();
            if (ch != "ai0" && ch != "ai1" && ch != "ai2" && ch != "ai3")
            {
                throw new InvalidOperationException("Imoni differential pair must use ai0/ai4, ai1/ai5, ai2/ai6, or ai3/ai7. Set Imoni positive channel to ai0, ai1, ai2, or ai3.");
            }
        }

        private void InvokeCreateAoVoltageChannel(object aoChannels, string physicalAo, double maximumVoltage, object aoUnits)
        {
            object[] sixArgs = { physicalAo, string.Empty, 0.0, maximumVoltage, aoUnits, string.Empty };
            object[] fiveArgs = { physicalAo, string.Empty, 0.0, maximumVoltage, aoUnits };

            if (TryInvokeCompatibleMethod(aoChannels, "CreateVoltageChannel", sixArgs, out _))
            {
                return;
            }

            if (TryInvokeCompatibleMethod(aoChannels, "CreateVoltageChannel", fiveArgs, out _))
            {
                return;
            }

            throw new MissingMethodException(aoChannels.GetType().FullName, "CreateVoltageChannel");
        }

        private void InvokeCreateAiVoltageChannel(object aiChannels, string physicalChannel, object terminalConfiguration, object aiUnits)
        {
            object[] sevenArgs = { physicalChannel, string.Empty, terminalConfiguration, -10.0, 10.0, aiUnits, string.Empty };
            object[] sixArgs = { physicalChannel, string.Empty, terminalConfiguration, -10.0, 10.0, aiUnits };

            if (TryInvokeCompatibleMethod(aiChannels, "CreateVoltageChannel", sevenArgs, out _))
            {
                return;
            }

            if (TryInvokeCompatibleMethod(aiChannels, "CreateVoltageChannel", sixArgs, out _))
            {
                return;
            }

            throw new MissingMethodException(aiChannels.GetType().FullName, "CreateVoltageChannel");
        }

        private object InvokeMethod(object target, string methodName, params object[] args)
        {
            if (TryInvokeCompatibleMethod(target, methodName, args, out object result))
            {
                return result;
            }

            throw new MissingMethodException(target.GetType().FullName, methodName);
        }

        private static bool TryInvokeCompatibleMethod(object target, string methodName, object[] args, out object result)
        {
            foreach (MethodInfo method in target.GetType().GetMethods().Where(m => m.Name == methodName))
            {
                if (!TryBuildCompatibleArguments(method, args, out object[] convertedArgs))
                {
                    continue;
                }

                result = method.Invoke(target, convertedArgs);
                return true;
            }

            result = null;
            return false;
        }

        private static bool TryBuildCompatibleArguments(MethodInfo method, object[] args, out object[] convertedArgs)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != args.Length)
            {
                convertedArgs = null;
                return false;
            }

            convertedArgs = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (!TryConvertArgument(args[i], parameters[i].ParameterType, out convertedArgs[i]))
                {
                    convertedArgs = null;
                    return false;
                }
            }

            return true;
        }

        private static bool TryConvertArgument(object value, Type targetType, out object converted)
        {
            if (value == null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                {
                    converted = null;
                    return true;
                }

                converted = null;
                return false;
            }

            Type nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            Type valueType = value.GetType();

            if (nonNullableTarget.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            try
            {
                if (nonNullableTarget.IsEnum)
                {
                    if (valueType.IsEnum && string.Equals(valueType.FullName, nonNullableTarget.FullName, StringComparison.Ordinal))
                    {
                        converted = value;
                        return true;
                    }

                    if (value is string s)
                    {
                        converted = Enum.Parse(nonNullableTarget, s, ignoreCase: true);
                        return true;
                    }

                    object numeric = Convert.ChangeType(value, Enum.GetUnderlyingType(nonNullableTarget), CultureInfo.InvariantCulture);
                    converted = Enum.ToObject(nonNullableTarget, numeric);
                    return true;
                }

                converted = Convert.ChangeType(value, nonNullableTarget, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                converted = null;
                return false;
            }
        }

        private static Assembly LoadDaqmxAssembly()
        {
            try
            {
                return Assembly.Load("NationalInstruments.DAQmx");
            }
            catch
            {
            }

            List<string> searchedPaths = new List<string>();
            foreach (string path in GetCandidateAssemblyPaths(searchedPaths))
            {
                try
                {
                    return Assembly.LoadFrom(path);
                }
                catch
                {
                }
            }

            string searched = string.Join(Environment.NewLine, searchedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Take(40));
            throw new FileNotFoundException(
                "NationalInstruments.DAQmx.dll was not found or could not be loaded. " +
                "Install NI-DAQmx with .NET support, or copy both NationalInstruments.DAQmx.dll and NationalInstruments.Common.dll into " +
                @"ElectrosprayControlSystem\ThirdParty\NI and rebuild. Searched locations:" + Environment.NewLine + searched);
        }

        private static IEnumerable<string> GetCandidateAssemblyPaths(List<string> searchedPaths)
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string localPath in GetLocalCandidatePaths())
            {
                searchedPaths.Add(localPath);
                if (File.Exists(localPath) && yielded.Add(localPath))
                {
                    yield return localPath;
                }
            }

            foreach (string installedPath in GetInstalledCandidatePaths(searchedPaths))
            {
                if (yielded.Add(installedPath))
                {
                    yield return installedPath;
                }
            }
        }

        private static IEnumerable<string> GetLocalCandidatePaths()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] relativePaths =
            {
                Path.Combine(baseDir, "NationalInstruments.DAQmx.dll"),
                Path.Combine(baseDir, "ThirdParty", "NI", "NationalInstruments.DAQmx.dll"),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\ThirdParty\NI\NationalInstruments.DAQmx.dll")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\ThirdParty\NI\NationalInstruments.DAQmx.dll")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\ThirdParty\NI\NationalInstruments.DAQmx.dll"))
            };

            foreach (string path in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }

        private static IEnumerable<string> GetInstalledCandidatePaths(List<string> searchedPaths)
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "National Instruments"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "National Instruments")
            }
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            var preferredSubpaths = new[]
            {
                Path.Combine("MeasurementStudioVS2022", "DotNET", "Assemblies (64-bit)", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2022", "DotNET", "Assemblies", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2019", "DotNET", "Assemblies (64-bit)", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2019", "DotNET", "Assemblies", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2017", "DotNET", "Assemblies (64-bit)", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2017", "DotNET", "Assemblies", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2015", "DotNET", "Assemblies (64-bit)", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2015", "DotNET", "Assemblies", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2013", "DotNET", "Assemblies (64-bit)", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2013", "DotNET", "Assemblies", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2012", "DotNET", "Assemblies (64-bit)", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2012", "DotNET", "Assemblies", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2010", "DotNET", "Assemblies (64-bit)", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2010", "DotNET", "Assemblies", "Current", "NationalInstruments.DAQmx.dll"),
                Path.Combine("MeasurementStudioVS2010", "DotNET", "Assemblies", "Legacy", "NationalInstruments.DAQmx.dll")
            };

            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in roots)
            {
                foreach (string subpath in preferredSubpaths)
                {
                    string fullPath = Path.Combine(root, subpath);
                    searchedPaths.Add(fullPath);
                    if (File.Exists(fullPath) && yielded.Add(fullPath))
                    {
                        yield return fullPath;
                    }
                }
            }

            foreach (string root in roots)
            {
                IEnumerable<string> recursiveHits = Enumerable.Empty<string>();
                try
                {
                    recursiveHits = Directory.EnumerateFiles(root, "NationalInstruments.DAQmx.dll", SearchOption.AllDirectories);
                }
                catch
                {
                }

                foreach (string path in recursiveHits.OrderBy(ScoreInstalledAssemblyPath))
                {
                    searchedPaths.Add(path);
                    if (yielded.Add(path))
                    {
                        yield return path;
                    }
                }
            }
        }

        private static int ScoreInstalledAssemblyPath(string path)
        {
            string normalized = path.Replace('/', '\\');
            if (normalized.IndexOf("Assemblies (64-bit)", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (normalized.IndexOf("\\Current\\", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (normalized.IndexOf("MeasurementStudioVS2022", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (normalized.IndexOf("MeasurementStudioVS2019", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (normalized.IndexOf("MeasurementStudioVS2017", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (normalized.IndexOf("MeasurementStudioVS2015", StringComparison.OrdinalIgnoreCase) >= 0) return 5;
            if (normalized.IndexOf("MeasurementStudioVS2013", StringComparison.OrdinalIgnoreCase) >= 0) return 6;
            if (normalized.IndexOf("MeasurementStudioVS2012", StringComparison.OrdinalIgnoreCase) >= 0) return 7;
            if (normalized.IndexOf("MeasurementStudioVS2010", StringComparison.OrdinalIgnoreCase) >= 0) return 8;
            if (normalized.IndexOf("\\Legacy\\", StringComparison.OrdinalIgnoreCase) >= 0) return 9;
            return 10;
        }
    }
}
