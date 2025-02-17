﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="AssemblyWeaver.cs" company="Catel development team">
//   Copyright (c) 2008 - 2013 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Catel.Reflection;
using LoadAssembliesOnStartup.Fody;
using LoadAssembliesOnStartup.Fody.TestAssembly;
using LoadAssembliesOnStartup.Fody.Tests;
using Mono.Cecil;
using Fody;

public class AssemblyWeaver
{
    #region Constants
    private readonly Dictionary<string, TestResult> _assemblies = new Dictionary<string, TestResult>();
    #endregion

    #region Constructors
    static AssemblyWeaver()
    {
        Instance = new AssemblyWeaver();
    }

    public AssemblyWeaver(List<string> referenceAssemblyPaths = null)
    {
        if (referenceAssemblyPaths is null)
        {
            referenceAssemblyPaths = new List<string>();
        }
    }

    public TestResult GetAssembly(string testCaseName, string configString)
    {
        if (!_assemblies.ContainsKey(configString))
        {
            var type = typeof(DummyDependencyInjectionClass);

            var assemblyPath = type.GetAssemblyEx().Location;

            var rootDirectory = Directory.GetParent(assemblyPath).FullName;
            var testCaseDirectory = Path.Combine(rootDirectory, testCaseName);
            Directory.CreateDirectory(testCaseDirectory);

            var assemblyResolver = new TestAssemblyResolver();
            //assemblyResolver.AddSearchDirectory(testCaseDirectory);
            //assemblyResolver.AddSearchDirectory(rootDirectory);

            var metadataResolver = new MetadataResolver(assemblyResolver);

            var readerParameters = new ReaderParameters
            {
                AssemblyResolver = assemblyResolver,
                MetadataResolver = metadataResolver,
                ReadSymbols = false,
            };

            using (var moduleDefinition = ModuleDefinition.ReadModule(assemblyPath, readerParameters))
            {
                // Important note: the test project can be on a completely different location (especially on a build agent)
                var projectName = Path.GetFileName(assemblyPath).Replace(".dll", string.Empty);

                // Check 1: fixed location from test
                var relativeCsProjectFilePath = Path.Combine(Directory.GetParent(assemblyPath).FullName, "..", "..", "..", "..", "src", projectName, $"{projectName}.csproj");
                var csProjectFilePath = Path.GetFullPath(relativeCsProjectFilePath);

                if (!File.Exists(csProjectFilePath))
                {
                    // Check 2: Check 2 directories up, then search for the project file (happens on a build agent)
                    var searchDirectory = Path.Combine(Directory.GetParent(assemblyPath).FullName, "..", "..", "..", "Source");
                    var searchFileName = $"{projectName}.csproj";
                    csProjectFilePath = Directory.GetFiles(searchDirectory, searchFileName, SearchOption.AllDirectories).FirstOrDefault();
                }

                if (!File.Exists(csProjectFilePath))
                {
                    throw new System.Exception($"Project file '{csProjectFilePath}' does not exist, make sure to check the paths since this is required for the unit tests");
                }

                var references = new string[]
                {
                    //"Catel.Core.dll",
                    //"Orc.FileSystem.dll"
                };

                var weavingTask = new ModuleWeaver
                {
                    Config = XElement.Parse(configString),
                    AssemblyResolver = assemblyResolver,
                    ProjectFilePath = csProjectFilePath,
                    ModuleDefinition = moduleDefinition,
                    References = string.Join(";", references.Select(r => Path.Combine(rootDirectory, r))),
                    ReferenceCopyLocalPaths = references.Select(r => Path.Combine(rootDirectory, r)).ToList(),
                };

                var testResult = weavingTask.ExecuteTestRun(assemblyPath,
                    assemblyName: testCaseName,
                    ignoreCodes: new[] { "0x80131869" },
                    runPeVerify: false);

                if (testResult.Errors.Count > 0)
                {
                    throw new System.Exception("Received errors while weaving");
                }

                _assemblies[configString] = testResult;
            }
        }

        return _assemblies[configString];
    }
    #endregion

    public static AssemblyWeaver Instance { get; private set; }
}
