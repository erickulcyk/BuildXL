using System;
using System.IO;
using System.Threading.Tasks;
using Agent.Services;
using BuildXL.Cloud.Proto;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Grpc.Core;

namespace Agent
{
    public class BuildAgentService : BuildXL.Cloud.Proto.CloudWorkerService.CloudWorkerServiceBase
    {
        private BuildXLService m_service;

        public BuildAgentService(BuildXLService service)
        {
            m_service = service;
        }

        public override async Task<BuildResponse> RunProcess(BuildXL.Cloud.Proto.BuildRequest request,
            ServerCallContext serverContext)
        {
            var loggingContext = new LoggingContext(
                Guid.NewGuid(),
                "Cloud",
                new LoggingContext.SessionInfo(Guid.NewGuid().ToString(), "c3", Guid.NewGuid()));

            var context = BuildXLContext.CreateInstanceForTesting();
            var protoContext = new BuildXL.Pips.ProtobufSerializationContext(context.PathTable);
            protoContext.ReceivePathTable(request.PathTable); // First reception of state
            var process = BuildXL.Pips.Operations.Process.FromProto(protoContext, request.Process);

            var executor = new SimplePipExecutor(m_service.Configuration.Sandbox, loggingContext, context);
            var result = await executor.ExecuteProcessAsync(process);

            var response = new BuildResponse()
            {
                ExitCode = result.ExitCode,
            };

            // TODO: validate file access against policy
            protoContext.PreparePathTableForWrite();

            // TODO: Consider parallel loop.
            foreach (var access in result.FileAccesses)
            {
                switch (access.RequestedAccess)
                {
                    case RequestedAccess.All:
                    case RequestedAccess.Write:
                    case RequestedAccess.ReadWrite:
                        var path = access.ManifestPath;
                        if (!path.IsValid)
                        {
                            path = AbsolutePath.Create(context.PathTable, access.Path);
                        }

                        // TODO: Consider shortcut for expandedpath not going through pathtable.
                        var expandedPath = path.Expand(context.PathTable);

                        var contentHash = await m_service.Cache.TryStoreAsync(
                            BuildXL.Engine.Cache.Artifacts.FileRealizationMode.HardLinkOrCopy, 
                            expandedPath);

                        // TODO: share file handle between other IO.
                        var length = new FileInfo(expandedPath.ExpandedPath).Length;

                        if (!contentHash.Succeeded)
                        {
                            throw new InvalidOperationException("TODO: Handle failure");
                        }

                        if (!result.TryGetRewrite(path, out var file))
                        {
                            file = FileArtifact.CreateOutputFile(path);
                        }

                        response.OutputFile.Add(new OutputFile
                        {
                            // TODO: Handle rewrites
                            File = protoContext.ToProto(file),
                            ContentHash = protoContext.ToProto(contentHash.Result),
                            Length = length,
                        });
                        break;
                    // Consider handling other cases
                }
            }

            response.PathTable = protoContext.SendPathTable();

            return response;
        }


        private AbsolutePath GetLocalPath(PathTable pathTable, ReportedFileAccess fileAccess)
        {
            if (fileAccess.ManifestPath.IsValid)
            {
                return fileAccess.ManifestPath;
            }

            if (!string.IsNullOrEmpty(fileAccess.Path))
            {
                return AbsolutePath.Create(pathTable, fileAccess.Path);
            }

            throw new InvalidOperationException(
                "TODO: Figure out when this can happen. at least need to be more robust...");
        }
        //var loggingContext = new LoggingContext(
        //    Guid.NewGuid(),
        //    "Cloud",
        //    new LoggingContext.SessionInfo(Guid.NewGuid().ToString(), "c3", Guid.NewGuid()));

        //var context = BuildXLContext.CreateInstanceForTesting();
        //var protoContext = new BuildXL.Pips.ProtobufSerializationContext(context.PathTable);
        //protoContext.MergePathTable(request.PathTable);
        //var pip = BuildXL.Pips.Operations.Process.FromProto(protoContext, request.Process);

        //IPipExecutionEnvironment pipExecutionEnvironment = new PipExecutionEnvironment(
        //    context,
        //    m_service.Configuration
        //);


        //var operationContext = OperationContext.CreateUntracked(loggingContext);
        //var result = await PipExecutor.ExecuteProcessAsync(
        //        operationContext: operationContext,
        //        environment: pipExecutionEnvironment,
        //        state: null,
        //        pip: pip,
        //        fingerprint: null);


        //    return new BuildResponse { };

        //public override async Task<BuildResponse> RunProcess(BuildXL.Cloud.Proto.BuildRequest request, ServerCallContext serverContext)
        //{

        //    PipExecutor.ExecuteProcessAsync(

        //        PipExecutionEnvironmentExtensions,
        //        PipExecutionSate.)

        //    // TODO: Setup telemetry properly
        //    var loggingContext = new LoggingContext(Guid.NewGuid(), "Cloud",
        //        new LoggingContext.SessionInfo("c2", "c3", Guid.Empty));

        //    // TODO: proper context
        //    var context = BuildXLContext.CreateInstanceForTesting();

        //    var operationContext = OperationContext.CreateUntracked(loggingContext);
        //    var protoContext = new BuildXL.Pips.ProtobufSerializationContext(context.PathTable);
        //    protoContext.MergePathTable(request.PathTable);
        //    var pip = BuildXL.Pips.Operations.Process.FromProto(protoContext, request.Process);

        //    string processDescription = pip.GetDescription(context);

        //    // Execute the process when resources are available

        //    // TODO: Resource monitor queue
        //    // TODO: Add Retry


        //    // TODO: fill in data
        //    var rootMappings = new Dictionary<string, string>(); // TODO
        //    var processInContainerManager = new ProcessInContainerManager(loggingContext, context.PathTable); // TODO
        //    var pipEnvironment = new PipEnvironment();
        //    var directoryArtifactContext = new DirectoryArtifactContext();
        //    var logger = new SandboxedProcessLogger(loggingContext, pip, context);
        //    var pipFragmentRender = new PipFragmentRenderer(
        //        context.PathTable,
        //        null, // TODO
        //        null); // TODO;

        //    var executor = new SandboxedProcessPipExecutor(
        //        context: context,
        //        loggingContext: loggingContext,
        //        pip: pip,
        //        sandBoxConfig: m_service.Configuration.Sandbox,
        //        layoutConfig: m_service.Configuration.Layout,
        //        loggingConfig: m_service.Configuration.Logging,
        //        rootMappings: rootMappings,
        //        processInContainerManager: processInContainerManager,
        //        whitelist: null, // TODO
        //        makeInputPrivate: null, // TODO
        //        makeOutputPrivate: null, // TODO
        //        semanticPathExpander: null,
        //        disableConHostSharing: m_service.Configuration.Engine.DisableConHostSharing,
        //        pipEnvironment: pipEnvironment,
        //        validateDistribution: m_service.Configuration.Distribution.ValidateDistribution,
        //        directoryArtifactContext: directoryArtifactContext,
        //        logger: logger,
        //        processIdListener: null, // TODO
        //        pipDataRenderer: pipFragmentRender,
        //        buildEngineDirectory: AbsolutePath.Invalid, // TODO
        //        directoryTranslator: null, // TODO
        //        remainingUserRetryCount: 0);


        //    var executionResult = await executor.RunAsync(
        //        serverContext.CancellationToken,
        //        sandboxedKextConnection: null); // TODO: Mac

        //    var processExecutionResult = new ExecutionResult();

        //    // Skipped: Handle detours heap telemetry

        //    // Skipped: Common retry error states
        //    // Skipped: Error cases not worth retrying
        //    // Skipped: Resource management clenaup

        //    processExecutionResult.ReportSandboxedExecutionResult(executionResult);

        //    // Skipped: Report global counters

        //    // We may have some violations reported already (outright denied by the sandbox manifest).
        //    FileAccessReportingContext fileAccessReportingContext = executionResult.UnexpectedFileAccesses;

        //    // Skipped: Handle prep failures
        //    // Skipped: Handle cancellation

        //    // These are the results we know how to handle. PreperationFailed has already been handled above.
        //    if (!(executionResult.Status == SandboxedProcessPipExecutionStatus.Succeeded ||
        //          executionResult.Status == SandboxedProcessPipExecutionStatus.ExecutionFailed ||
        //          executionResult.Status == SandboxedProcessPipExecutionStatus.FileAccessMonitoringFailed ||
        //          executionResult.Status == SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed ||
        //          executionResult.Status == SandboxedProcessPipExecutionStatus.MismatchedMessageCount))
        //    {
        //        Contract.Assert(false, "Unexpected execution result " + executionResult.Status);
        //    }

        //    bool succeeded = executionResult.Status == SandboxedProcessPipExecutionStatus.Succeeded;

        //    if (executionResult.Status == SandboxedProcessPipExecutionStatus.ExecutionFailed ||
        //        executionResult.Status == SandboxedProcessPipExecutionStatus.FileAccessMonitoringFailed ||
        //        executionResult.Status == SandboxedProcessPipExecutionStatus.OutputWithNoFileAccessFailed ||
        //        executionResult.Status == SandboxedProcessPipExecutionStatus.MismatchedMessageCount)
        //    {
        //        Contract.Assert(operationContext.LoggingContext.ErrorWasLogged,
        //            I($"Error should have been logged for '{executionResult.Status}'"));
        //    }

        //    Contract.Assert(executionResult.ObservedFileAccesses != null,
        //        "Success / ExecutionFailed provides all execution-time fields");
        //    Contract.Assert(executionResult.UnexpectedFileAccesses != null,
        //        "Success / ExecutionFailed provides all execution-time fields");
        //    Contract.Assert(executionResult.PrimaryProcessTimes != null,
        //        "Success / ExecutionFailed provides all execution-time fields");

        //    //counters.AddToCounter(PipExecutorCounter.ExecuteProcessDuration,
        //    //    executionResult.PrimaryProcessTimes.TotalWallClockTime);

        //    using (operationContext.StartOperation(PipExecutorCounter.ProcessOutputsDuration))
        //    {
        //        ObservedInputProcessingResult observedInputValidationResult;
        //        using (operationContext.StartOperation(PipExecutorCounter.ProcessOutputsObservedInputValidationDuration))
        //        {
        //            // In addition, we need to verify that additional reported inputs are actually allowed, and furthermore record them.
        //            //
        //            // Don't track file changes in observed input processor when process execution failed. Running observed input processor has side effects
        //            // that some files get tracked by the file change tracker. Suppose that the process failed because it accesses paths that
        //            // are supposed to be untracked (but the user forgot to specify it in the spec). Those paths will be tracked by 
        //            // file change tracker because the observed input processor may try to probe and track those paths.
        //            observedInputValidationResult =
        //                await ValidateObservedFileAccesses(
        //                    operationContext,
        //                    environment,
        //                    state,
        //                    state.GetCacheableProcess(pip, environment),
        //                    fileAccessReportingContext,
        //                    executionResult.ObservedFileAccesses,
        //                    trackFileChanges: succeeded);
        //        }

        //        // Skipped: Validate file accesses

        //        // Store the dynamically observed accesses
        //        processExecutionResult.DynamicallyObservedFiles = observedInputValidationResult.DynamicallyObservedFiles;
        //        processExecutionResult.DynamicallyObservedEnumerations = observedInputValidationResult.DynamicallyObservedEnumerations;
        //        processExecutionResult.AllowedUndeclaredReads = observedInputValidationResult.AllowedUndeclaredSourceReads;
        //        processExecutionResult.AbsentPathProbesUnderOutputDirectories = observedInputValidationResult.AbsentPathProbesUnderNonDependenceOutputDirectories;

        //        if (observedInputValidationResult.Status == ObservedInputProcessingStatus.Aborted)
        //        {
        //            succeeded = false;
        //            Contract.Assume(operationContext.LoggingContext.ErrorWasLogged,
        //                "No error was logged when ValidateObservedAccesses failed");
        //        }

        //        if (pip.ProcessAbsentPathProbeInUndeclaredOpaquesMode ==
        //            Process.AbsentPathProbeInUndeclaredOpaquesMode.Relaxed
        //            && observedInputValidationResult.AbsentPathProbesUnderNonDependenceOutputDirectories.Count > 0)
        //        {
        //            bool isDirty = false;
        //            foreach (var absentPathProbe in observedInputValidationResult
        //                .AbsentPathProbesUnderNonDependenceOutputDirectories)
        //            {
        //                if (!pip.DirectoryDependencies.Any(dir => absentPathProbe.IsWithin(protoContext.PathTable, dir)))
        //                {
        //                    isDirty = true;
        //                    break;
        //                }
        //            }

        //            processExecutionResult.MustBeConsideredPerpetuallyDirty = isDirty;
        //        }

        //        // We have all violations now.
        //        UnexpectedFileAccessCounters unexpectedFilesAccesses = fileAccessReportingContext.Counters;
        //        processExecutionResult.ReportUnexpectedFileAccesses(unexpectedFilesAccesses);

        //        // Set file access violations which were not whitelisted for use by file access violation analyzer
        //        processExecutionResult.FileAccessViolationsNotWhitelisted =
        //            fileAccessReportingContext.FileAccessViolationsNotWhitelisted;
        //        processExecutionResult.WhitelistedFileAccessViolations =
        //            fileAccessReportingContext.WhitelistedFileAccessViolations;

        //        // We need to update this instance so used a boxed representation
        //        BoxRef<ProcessFingerprintComputationEventData> fingerprintComputation =
        //            new ProcessFingerprintComputationEventData
        //            {
        //                Kind = FingerprintComputationKind.Execution,
        //                PipId = pip.PipId,
        //                WeakFingerprint = new WeakContentFingerprint((fingerprint ?? ContentFingerprint.Zero).Hash),

        //                // This field is set later for successful strong fingerprint computation
        //                StrongFingerprintComputations =
        //                    CollectionUtilities.EmptyArray<ProcessStrongFingerprintComputationData>(),
        //            };

        //        bool outputHashSuccess = false;

        //        if (succeeded)
        //        {
        //            // We are now be able to store a descriptor and content for this process to cache if we wish.
        //            // But if the pip completed with (warning level) file monitoring violations (suppressed or not), there's good reason
        //            // to believe that there are missing inputs or outputs for the pip. This allows a nice compromise in which a build
        //            // author can iterate quickly on fixing monitoring errors in a large build - mostly cached except for those parts with warnings.
        //            // Of course, if the whitelist was configured to explicitly allow caching for those violations, we allow it.
        //            //
        //            // N.B. fileAccessReportingContext / unexpectedFilesAccesses accounts for violations from the execution itself as well as violations added by ValidateObservedAccesses
        //            bool skipCaching = true;
        //            ObservedInputProcessingResult? observedInputProcessingResultForCaching = null;

        //            if (unexpectedFilesAccesses.HasUncacheableFileAccesses)
        //            {
        //                Logger.Log.ScheduleProcessNotStoredToCacheDueToFileMonitoringViolations(operationContext,
        //                    processDescription);
        //            }
        //            else if (executionResult.NumberOfWarnings > 0 &&
        //                     ExtraFingerprintSalts.ArePipWarningsPromotedToErrors(environment.Configuration.Logging))
        //            {
        //                // Just like not caching errors, we also don't want to cache warnings that are promoted to errors
        //                Logger.Log.ScheduleProcessNotStoredToWarningsUnderWarnAsError(operationContext,
        //                    processDescription);
        //            }
        //            else if (!fingerprint.HasValue)
        //            {
        //                Logger.Log.ScheduleProcessNotStoredToCacheDueToInherentUncacheability(operationContext,
        //                    processDescription);
        //            }
        //            else
        //            {
        //                Contract.Assume(
        //                    observedInputValidationResult.Status == ObservedInputProcessingStatus.Success,
        //                    "Should never cache a process that failed observed file input validation (cacheable-whitelisted violations leave the validation successful).");

        //                // Note that we discard observed inputs if cache-ineligible (required by StoreDescriptorAndContentForProcess)
        //                observedInputProcessingResultForCaching = observedInputValidationResult;
        //                skipCaching = false;
        //            }

        //            // TODO: Maybe all counter updates should occur on distributed build master.
        //            if (skipCaching)
        //            {
        //                counters.IncrementCounter(PipExecutorCounter.ProcessPipsExecutedButUncacheable);
        //            }

        //            using (operationContext.StartOperation(PipExecutorCounter
        //                .ProcessOutputsStoreContentForProcessAndCreateCacheEntryDuration))
        //            {
        //                outputHashSuccess = await StoreContentForProcessAndCreateCacheEntryAsync(
        //                    operationContext,
        //                    environment,
        //                    state,
        //                    pip,
        //                    processDescription,
        //                    observedInputProcessingResultForCaching,
        //                    executionResult.EncodedStandardOutput,
        //                    // Possibly null
        //                    executionResult.EncodedStandardError,
        //                    // Possibly null
        //                    executionResult.NumberOfWarnings,
        //                    processExecutionResult,
        //                    enableCaching: !skipCaching,
        //                    fingerprintComputation: fingerprintComputation,
        //                    executionResult.ContainerConfiguration);
        //            }

        //            if (outputHashSuccess)
        //            {
        //                processExecutionResult.SetResult(operationContext, PipResultStatus.Succeeded);
        //                processExecutionResult.MustBeConsideredPerpetuallyDirty = skipCaching;
        //            }
        //            else
        //            {
        //                // The Pip itself did not fail, but we are marking it as a failure because we could not handle the post processing.
        //                Contract.Assume(
        //                    operationContext.LoggingContext.ErrorWasLogged,
        //                    "Error should have been logged for StoreContentForProcessAndCreateCacheEntry() failure");
        //            }
        //        }

        //        // Skipped: Partial xlg in failure case

        //        // Log the fingerprint computation
        //        // environment.State.ExecutionLog?.ProcessFingerprintComputation(fingerprintComputation.Value);

        //        if (!outputHashSuccess)
        //        {
        //            processExecutionResult.SetResult(operationContext, PipResultStatus.Failed);
        //        }

        //        return processExecutionResult;
        //    }
        //}
    }
}
