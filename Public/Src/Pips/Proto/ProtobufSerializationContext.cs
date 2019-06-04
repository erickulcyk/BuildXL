// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Google.Protobuf;
using Google.Protobuf.Collections;
using AbsolutePathProto = BuildXL.Proto.AbsolutePath;
using DirectoryArtifactProto = BuildXL.Proto.DirectoryArtifact;
using FileArtifactProto = BuildXL.Proto.FileArtifact;

namespace BuildXL.Pips
{
    /// <summary>
    /// Class that tracks deduplicating and simplifying path usages
    /// </summary>
    public class ProtobufSerializationContext
    {
        private enum Mode
        {
            Uninitialized,
            Read,
            Write,
        }

        private Mode m_mode;
        public PathTable PathTable { get; }

        private readonly Dictionary<int, int> m_pathIdToProtoIdMap = new Dictionary<int, int>();
        private readonly Dictionary<int, AbsolutePath> m_protoIdIdToPathMap = new Dictionary<int, AbsolutePath>();
        private BuildXL.Proto.PathTable  m_pathTableProto = new BuildXL.Proto.PathTable();

        public ProtobufSerializationContext(PathTable pathTable)
        {
            PathTable = pathTable;

            // Always add empty path in the list to represent invalid path
            m_pathTableProto.NextLocalPathId = 1;
            m_mode = Mode.Write;
        }

        #region send/receive management

        public void ReceivePathTable(BuildXL.Proto.PathTable protoPathTable)
        {
            m_mode = Mode.Read;

            // TODO: Make this lazy
            foreach (var entry in protoPathTable.Paths)
            {
                if (entry.Id == 0)
                {
                    // Skip already 
                }
                else if (entry.Parent == 0)
                {
                    // Special case, create a full path
                    var rootPath = entry.Name;
                    if (!OperatingSystemHelper.IsUnixOS && rootPath.LastOrDefault() != Path.DirectorySeparatorChar)
                    {
                        rootPath += Path.DirectorySeparatorChar;
                    }

                    var path = AbsolutePath.Create(PathTable, rootPath);
                    m_protoIdIdToPathMap.Add(entry.Id, path);
                    m_pathIdToProtoIdMap.Add(path.Value.Value, entry.Id);
                }
                else
                {
                    // If we have a proper parent create it the fast way.
                    var parent = PathFromProto(entry.Parent);
                    var path = parent.Combine(PathTable, entry.Name);
                    m_protoIdIdToPathMap.Add(entry.Id, path);
                    m_pathIdToProtoIdMap.Add(path.Value.Value, entry.Id);
                }

                m_pathTableProto.NextLocalPathId++;
            }

            ResetPathTable();
        }

        public void PreparePathTableForWrite()
        {
            this.ResetPathTable();
            m_mode = Mode.Write;
        }

        public BuildXL.Proto.PathTable SendPathTable()
        {
            var result = m_pathTableProto;
            ResetPathTable();
            m_mode = Mode.Uninitialized;
            return result;
        }

        private void ResetPathTable()
        {
            var nextLocalPathId = m_pathTableProto.NextLocalPathId;
            m_pathTableProto = new BuildXL.Proto.PathTable();
            m_pathTableProto.NextLocalPathId = nextLocalPathId;
        }
        #endregion

        public string ToProto(StringId str)
        {
            return str.IsValid 
                ? str.ToString(PathTable.StringTable)
                : string.Empty;
        }

        public StringId FromProto(string str)
        {
            return string.IsNullOrEmpty(str)
                ? StringId.Invalid
                : StringId.Create(PathTable.StringTable, str);
        }

        /// <summary>
        /// Converts a BuildXL absolute path to a protobuf version of AbsolutePath
        /// </summary>
        /// <remarks>
        /// Derived types may include PathTable semantics and allow for dedupping or progressive deduping.
        /// </remarks>
        public int ToProto(AbsolutePath path)
        {
            return AddPath(path);
        }

        public AbsolutePath PathFromProto(int protoId)
        {
            Contract.Requires(m_mode == Mode.Read);
            if (protoId == 0)
            {
                return AbsolutePath.Invalid;
            }

            if (m_protoIdIdToPathMap.TryGetValue(protoId, out var path))
            {
                return path;
            }

            throw Contract.AssertFailure("No matching id");
        }

        public int AddPath(AbsolutePath path)
        {
            return AddPath(path, path.Value.Value);
        }

        private int AddPath(AbsolutePath path, int pathId)
        {
            Contract.Requires(m_mode == Mode.Write);
            int localId = 0;
            if (!path.IsValid)
            {
                return localId;
            }

            if (!m_pathIdToProtoIdMap.TryGetValue(pathId, out localId))
            {
                var localParentId = AddPath(path.GetParent(PathTable));
                localId = m_pathTableProto.NextLocalPathId++; 
                m_pathIdToProtoIdMap.Add(pathId, localId);
                m_protoIdIdToPathMap.Add(localId, path);
                m_pathTableProto.Paths.Add(
                    new AbsolutePathProto()
                    {
                        Id = localId,
                        Parent = localParentId,
                        Name = path.GetName(PathTable).ToString(PathTable.StringTable),
                    });

            }

            return localId;
        }

        public FileArtifactProto ToProto(FileArtifact f)
        {
            if (!f.IsValid)
            {
                return new FileArtifactProto();
            }

            return new FileArtifactProto
                   {
                       PathId = AddPath(f.Path),
                       RewriteCount = f.RewriteCount
                   };
        }

        public FileArtifact FromProto(FileArtifactProto f)
        {
            if (f.PathId == 0)
            {
                return FileArtifact.Invalid;
            }

            return new FileArtifact(PathFromProto(f.PathId), f.RewriteCount);
        }

        public BuildXL.Proto.FileArtifactWithAttributes ToProto(FileArtifactWithAttributes f)
        {
            return new BuildXL.Proto.FileArtifactWithAttributes
                   {
                       PathId = ToProto(f.Path),
                       FileExistence = (BuildXL.Proto.FileExistance)(int)f.FileExistence,
                       RewriteCount = f.RewriteCount
                   };
        }

        public FileArtifactWithAttributes FromProto(BuildXL.Proto.FileArtifactWithAttributes f)
        {
            return FileArtifactWithAttributes.Create(
                new FileArtifact(PathFromProto(f.PathId), f.RewriteCount), 
                (FileExistence)(int)f.FileExistence);
        }

        public DirectoryArtifactProto ToProto(DirectoryArtifact d)
        {
            return new DirectoryArtifactProto
                   {
                       PathId = AddPath(d.Path),
                       PartialSealId = d.PartialSealId,
                       IsShared = d.IsSharedOpaque,
                   };
        }

        public DirectoryArtifact FromProto(DirectoryArtifactProto d)
        {
            return new DirectoryArtifact(PathFromProto(d.PathId), d.PartialSealId, d.IsShared);
        }

        public long ToProto(TimeSpan? warningTimeout)
        {
            return warningTimeout?.Ticks ?? 0;
        }

        public TimeSpan? TimeSpanFromProto(long ticks)
        {
            if (ticks == 0)
            {
                return null;
            }

            return TimeSpan.FromTicks(ticks);
        }

        /// <summary>
        /// Converts IEnumerable to Protobuf Repeated field
        /// </summary>
        public RepeatedField<TProto> ToProto<TFrom, TProto>(ReadOnlyArray<TFrom> dependencies, Func<TFrom, TProto> convert)
        {
            var result = new RepeatedField<TProto>();
            result.AddRange(dependencies.Select(convert));
            return result;
        }

        /// <summary>
        /// Converts IEnumerable to Protobuf Repeated field
        /// </summary>
        public RepeatedField<TValue> ToProto<TValue>(ReadOnlyArray<TValue> items)
        {
            var result = new RepeatedField<TValue>();
            result.AddRange(items);
            return result;
        }


        /// <summary>
        /// Converts IEnumerable to Protobuf Repeated field
        /// </summary>
        public ReadOnlyArray<TResult> FromProto<TProto, TResult>(RepeatedField<TProto> items, Func<TProto, TResult> convert)
        {
            var result = new TResult[items.Count];
            int i = 0;
            foreach (var item in items)
            {
                result[i] = convert(item);
                i++;
            }

            return ReadOnlyArray<TResult>.FromWithoutCopy(result);
        }


        /// <summary>
        /// Converts IEnumerable to Protobuf Repeated field
        /// </summary>
        public ReadOnlyArray<TValue> FromProto<TValue>(RepeatedField<TValue> items)
        {
            var result = new TValue[items.Count];
            int i = 0;
            foreach (var item in items)
            {
                result[i] = item;
                i++;
            }

            return ReadOnlyArray<TValue>.FromWithoutCopy(result);
        }
    }
}
