// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Scheduler {
    const anyBuildFolder = d`d:/src2/AnyBuild/src`;

    @@public
    export const protoFiles = GrpcSdk.generate({
                proto: globR(d`Cloud`, "*.proto"),
                includes: [
                    importFrom("BuildXL.Pips").Proto.include
                ],
            }).sources;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Scheduler",
        generateLogs: true,
        sources: [
            ...globR(d`.`, "*.cs"),
            ...protoFiles,
        ],
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Runtime.Serialization.dll,
                NetFx.System.Text.Encoding.dll
            ),
            Cache.dll,
            Processes.dll,
            Distribution.Grpc.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Pips").Proto.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("Google.Protobuf").pkg,
            importFrom("Grpc.Core").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("System.Interactive.Async").pkg,
            importFrom("Sdk.Selfhost.RocksDbSharp").pkg,
            ...addIf(qualifier.targetFramework === "net472",
                    BuildXLSdk.Factory.createBinaryFromFiles(f`${anyBuildFolder}/Services/Agent/Grpc/bin/Debug/net472/AnyBuild.Agent.Grpc.dll`),
                    BuildXLSdk.Factory.createBinaryFromFiles(f`${anyBuildFolder}/Client/ClientLib/bin/Debug/net472/AnyBuild.Client.dll`),
                    BuildXLSdk.Factory.createBinaryFromFiles(f`${anyBuildFolder}/Common/Grpc/bin/Debug/net472/AnyBuild.Common.Grpc.dll`)
            ),
        ],
        embeddedResources: [
            {
                resX: f`Filter/ErrorMessages.resx`
            }
        ],
        internalsVisibleTo: [
            "bxlanalyzer",
            "BuildXL.Engine",
            "Test.BuildXL.FingerprintStore",
            "Test.BuildXL.Scheduler",
            "Test.BuildXL.FrontEnd.MsBuild",
            "Test.Tool.Analyzers",
            "IntegrationTest.BuildXL.Scheduler",
        ],
    });
}
