// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as GrpcSdk from "Sdk.Protocols.Grpc";

namespace Proto {
    @@public
    export const include = Transformer.sealDirectory(
        d`.`,
        globR(d`.`, "*.proto")
    );

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Pips.Proto",
        sources: [
            ...globR(d`.`, "*.cs"),
            ...GrpcSdk.generate({
                proto: globR(d`.`, "*.proto"),
                enableGrpc: false,
            }).sources,
        ],
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("Google.Protobuf").pkg,
        ],
    });
}