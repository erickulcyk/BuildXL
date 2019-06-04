config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                f`module.config.dsc`,
                f`${Environment.getPathValue("BUILDXL_BIN")}/Sdk/Sdk.Transformers/package.config.dsc`,
            ]
        },
    ],
    mounts: [
        {
            name: a`Out`,
            path: p`Out\Bin`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true
        },
        {
            name: a`Agent`,
            path: p`d:/src2/anybuild/distrib/debug/agent`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true
        },
    ]
});