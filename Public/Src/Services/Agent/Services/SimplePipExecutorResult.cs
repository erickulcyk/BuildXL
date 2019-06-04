using System.Collections.Generic;
using BuildXL.Processes;
using BuildXL.Utilities;

namespace Agent.Services
{
    public class SimplePipExecutorResult
    {
        private readonly SandboxedProcessResult m_sandboxResult;
        private readonly IReadOnlyDictionary<AbsolutePath, FileArtifact> m_rewrites;

        public SimplePipExecutorResult(SandboxedProcessResult sandboxResult, IReadOnlyDictionary<AbsolutePath, FileArtifact> rewrites)
        {
            m_sandboxResult = sandboxResult;
            m_rewrites = rewrites;
        }

        public bool TryGetRewrite(AbsolutePath path, out FileArtifact file)
        {
            return m_rewrites.TryGetValue(path, out file);
        }

        public int ExitCode => m_sandboxResult.ExitCode;

        public ISet<ReportedFileAccess> FileAccesses => m_sandboxResult.FileAccesses;

    }
}
