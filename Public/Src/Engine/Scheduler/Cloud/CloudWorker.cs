// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if FEATURE_CORECLR

// HACK SOMEHOW THIS GetS BUILD FOR CORECLR Regardless of qualifier
// so have to place a dummy class here to satify that build.
using BuildXL.Native.IO;
using BuildXL.Scheduler.Artifacts;
using System.Collections.Generic;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Worker that schedules the work to a cloud based endpoint
    /// </summary>
    public class CloudWorker : Worker
    {
        public CloudWorker(uint workerId, string name, IReadOnlyList<string> endPoint, FileContentManager fileContentManager, ITempDirectoryCleaner tempCleaner)
            : base(workerId, name)
        {
        }
    }
}
#else
using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cloud.Proto;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Cloud;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Google.Protobuf;
using Grpc.Core;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Worker that schedules the work to a cloud based endpoint
    /// </summary>
    public class CloudWorker : Worker
    {
        private ContentServer.ContentServerClient m_casClient;
        private RemoteAgentService.RemoteAgentServiceClient m_remoteClient;

        /// <nodoc />
        public CloudWorker(
            uint workerId,
            string name,
            IReadOnlyList<string> endPoints,
            FileContentManager fileContentManager,
            ITempDirectoryCleaner tempCleaner)
            : base(workerId, name)
        {

            // TODO: Round-robin and secure
            var channel = new Channel("dannyvv-a1903", 2233, ChannelCredentials.Insecure);
            m_casClient = new ContentServer.ContentServerClient(channel);
            m_remoteClient = new RemoteAgentService.RemoteAgentServiceClient(channel);

            // m_fileContentManager = fileContentManager;
            // m_tempDirectoryCleaner = tempCleaner;
            // m_endPoints = endPoints;
            // m_client = AnyBuildClientFactory.CreateClient(
            //     Guid.NewGuid(),
            //     m_fileContentManager.LocalDiskContentStore.FileContentTable,
            //     endPoints.Select(e => new AgentInfo(e, 11337))
            // );
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeInputsAsync(RunnablePip runnablePip)
        {
            Contract.Assert(runnablePip.PipType == PipType.Process, "We should only remote process pips");

            using (OnPipExecutionStarted(runnablePip))
            {
                var process = (Process)runnablePip.Pip;
                var fileContentManager = runnablePip.Environment.State.FileContentManager;

                // Ensure all hashes are registered.
                fileContentManager.RegisterDirectoryDependencies(process);

                var files = new List<(AbsolutePath, FileContentInfo)>();
                var pinRequest = new PinBulkRequest()
                {
                    Header = new RequestHeader() { TraceId = runnablePip.LoggingContext.Session.Id, SessionId = 1, }
                };

                foreach (var file in process.Dependencies)
                {
                    addFile(file);
                }

                foreach (var directory in process.DirectoryDependencies)
                {
                    // Sealed directories have multiple files in them, that the task may use. Preemptively send the files over
                    foreach (var file in fileContentManager.ListSealedDirectoryContents(directory))
                    {
                        addFile(file);
                    }
                }
                
                var pinResult = await m_casClient.PinBulkAsync(pinRequest);

                foreach (var header in pinResult.Header)
                {
                    switch ((PinResult.ResultCode)header.Value.Result)
                    {
                        case PinResult.ResultCode.Error:
                            throw new InvalidOperationException("Error pinning: " + header.Value.ErrorMessage);
                        case PinResult.ResultCode.Success:
                            // Cool pinned
                            break;
                        case PinResult.ResultCode.ContentNotFound:
                            // have to upload
                            await
                            break;
                    }
                }

                void addFile(FileArtifact file)
                {
                    if (!fileContentManager.TryGetInputContent(file, out var fileMaterializationInfo))
                    {
                        throw new InvalidOperationException("TODO: Handle no input content");
                    }

                    var fileContentInfo = fileMaterializationInfo.FileContentInfo;
                    if (fileContentInfo.Hash == WellKnownContentHashes.UntrackedFile)
                    {
                        throw new InvalidOperationException("TODO: Handle untracked file");
                    }

                    files.Add((file.Path, fileContentInfo));
                    pinRequest.Hashes.Add(fileContentInfo.Hash.ToProto());
                }

            }
        }

        private void AddFile(LoggingContext loggingContext, PipExecutionContext context, AbsolutePath path, ContentHash hash)
        {
            var storeFileRequest = new StoreFileRequest
            {
                Header = loggingContext.ToHeader(),
                ContentHash = hash.ToProto(),
                Path = path.ToString(context.PathTable),

            };
            var x = await m_casClient.StoreFile()
        }
    }


    internal static class ProtoExtension
    {
        public static RequestHeader ToHeader(this LoggingContext loggingContext)
        {
            return new RequestHeader()
               {
                   TraceId = loggingContext.Session.Id,
                   SessionId = 1,
               };
        }

        public static ContentHashAndHashTypeData ToProto(this ContentHash hash)
        {
            return new ContentHashAndHashTypeData
               {
                   HashType = (int)hash.HashType,
                   ContentHash = hash.ToByteString()
               };
        }
    }
}
#endif