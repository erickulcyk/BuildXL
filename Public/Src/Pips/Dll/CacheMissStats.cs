using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Pips
{
    public struct CacheMissStats
    {
        public PipId Pip;

        public CacheMissAnalysisResult CacheMissResult;

        public IEnumerable<(string, string)> Summary;
    }

    /// <summary>
    /// Cache miss analysis result
    /// </summary>
    public enum CacheMissAnalysisResult
    {
        /// <nodoc/>
        Invalid,

        /// <nodoc/>
        MissingFromOldBuild,

        /// <nodoc/>
        MissingFromNewBuild,

        /// <nodoc/>
        WeakFingerprintMismatch,

        /// <nodoc/>
        StrongFingerprintMismatch,

        /// <nodoc/>
        UncacheablePip,

        /// <nodoc/>
        DataMiss,

        /// <nodoc/>
        InvalidDescriptors,

        /// <nodoc/>
        ArtificialMiss,

        /// <nodoc/>
        NoMiss
    }
}
