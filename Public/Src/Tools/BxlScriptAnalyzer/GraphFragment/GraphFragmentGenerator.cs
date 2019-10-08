﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    /// <summary>
    /// Analyzer for generating graph fragment.
    /// </summary>
    public class GraphFragmentGenerator : Analyzer
    {
        private string m_outputFile;
        private string m_description;
        private readonly OptionName m_outputFileOption = new OptionName("OutputFile", "o");
        private readonly OptionName m_descriptionOption = new OptionName("Description", "d");
        private readonly OptionName m_topSortOption = new OptionName("TopSort", "t");
        private readonly OptionName m_alternateSymbolSeparatorOption = new OptionName("AlternateSymbolSeparator", "sep");
        private readonly OptionName m_outputDirectoryForEvaluationOption = new OptionName("OutputDirectoryForEvaluation");

        // Temporarily default to '_' for office mularchy builds until explicitly specified
        private char m_alternateSymbolSeparator = '_';

        private AbsolutePath m_absoluteOutputPath;

        /// <inheritdoc />
        public override AnalyzerKind Kind => AnalyzerKind.GraphFragment;

        /// <inheritdoc />
        public override EnginePhases RequiredPhases => EnginePhases.Schedule;

        /// <inheritdoc />
        public override bool HandleOption(CommandLineUtilities.Option opt)
        {
            if (m_outputFileOption.Match(opt.Name))
            {
                m_outputFile = CommandLineUtilities.ParsePathOption(opt);
                return true;
            }

            if (m_descriptionOption.Match(opt.Name))
            {
                m_description = opt.Value;
                return true;
            }

            if (m_alternateSymbolSeparatorOption.Match(opt.Name))
            {
                var alternateSymbolSeparatorString = CommandLineUtilities.ParseStringOption(opt);
                m_alternateSymbolSeparator = alternateSymbolSeparatorString.Length != 0 
                    ? alternateSymbolSeparatorString[0]
                    : default;
                return true;
            }

            if (m_topSortOption.Match(opt.Name))
            {
                SerializeUsingTopSort = CommandLineUtilities.ParseBooleanOption(opt);
                return true;
            }

            return base.HandleOption(opt);
        }

        /// <inheritdoc />
        public override void WriteHelp(HelpWriter writer)
        {
            writer.WriteOption(m_outputFileOption.LongName, "The path where the graph fragment should be generated", shortName: m_outputFileOption.ShortName);
            base.WriteHelp(writer);
        }

        /// <inheritdoc />
        public override bool Initialize()
        {
            if (string.IsNullOrEmpty(m_outputFile))
            {
                Logger.GraphFragmentMissingOutputFile(LoggingContext, m_outputFileOption.LongName);
                return false;
            }

            if (!Path.IsPathRooted(m_outputFile))
            {
                m_outputFile = Path.GetFullPath(m_outputFile);
            }

            if (!AbsolutePath.TryCreate(PathTable, m_outputFile, out m_absoluteOutputPath))
            {
                Logger.GraphFragmentInvalidOutputFile(LoggingContext, m_outputFile, m_outputFileOption.LongName);
                return false;
            }

            return base.Initialize();
        }

        /// <inheritdoc />
        public override bool AnalyzeSourceFile(Workspaces.Core.Workspace workspace, AbsolutePath path, ISourceFile sourceFile) => true;

        /// <inheritdoc />
        public override bool FinalizeAnalysis()
        {
            if (PipGraph == null)
            {
                Logger.GraphFragmentMissingGraph(LoggingContext);
                return false;
            }

            try
            {
                var serializer = new PipGraphFragmentSerializer(Context, new PipGraphFragmentContext())
                {
                    AlternateSymbolSeparator = m_alternateSymbolSeparator
                };

                var pips = PipGraph.RetrieveScheduledPips().ToList();
                if (SerializeUsingTopSort)
                {
                    var finalPipList = TopSort(pips);
                    serializer.SerializeTopSort(m_absoluteOutputPath, finalPipList, pips.Count, m_description);
                }
                else
                {
                    serializer.SerializeSerially(m_absoluteOutputPath, pips, m_description);
                }

                Logger.GraphFragmentSerializationStats(LoggingContext, serializer.FragmentDescription, serializer.Stats.ToString());
            }
            catch (Exception e)
            {
                Logger.GraphFragmentExceptionOnSerializingFragment(LoggingContext, m_absoluteOutputPath.ToString(Context.PathTable), e.ToString());
                return false;
            }

            return base.FinalizeAnalysis();
        }

        /// <summary>
        /// The pips should be in a similar order to how they were originally inserted into the graph
        /// </summary>
        private static List<List<Pip>> StableSortPips(List<Pip> pips, List<List<Pip>> finalPipList)
        {
            var order = new Dictionary<Pip, int>();
            for (int i = 0; i < pips.Count; i++)
            {
                order[pips[i]] = i;
            }

            finalPipList = finalPipList.Select(pipGroup => pipGroup.OrderBy(pip => order[pip]).ToList()).ToList();
            return finalPipList;
        }

        private List<List<Pip>> TopSort(List<Pip> pips)
        {
            var sortedPipGroups = new List<List<Pip>>();
            var modules = new List<Pip>();
            var specs = new List<Pip>();
            var values = new List<Pip>();

            // Service related are service-shutdown process pip, service finalization (IPC) pip, service-start process pip.
            var serviceRelatedPips = new List<Pip>();
            var otherPips = new List<Pip>();

            foreach (var pip in pips)
            {
                if (pip is ModulePip)
                {
                    modules.Add(pip);
                }
                else if (pip is SpecFilePip)
                {
                    specs.Add(pip);
                }
                else if (pip is ValuePip)
                {
                    values.Add(pip);
                }
                else if (ServicePipKindUtil.IsServiceStartShutdownOrFinalizationPip(pip))
                {
                    serviceRelatedPips.Add(pip);
                }
                else
                {
                    otherPips.Add(pip);
                }
            }

            sortedPipGroups.Add(modules);
            sortedPipGroups.Add(specs);
            sortedPipGroups.Add(values);
            
            // Special service related pips must go in sequential order.
            sortedPipGroups.AddRange(serviceRelatedPips.Select(pip => new List<Pip>() { pip }));

            TopSortInternal(otherPips, sortedPipGroups);
            sortedPipGroups = StableSortPips(pips, sortedPipGroups);

            return sortedPipGroups;
        }

        private void TopSortInternal(List<Pip> pips, List<List<Pip>> sortedPipGroups)
        {
            var childrenLeftToVisit = new Dictionary<Pip, int>();
            sortedPipGroups.Add(new List<Pip>());
            int totalAdded = 0;
            foreach (var pip in pips)
            {
                childrenLeftToVisit[pip] = 0;
            }

            foreach (var pip in pips)
            {
                foreach (var dependent in (PipGraph.RetrievePipImmediateDependents(pip) ?? Enumerable.Empty<Pip>()))
                {
                    childrenLeftToVisit[dependent]++;
                }
            }

            foreach (var pip in pips)
            {
                if (childrenLeftToVisit[pip] == 0)
                {
                    totalAdded++;
                    sortedPipGroups[sortedPipGroups.Count - 1].Add(pip);
                }
            }

            int currentLevel = sortedPipGroups.Count - 1;
            while (totalAdded < pips.Count)
            {
                sortedPipGroups.Add(new List<Pip>());
                foreach (var pip in sortedPipGroups[currentLevel])
                {
                    foreach (var dependent in PipGraph.RetrievePipImmediateDependents(pip) ?? Enumerable.Empty<Pip>())
                    {
                        if (--childrenLeftToVisit[dependent] == 0)
                        {
                            totalAdded++;
                            sortedPipGroups[currentLevel + 1].Add(dependent);
                        }
                    }
                }

                currentLevel++;
            }
        }

        private struct OptionName
        {
            public readonly string LongName;
            public readonly string ShortName;

            public OptionName(string name)
            {
                LongName = name;
                ShortName = name;
            }

            public OptionName(string longName, string shortName)
            {
                LongName = longName;
                ShortName = shortName;
            }

            public bool Match(string option) =>
                string.Equals(option, LongName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option, ShortName, StringComparison.OrdinalIgnoreCase);
        }
    }
}