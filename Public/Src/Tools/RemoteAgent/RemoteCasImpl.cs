// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cloud.Proto;
using BuildXL.Native.IO;
using Grpc.Core;

namespace RemoteAgent
{
    public class RemoteCasImpl : ContentServer.ContentServerBase
    {
        private IContentSession m_casSession;
        private ILogger m_logger;

        public RemoteCasImpl(IContentSession casSession, ILogger logger)
        {
            m_casSession = casSession;
            m_logger = logger;
        }

        public override async Task<PinBulkResponse> PinBulk(PinBulkRequest request, ServerCallContext context)
        {
            var startTime = DateTime.UtcNow;
            var cacheContext = new BuildXL.Cache.ContentStore.Interfaces.Tracing.Context(new Guid(request.Header.TraceId), m_logger);

            var pinList = new List<ContentHash>();
            foreach (var hash in request.Hashes)
            {
                pinList.Add(hash.ContentHash.ToContentHash((HashType)hash.HashType));
            }

            List<Task<Indexed<PinResult>>> pinResults = (await m_casSession.PinAsync(
                cacheContext,
                pinList,
                context.CancellationToken)).ToList();

            var response = new PinBulkResponse();
            try
            {
                foreach (var pinResult in pinResults)
                {
                    var result = await pinResult;
                    var item = result.Item;
                    var responseHeader =
                        new ResponseHeader()
                        {
                            Succeeded = item.Succeeded,
                            Result = (int)item.Code,
                            Diagnostics = item.Diagnostics,
                            ErrorMessage = item.ErrorMessage,
                            ServerReceiptTimeUtcTicks = startTime.Ticks,
                        };
                    response.Header.Add(result.Index, responseHeader);
                }
            }
            catch (Exception)
            {
                pinResults.ForEach(task => task.FireAndForget(cacheContext));
                throw;
            }

            return response;
        }

        public override async Task<StoreFileResponse> StoreFile(IAsyncStreamReader<StoreFileRequest> requestStream, ServerCallContext context)
        {
            BuildXL.Cache.ContentStore.Interfaces.Tracing.Context cacheContext = null;
            Stream stream = null;
            string path = null;
            ContentHash contentHash = default(ContentHash);

            var startTime = DateTime.UtcNow;

            try
            {
                while (await requestStream.MoveNext().ConfigureAwait(false))
                {
                    var storeFileRequest = requestStream.Current;
                    if (stream == null)
                    {
                        cacheContext = new BuildXL.Cache.ContentStore.Interfaces.Tracing.Context(new Guid(storeFileRequest.Header.TraceId), m_logger);

                        contentHash = storeFileRequest.ContentHash.ToContentHash((HashType)storeFileRequest.HashType);
                        path = storeFileRequest.Path;
                        stream = FileUtilities.CreateAsyncFileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                    }

                    storeFileRequest.FileContent.Content.WriteTo(stream);
                }

                stream.Close();
                var putResult = await m_casSession.PutFileAsync(cacheContext, contentHash, new AbsolutePath(path), FileRealizationMode.Any, context.CancellationToken);

                return new StoreFileResponse()
                       {
                           Header = new ResponseHeader()
                                {
                                    Succeeded = putResult.Succeeded,
                                    Result = putResult.Succeeded ? 0 : 1,
                                    Diagnostics = putResult.Diagnostics,
                                    ErrorMessage = putResult.ErrorMessage,
                                    ServerReceiptTimeUtcTicks = startTime.Ticks,
                                }
                };
            }
            finally
            {
                stream?.Dispose();
            }
        }
    }
}
