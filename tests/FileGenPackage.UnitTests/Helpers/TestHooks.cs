using System;
using System.IO;
using System.Threading.Tasks;
using TechTalk.SpecFlow;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using FileGenPackage.Infrastructure;

namespace FileGenPackage.UnitTests.Helpers
{
    [Binding]
    public class TestHooks
    {
        private MongoDbContainer? _container;
        private readonly ScenarioContext _context;

        public TestHooks(ScenarioContext context)
        {
            _context = context;
        }

        [BeforeScenario]
        public async Task BeforeScenarioAsync()
        {
            // Try to start a MongoDB Testcontainer. If Docker isn't available, mark as disabled.
            try
            {
                _container = new MongoDbBuilder().Build();
                await _container.StartAsync();

                var conn = _container.GetConnectionString();
                var client = new MongoClient(conn);

                var config = new FileGenPackage.Abstractions.MongoConfig
                {
                    ConnectionString = conn,
                    Database = "test",
                    LeaseCollection = "leases",
                    StatusCollection = "status"
                };

                var leaseStore = new MongoLeaseStore(client, config, NullLogger<MongoLeaseStore>.Instance);
                var progressStore = new MongoProgressStore(client, config, NullLogger<MongoProgressStore>.Instance);

                // Temporary output directory for file writers
                var outRoot = Path.Combine(Path.GetTempPath(), "filegen_tests", Guid.NewGuid().ToString());
                Directory.CreateDirectory(outRoot);

                var writerFactory = new BufferedFileWriterFactory(NullLogger<BufferedFileWriterFactory>.Instance);

                _context["MongoClient"] = client;
                _context["LeaseStore"] = leaseStore;
                _context["ProgressStore"] = progressStore;
                _context["OutputRoot"] = outRoot;
                _context["WriterFactory"] = writerFactory;
                _context["TestcontainersEnabled"] = true;
            }
            catch (ArgumentException)
            {
                // Docker/testcontainers not available — mark scenario to use mocked helpers instead
                _context["TestcontainersEnabled"] = false;
            }
        }

        [AfterScenario]
        public async Task AfterScenarioAsync()
        {
            try
            {
                if (_context.TryGetValue("OutputRoot", out var o) && o is string outRoot)
                {
                    try { Directory.Delete(outRoot, true); } catch { }
                }

                if (_container != null)
                {
                    await _container.StopAsync();
                    await _container.DisposeAsync();
                }
            }
            catch { }
        }
    }
}
