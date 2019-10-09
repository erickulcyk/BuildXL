// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Pips;
using Newtonsoft.Json.Linq;
using static BuildXL.Scheduler.Tracing.FingerprintStoreReader;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Cache miss analysis methods used by both on-the-fly and execution log analyzer
    /// </summary>
    public static class CacheMissAnalysisUtilities
    {
        /// <summary>
        /// Analyzes the cache miss for a specific pip.
        /// </summary>
        public static (CacheMissAnalysisResult, IEnumerable<(string,string)>) AnalyzeCacheMiss(
            TextWriter writer,
            PipCacheMissInfo missInfo,
            Func<PipRecordingSession> oldSessionFunc,
            Func<PipRecordingSession> newSessionFunc)
        {
            Contract.Requires(oldSessionFunc != null);
            Contract.Requires(newSessionFunc != null);

            WriteLine($"Cache miss type: {missInfo.CacheMissType}", writer);
            WriteLine(string.Empty, writer);

            switch (missInfo.CacheMissType)
            {
                // Fingerprint miss
                case PipCacheMissType.MissForDescriptorsDueToWeakFingerprints:
                case PipCacheMissType.MissForDescriptorsDueToStrongFingerprints:
                    // Compute the pip unique output hash to use as the primary lookup key for fingerprint store entries
                    return AnalyzeFingerprints(oldSessionFunc, newSessionFunc, writer);

                // We had a weak and strong fingerprint match, but couldn't retrieve correct data from the cache
                case PipCacheMissType.MissForCacheEntry:
                case PipCacheMissType.MissForProcessMetadata:
                case PipCacheMissType.MissForProcessMetadataFromHistoricMetadata:
                case PipCacheMissType.MissForProcessOutputContent:
                    WriteLine($"Data missing from the cache.", writer);
                    return (CacheMissAnalysisResult.DataMiss, null);

                case PipCacheMissType.MissDueToInvalidDescriptors:
                    WriteLine($"Cache returned invalid data.", writer);
                    return (CacheMissAnalysisResult.InvalidDescriptors, null);

                case PipCacheMissType.MissForDescriptorsDueToArtificialMissOptions:
                    WriteLine($"Cache miss artificially forced by user.", writer);
                    return (CacheMissAnalysisResult.ArtificialMiss, null);

                case PipCacheMissType.Invalid:
                    WriteLine($"Unexpected condition! No valid changes or cache issues were detected to cause process execution, but a process still executed.", writer);
                    return (CacheMissAnalysisResult.Invalid, null);

                case PipCacheMissType.Hit:
                    WriteLine($"Pip was a cache hit.", writer);
                    return (CacheMissAnalysisResult.NoMiss, null);

                default:
                    WriteLine($"Unexpected condition! Unknown cache miss type.", writer);
                    return (CacheMissAnalysisResult.Invalid, null);
            }
        }

        private static (CacheMissAnalysisResult, IEnumerable<(string, string)>) AnalyzeFingerprints(
            Func<PipRecordingSession> oldSessionFunc,
            Func<PipRecordingSession> newSessionFunc,
            TextWriter writer)
        {
            var result = CacheMissAnalysisResult.Invalid;
            IEnumerable<(string, string)> cacheMissSummary = null;

            // While a PipRecordingSession is in scope, any pip information retrieved from the fingerprint store is
            // automatically written out to per-pip files.
            using (var oldPipSession = oldSessionFunc())
            using (var newPipSession = newSessionFunc())
            {
                bool missingPipEntry = false;
                if (!oldPipSession.EntryExists)
                {
                    WriteLine("No fingerprint computation data found from old build.", writer, oldPipSession.PipWriter);
                    WriteLine("This may be the first execution where pip outputs were stored to the cache.", writer, oldPipSession.PipWriter);

                    // Write to just the old pip file
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, oldPipSession.PipWriter);
                    missingPipEntry = true;
                    result = CacheMissAnalysisResult.MissingFromOldBuild;

                    WriteLine(string.Empty, writer, oldPipSession.PipWriter);
                }

                if (!newPipSession.EntryExists)
                {
                    // Cases:
                    // 1. ScheduleProcessNotStoredToCacheDueToFileMonitoringViolations
                    // 2. ScheduleProcessNotStoredDueToMissingOutputs
                    // 3. ScheduleProcessNotStoredToWarningsUnderWarnAsError
                    // 4. ScheduleProcessNotStoredToCacheDueToInherentUncacheability
                    WriteLine("No fingerprint computation data found from new build.", writer, newPipSession.PipWriter);

                    // Write to just the new pip file
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, newPipSession.PipWriter);
                    missingPipEntry = true;
                    result = CacheMissAnalysisResult.MissingFromNewBuild;

                    WriteLine(string.Empty, writer, newPipSession.PipWriter);
                }

                if (missingPipEntry)
                {
                    // Only write once to the analysis file
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, writer);

                    // Nothing to compare if an entry is missing
                    return (result, cacheMissSummary);
                }

                if (oldPipSession.FormattedSemiStableHash != newPipSession.FormattedSemiStableHash)
                {
                    // Make trivial json so the print looks like the rest of the diff
                    var oldNode = new JsonNode
                    {
                        Name = RepeatedStrings.FormattedSemiStableHashChanged
                    };
                    oldNode.Values.Add(oldPipSession.FormattedSemiStableHash);

                    var newNode = new JsonNode
                    {
                        Name = RepeatedStrings.FormattedSemiStableHashChanged
                    };
                    newNode.Values.Add(newPipSession.FormattedSemiStableHash);

                    var jsonDiff = JsonTree.GetTreeDiff(oldNode, newNode);
                    // cacheMissSummary = GetCacheSummary(jsonDiff);
                    WriteLine(JsonTree.PrintTreeDiff(jsonDiff), writer);
                }

                // Diff based off the actual fingerprints instead of the PipCacheMissType
                // to avoid shared cache diff confusion.
                //
                // In the following shared cache scenario:
                // Local cache: WeakFingerprint miss
                // Remote cache: WeakFingerprint hit, StrongFingerprint miss
                //
                // The pip cache miss type will be a strong fingerprint miss,
                // but the data in the fingerprint store will not match the 
                // remote cache's, so we diff based off what we have in the fingerprint store.
                if (oldPipSession.WeakFingerprint != newPipSession.WeakFingerprint)
                {
                    WriteLine("WeakFingerprint", writer);
                    var jsonDiff = JsonTree.GetTreeDiff(oldPipSession.GetWeakFingerprintTree(), newPipSession.GetWeakFingerprintTree());
                    JObject jobj = jsonDiff.ToObject<JObject>();
                    cacheMissSummary = GetCacheSummary(jobj);
                    WriteLine(JsonTree.PrintTreeDiff(jsonDiff), writer);
                    result = CacheMissAnalysisResult.WeakFingerprintMismatch;
                }
                else if (oldPipSession.StrongFingerprint != newPipSession.StrongFingerprint)
                {
                    WriteLine("StrongFingerprint", writer);
                    var jsonDiff = JsonTree.GetTreeDiff(oldPipSession.GetStrongFingerprintTree(), newPipSession.GetStrongFingerprintTree());
                    JObject jobj = jsonDiff.ToObject<JObject>();
                    cacheMissSummary = GetCacheSummary(jobj);
                    WriteLine(JsonTree.PrintTreeDiff(jsonDiff), writer);
                    result = CacheMissAnalysisResult.StrongFingerprintMismatch;
                }
                else
                {
                    WriteLine("The fingerprints from both builds matched and no cache retrieval errors occurred.", writer);
                    WriteLine(RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching, writer, oldPipSession.PipWriter, newPipSession.PipWriter);
                    result = CacheMissAnalysisResult.UncacheablePip;
                }
            }

            return (result, cacheMissSummary);
        }

        private static HashSet<string> FingerprintPossibleCacheMissArrayReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Dependencies",
            "DirectoryDependencies",
            "DirectoryOutputs",
            "Outputs",
            "EnvironmentVariables",
            "PathSet",
            "UntrackedPaths",
            "UntrackedScopes"
        };

        private static HashSet<string> FingerprintPossibleCacheMissReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Arguments",
            "Executable",
            "ExecutionAndFingerprintOptionsHash",
            "ResponseFileData",
            "StandardInputData",
            "WorkingDirectory",
        };

        private static IEnumerable<(string, string)> GetCacheSummary(JObject o)
        {
            foreach (var changedProperty in o)
            {
                string changedPropertyName = changedProperty.Key;
                JObject value = changedProperty.Value as JObject;
                if (changedPropertyName == "PathSet" && (value.ContainsKey("0") || value.ContainsKey("_0")))
                {
                    yield return ("Path Set Change", "Path Set Change");
                    break;
                }
                else if (FingerprintPossibleCacheMissArrayReasons.Contains(changedPropertyName))
                {
                    foreach (var arrayValue in TryParseArray(changedProperty.Value, true))
                    {
                        string arrayValueName = GetTokenName(arrayValue);
                        string changedItemCategory = changedPropertyName;
                        var children = arrayValue.Children();
                        if (children.Any() && children.First() is JObject)
                        {
                            var categoryJson = children.First();
                            string potentialCatetgoryName = GetTokenName(categoryJson);

                            // This happens where there was more than one change to the same file for the same pip
                            // Common case would be directory enumeration, where it will report both directory enumeration and which members changed.
                            bool childIsArray = potentialCatetgoryName == "_t";
                            if (childIsArray)
                            {
                                categoryJson = TryParseArray(categoryJson, false).First();
                                potentialCatetgoryName = GetTokenName(categoryJson);
                            }

                            if (potentialCatetgoryName != null && potentialCatetgoryName == "ObservedInputs")
                            {
                                potentialCatetgoryName = ParseObservedInputCategory(categoryJson) ?? potentialCatetgoryName;
                            }

                            if (potentialCatetgoryName != null && potentialCatetgoryName != arrayValueName)
                            {
                                changedItemCategory = potentialCatetgoryName;
                            }
                        }

                        /*if (copyFiles != null)
                        {
                            while (arrayValueName != null && copyFiles.ContainsKey(arrayValueName) && copyFiles[arrayValueName] != null)
                            {
                                changedItemCategory = "CopyFile";
                                arrayValueName = copyFiles[arrayValueName];
                            }
                        }*/

                        yield return (arrayValueName, changedItemCategory);
                    }
                }
                else if (FingerprintPossibleCacheMissReasons.Contains(changedPropertyName))
                {
                    yield return (changedPropertyName, changedPropertyName);
                }
                else
                {
                    Console.Error.WriteLine("Unknown fingerprint json: \n" + changedPropertyName + "\n" + value);
                }
            }
        }

        private static IEnumerable<JToken> TryParseArray(JToken value, bool shouldBePath)
        {
            return value is JArray ? TryParseArray(value as JArray) : TryParseArray(value as JObject, shouldBePath);
        }

        private static IEnumerable<JToken> TryParseArray(JArray value)
        {
            return value.Children();
        }

        private static IEnumerable<JToken> TryParseArray(JObject value, bool shouldBePath)
        {
            Dictionary<string, JToken> arrayValuesChanged = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in value)
            {
                int number;
                if (int.TryParse(kvp.Key, out number) || int.TryParse(kvp.Key.Substring(1), out number))
                {
                    foreach (var child in kvp.Value.Children())
                    {
                        if (child is JProperty || child is JObject || child is JValue)
                        {
                            string tokenName = GetTokenName(child);
                            if (arrayValuesChanged.ContainsKey(tokenName))
                            {
                                arrayValuesChanged.Remove(tokenName);
                            }
                            else
                            {
                                arrayValuesChanged[tokenName] = child;
                            }
                        }
                        else
                        {
                            Console.WriteLine("Unrecognized array object: " + kvp.Key + "\n" + kvp.Value);
                        }
                    }
                }
                else if (kvp.Key != "_t")
                {
                    Console.WriteLine("Unreconized object: " + kvp.Key);
                }
            }

            return arrayValuesChanged.Values;
        }

        private static string GetTokenName(JToken token)
        {
            var jprop = token as JProperty;
            if (jprop != null)
            {
                return jprop.Name;
            }

            var jobj = token as JObject;
            if (jobj != null)
            {
                return jobj.Children<JProperty>().First().Name;
            }

            var jValue = token as JValue;
            if (jValue != null)
            {
                return jValue.Value.ToString();
            }

            var jArray = token as JArray;
            if (jArray != null && jArray.Count > 0)
            {
                return GetTokenName(jArray.First());
            }

            Console.WriteLine("Unrecognized token: " + token);
            return null;
        }

        private static string ParseObservedInputCategory(JToken categoryJson)
        {
            if (categoryJson is JObject)
            {
                categoryJson = categoryJson.First();
            }

            if (categoryJson is JProperty)
            {
                categoryJson = (categoryJson as JProperty).Value;
                if (categoryJson is JArray)
                {
                    categoryJson = categoryJson.Children().First();
                    return GetTokenName(categoryJson).Split(':')[0];
                }
                else if (categoryJson is JValue)
                {
                    return (categoryJson as JValue).Value.ToString().Split(':')[0];
                }
                else
                {
                    Console.WriteLine("Unknown ObservedInputs category json: " + categoryJson);
                }
            }
            else
            {
                Console.WriteLine("Unknown category json: " + categoryJson);
            }

            return null;
        }

        private static void WriteLine(string message, TextWriter writer, params TextWriter[] additionalWriters)
        {
            writer?.WriteLine(message);
            foreach (var w in additionalWriters)
            {
                w?.WriteLine(message);
            }
        }


        /// <summary>
        /// Any strings that need to be repeated.
        /// </summary>
        public readonly struct RepeatedStrings
        {
            /// <summary>
            /// Disallowed file accesses prevent caching.
            /// </summary>
            public const string DisallowedFileAccessesOrPipFailuresPreventCaching
                = "Settings that permit disallowed file accesses or pip failure can prevent pip outputs from being stored in the cache.";

            /// <summary>
            /// Missing directory membership fingerprint.
            /// </summary>
            public const string MissingDirectoryMembershipFingerprint
                = "Directory membership fingerprint entry missing from store.";

            /// <summary>
            /// Formatted semi stable hash changed.
            /// </summary>
            public const string FormattedSemiStableHashChanged
                = "FormattedSemiStableHash";
        }
    }
}
