using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;

namespace Agent.Services
{
    public class BuildXLService
    {
        public IConfiguration Configuration { get; }

        public IArtifactContentCache Cache { get; }

        public BuildXLService()
        {
            Configuration = new ConfigurationImpl();
            ContentHashingUtilities.SetDefaultHashType();
            Cache = new InMemoryArtifactContentCache(null);
        }
    }
}
