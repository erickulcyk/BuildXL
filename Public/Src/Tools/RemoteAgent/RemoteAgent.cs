﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cloud.Proto;
using Grpc.Core;

namespace RemoteAgent
{
    public class RemoteAgent : IDisposable
    {
        // CAS
        private FileLog m_casFileLog;
        private Logger m_casLogger;
        private IContentSession m_casSession;
        private Server m_server;

        public RemoteAgent()
        {
        }


        public async Task StartAsync(string root, int port)
        {
            await StartCacheAsync(root);
            await StartServiceAsync(port);
        }

        private async Task StartCacheAsync(string root)
        {
            m_casFileLog = new FileLog(Path.Combine(root, @"Logs\CAS.log"));
            m_casLogger = new Logger(m_casFileLog);
            var casContext = new BuildXL.Cache.ContentStore.Interfaces.Tracing.Context(m_casLogger);

            var contentStore = new FileSystemContentStore(
                new PassThroughFileSystem(m_casLogger),
                SystemClock.Instance,
                new BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath(Path.Combine(root, "CAS")),
                configurationModel: new ConfigurationModel(new ContentStoreConfiguration(), ConfigurationSelection.RequireAndUseInProcessConfiguration, MissingConfigurationFileOption.DoNotWrite),
                settings: ContentStoreSettings.DefaultSettings);

            var storeResult = await contentStore.StartupAsync(casContext);
            if (!storeResult.Succeeded)
            {
                throw new InvalidOperationException("Failed to start cache: " + storeResult.ErrorMessage);
            }

            var createContentResult = contentStore.CreateSession(
                casContext,
                "RemoteAgent",
                ImplicitPin.PutAndGet);
            if (!createContentResult.Succeeded)
            {
                // $TODO: Handle error better
                throw new InvalidOperationException("Failed to create cache: " + createContentResult.ErrorMessage);
            }

            m_casSession = createContentResult.Session;

            var sessionResult = await m_casSession.StartupAsync(new BuildXL.Cache.ContentStore.Interfaces.Tracing.Context(m_casLogger));
            if (!sessionResult.Succeeded)
            {
                // $TODO: Handle error better
                throw new InvalidOperationException("Failed to start cache:" + sessionResult.ErrorMessage);
            }

        }

        private async Task StartServiceAsync(int port)
        {
            m_server = new Server()
               {
                   Services =
                   {
                       ContentServer.BindService(new RemoteCasImpl(m_casSession, m_casLogger)),
                       RemoteAgentService.BindService(new RemoteAgentServiceImpl()),
                   },
                   Ports =
                   {
                       // TODO: Secure channels
                       new ServerPort(IPAddress.Any.ToString(), port, ServerCredentials.Insecure)
                   }
               };

            m_server.Start();
        }

        public async Task ShutDownAsync()
        {
            if (m_server != null)
            {
                await m_server.ShutdownAsync();
            }

            m_server = null;
            Dispose();
        }

        public void Dispose()
        {
            m_casSession?.Dispose();
            m_casSession = null;
            m_casLogger?.Dispose();
            m_casLogger = null;

            m_casFileLog?.Dispose();
            m_casFileLog = null;
            
        }
    }
}
