// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips
{
    /// <summary>
    /// Mutable pip state
    /// </summary>
    /// <remarks>
    /// While a <code>Pip</code> is strictly immutable, all mutable information associated with a Pip goes here.
    /// (This class also holds some immutable information that is often needed to identify a Pip,
    /// in particular <code>NodeIdValue</code>, <code>SemiStableHash</code>.)
    /// </remarks>
    internal class MutablePipState
    {
        /// <summary>
        /// Identifier of this pip that is stable across BuildXL runs with an identical schedule
        /// </summary>
        /// <remarks>
        /// This identifier is not necessarily unique, but should be quite unique in practice.
        /// This property is equivalent to <see cref="Pip.SemiStableHash"/> for the pip represented, and therefore immutable.
        /// </remarks>
        internal long SemiStableHash { get; }

        /// <summary>
        /// Associated PageableStoreId for the mutable. Used to retrieve the corresponding Pip
        /// </summary>
        internal PageableStoreId StoreId;

        private WeakReference<Pip> m_weakPip;

        internal readonly PipType PipType;

        internal readonly int Priority;

        /// <summary>
        /// /// Constructor used for deserialization
        /// </summary>
        protected MutablePipState(PipType piptype, long semiStableHash, PageableStoreId storeId, int priority)
        {
            PipType = piptype;
            SemiStableHash = semiStableHash;
            StoreId = storeId;
            Priority = priority;
        }

        /// <summary>
        /// Creates a new mutable from a live pip
        /// </summary>
        public static MutablePipState Create(Pip pip)
        {
            Contract.Requires(pip != null);
            Contract.Assert(PipState.Ignored == 0);

            MutablePipState mutable;

            switch (pip.PipType)
            {
                case PipType.Ipc:
                    var pipAsIpc = (IpcPip)pip;
                    var serviceKind =
                        pipAsIpc.IsServiceFinalization ? ServicePipKind.ServiceFinalization :
                        pipAsIpc.ServicePipDependencies.Any() ? ServicePipKind.ServiceClient :
                        ServicePipKind.None;
                    var serviceInfo = new ServiceInfo(serviceKind, pipAsIpc.ServicePipDependencies);
                    mutable = new ProcessMutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId), serviceInfo, Process.Options.IsLight, Process.MinPriority);
                    break;
                case PipType.Process:
                    var pipAsProcess = (Process)pip;
                    mutable = new ProcessMutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId), pipAsProcess.ServiceInfo, pipAsProcess.ProcessOptions, pipAsProcess.Priority);
                    break;
                case PipType.CopyFile:
                    var pipAsCopy = (CopyFile)pip;
                    mutable = new CopyMutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId), pipAsCopy.OutputsMustRemainWritable, Process.MinPriority);
                    break;
                case PipType.SealDirectory:
                    SealDirectoryKind sealDirectoryKind = default;
                    bool scrub = false;
                    var seal = (SealDirectory)pip;
                    if (seal != null)
                    {
                        sealDirectoryKind = seal.Kind;
                        scrub = seal.Scrub;
                    }

                    mutable = new SealDirectoryMutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId), sealDirectoryKind, seal.Patterns, seal.IsComposite, scrub, Process.MinPriority);
                    break;
                default:
                    mutable = new MutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId), Process.MinPriority);
                    break;
            }

            mutable.m_weakPip = new WeakReference<Pip>(pip);
            return mutable;
        }

        /// <summary>
        /// Serializes
        /// </summary>
        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write((byte)PipType);
            writer.Write(SemiStableHash);
            writer.Write(Priority);
            StoreId.Serialize(writer);
            SpecializedSerialize(writer);
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        internal static MutablePipState Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var pipType = (PipType)reader.ReadByte();
            var semiStableHash = reader.ReadInt64();
            var priority = reader.ReadInt32();
            var storeId = PageableStoreId.Deserialize(reader);

            switch (pipType)
            {
                case PipType.Ipc:
                case PipType.Process:
                    return ProcessMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId, priority);
                case PipType.CopyFile:
                    return CopyMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId, priority);
                case PipType.SealDirectory:
                    return SealDirectoryMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId, priority);
                default:
                    return new MutablePipState(pipType, semiStableHash, storeId, priority);
            }
        }

        /// <summary>
        /// Implemented by derived classes that need custom deserialization
        /// </summary>
        protected virtual void SpecializedSerialize(BuildXLWriter writer)
        {
        }

        /// <summary>
        /// Checks if pip outputs must remain writable.
        /// </summary>
        /// <returns></returns>
        public virtual bool MustOutputsRemainWritable() => false;

        /// <summary>
        /// Checks if pip outputs must be preserved.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsPreservedOutputsPip() => false;

        internal bool IsAlive
        {
            get
            {
                if (m_weakPip == null)
                {
                    return false;
                }

                Pip pip;
                return m_weakPip.TryGetTarget(out pip);
            }
        }

        internal Pip InternalGetOrSetPip(PipTable table, PipId pipId, PipQueryContext context, Func<PipTable, PipId, PageableStoreId, PipQueryContext, Pip> creator)
        {
            Contract.Ensures(Contract.Result<Pip>() != null);

            lock (this)
            {
                Pip pip;
                if (m_weakPip == null)
                {
                    pip = creator(table, pipId, StoreId, context);
                    m_weakPip = new WeakReference<Pip>(pip);
                }
                else if (!m_weakPip.TryGetTarget(out pip))
                {
                    m_weakPip.SetTarget(pip = creator(table, pipId, StoreId, context));
                }

                return pip;
            }
        }
    }

    /// <summary>
    /// Mutable pip state for Process pips.
    /// </summary>
    internal sealed class ProcessMutablePipState : MutablePipState
    {
        internal readonly ServiceInfo ServiceInfo;
        internal readonly Process.Options ProcessOptions;

        internal ProcessMutablePipState(
            PipType pipType, 
            long semiStableHash, 
            PageableStoreId storeId, 
            ServiceInfo serviceInfo, 
            Process.Options processOptions,
            int priority)
            : base(pipType, semiStableHash, storeId, priority)
        {
            ServiceInfo = serviceInfo;
            ProcessOptions = processOptions;
        }

        /// <summary>
        /// Shortcut for whether this is a start or shutdown pip
        /// </summary>
        internal bool IsStartOrShutdown
        {
            get
            {
                return ServiceInfo != null && ServiceInfo.Kind.IsStartOrShutdown();
            }
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write(ServiceInfo, ServiceInfo.InternalSerialize);
            writer.Write((int)ProcessOptions);
        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId, int priority)
        {
            ServiceInfo serviceInfo = reader.ReadNullable(ServiceInfo.InternalDeserialize);
            int options = reader.ReadInt32();

            return new ProcessMutablePipState(pipType, semiStableHash, storeId, serviceInfo, (Process.Options)options, priority);
        }

        public override bool IsPreservedOutputsPip() => (ProcessOptions & Process.Options.AllowPreserveOutputs) != 0;

        public override bool MustOutputsRemainWritable() => (ProcessOptions & Process.Options.OutputsMustRemainWritable) != 0;
    }

    internal sealed class CopyMutablePipState : MutablePipState
    {
        private readonly bool m_keepOutputWritable;

        internal CopyMutablePipState(
            PipType pipType,
            long semiStableHash,
            PageableStoreId storeId,
            bool keepOutputWritable,
            int priority)
            : base(pipType, semiStableHash, storeId, priority)
        {
            m_keepOutputWritable = keepOutputWritable;
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write(m_keepOutputWritable);
        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId, int priority)
        {
            bool keepOutputWritable = reader.ReadBoolean();

            return new CopyMutablePipState(pipType, semiStableHash, storeId, keepOutputWritable, priority);
        }

        public override bool MustOutputsRemainWritable() => m_keepOutputWritable;
    }

    /// <summary>
    /// Mutable pip state for SealDirectory pips.
    /// </summary>
    internal sealed class SealDirectoryMutablePipState : MutablePipState
    {
        internal readonly SealDirectoryKind SealDirectoryKind;
        internal readonly ReadOnlyArray<StringId> Patterns;
        internal readonly bool IsComposite;
        internal readonly bool Scrub;

        public SealDirectoryMutablePipState(PipType piptype, long semiStableHash, PageableStoreId storeId, SealDirectoryKind sealDirectoryKind, ReadOnlyArray<StringId> patterns, bool isComposite, bool scrub, int priority)
            : base(piptype, semiStableHash, storeId, priority)
        {
            SealDirectoryKind = sealDirectoryKind;
            Patterns = patterns;
            IsComposite = isComposite;
            Scrub = scrub;
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write((byte)SealDirectoryKind);
            writer.Write(Patterns, (w, v) => w.Write(v));
            writer.Write(IsComposite);
            writer.Write(Scrub);
        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId, int priority)
        {
            var sealDirectoryKind = (SealDirectoryKind)reader.ReadByte();
            var patterns = reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId());
            var isComposite = reader.ReadBoolean();
            var scrub = reader.ReadBoolean();

            return new SealDirectoryMutablePipState(pipType, semiStableHash, storeId, sealDirectoryKind, patterns, isComposite, scrub, priority);
        }
    }
}
