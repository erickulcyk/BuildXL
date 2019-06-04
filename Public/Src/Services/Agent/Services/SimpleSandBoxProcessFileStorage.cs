using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Processes;

namespace Agent.Services
{
    public class SimpleSandBoxProcessFileStorage : ISandboxedProcessFileStorage
    {
        private string m_workingDirectory;

        public SimpleSandBoxProcessFileStorage(string workingDirectory)
        {
            m_workingDirectory = workingDirectory;
        }

        /// <nodoc />
        public string GetFileName(SandboxedProcessFile file)
        {
            return Path.Combine(m_workingDirectory, file.DefaultFileName());
        }
    }
}
