using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ElectrosprayControlSystem.Services
{
    internal static class DinoLiteSdkLocator
    {
        private static readonly string[] CandidateFileNames =
        {
            "DNX64.dll",
            "DNX64d.dll"
        };

        public static string ResolveDllPath()
        {
            return GetSearchCandidates().FirstOrDefault(File.Exists);
        }

        public static IReadOnlyList<string> GetSearchCandidates()
        {
            string appBase = AppDomain.CurrentDomain.BaseDirectory;
            string[] baseFolders =
            {
                Path.Combine(appBase, "ThirdParty", "DinoLite"),
                Path.GetFullPath(Path.Combine(appBase, "..", "..", "..", "ThirdParty", "DinoLite"))
            };

            return baseFolders
                .SelectMany(folder => CandidateFileNames.Select(file => Path.Combine(folder, file)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
