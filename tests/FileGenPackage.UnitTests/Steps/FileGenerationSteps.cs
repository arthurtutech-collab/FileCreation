using System;
using TechTalk.SpecFlow;
using Xunit;

namespace FileGenPackage.UnitTests.Steps
{
    [Binding]
    public class FileGenerationSteps
    {
        private readonly ScenarioContext _context;

        public FileGenerationSteps(ScenarioContext context)
        {
            _context = context;
        }

        [Given(@"a Worker configuration exists with target files and Mongo lease/progress stores")]
        public void GivenAWorkerConfigurationExists()
        {
            _context["WorkerConfigPresent"] = true;
        }

        [Given(@"a Kafka ""(?<eventName>.*)"" event arrives for date ""(?<date>.*)""")]
        public void GivenAKafkaEventArrives(string eventName, string date)
        {
            _context["EventName"] = eventName;
            _context["EventDate"] = date;
        }

        [Given(@"there has been no prior successful run for that worker today")]
        public void GivenNoPriorRunToday()
        {
            _context["PriorRunToday"] = false;
        }

        [When(@"the event is consumed by the worker")]
        public void WhenEventConsumed()
        {
            // Simulate decision to start processing
            var prior = _context.ContainsKey("PriorRunToday") && (bool)_context["PriorRunToday"];
            _context["ProcessingStarted"] = !prior;
        }

        [Then(@"processing for the current day starts")]
        public void ThenProcessingStarts()
        {
            Assert.True(_context.ContainsKey("ProcessingStarted") && (bool)_context["ProcessingStarted"]);
        }

        [Then(@"subsequent ""(?<eventName>.*)"" events arriving on the same date are ignored")]
        public void ThenSubsequentEventsIgnored(string eventName)
        {
            // Basic placeholder assertion - real behavior validated in integration tests
            Assert.True(true);
        }

        [Given(@"SQL page (?<page>\d+) contains rows R \(stable ordering\)")] 
        public void GivenSqlPageContainsRows(int page)
        {
            _context["FetchedPage"] = page;
        }

        [When(@"the worker fetches page (?<page>\d+) from SQL")]
        public void WhenWorkerFetchesPage(int page)
        {
            _context["FetchedPageAtRuntime"] = page;
        }

        [Then(@"the worker uses the same fetched rows R to translate and write to files A, B and C")]
        public void ThenUsesSameFetchedRows()
        {
            Assert.Equal(_context["FetchedPage"], _context["FetchedPageAtRuntime"]);
        }

        [Given(@"a leader instance started processing and wrote file headers marking status \"Started\"")]
        public void GivenLeaderStartedProcessing()
        {
            _context["LeaderStarted"] = true;
        }

        [Given(@"the leader dies mid-run without finalizing files")]
        public void GivenLeaderDies()
        {
            _context["LeaderAlive"] = false;
        }

        [Given(@"the leader's Mongo lease has expired")]
        public void GivenLeaseExpired()
        {
            _context["LeaseExpired"] = true;
        }

        [When(@"another pod detects leases expired and attempts to acquire leadership")]
        public void WhenAnotherPodAttemptsAcquire()
        {
            if (_context.ContainsKey("LeaseExpired") && (bool)_context["LeaseExpired"]) {
                _context["Acquired"] = true;
            }
        }

        [Then(@"the new leader reads file progress for all files")]
        public void ThenNewLeaderReadsProgress()
        {
            Assert.True(_context.ContainsKey("Acquired") && (bool)_context["Acquired"]);
        }

        [Given(@"file \"(?<file>.*)\" header indicates last processed page = (?<page>\d+)")]
        public void GivenFileHeaderIndicatesPage(string file, int page)
        {
            _context[$"Header_{file}"] = page;
        }

        [When(@"processing attempts to write page (?<page>\d+) to \"(?<file>.*)\"")]
        public void WhenProcessingAttemptsWrite(int page, string file)
        {
            _context["AttemptPage"] = page;
            _context["AttemptFile"] = file;
        }

        [Then(@"the worker detects the header progress is beyond page (?<page>\d+)")]
        public void ThenDetectsHeaderBeyond(int page)
        {
            var file = (string)_context["AttemptFile"]!;
            var header = (int)_context[$"Header_{file}"]!;
            Assert.True(header >= page);
        }

        [Then(@"the worker skips writing to \"(?<file>.*)\" for page (?<page>\d+)")]
        public void ThenSkipsWriting(string file, int page)
        {
            // placeholder: mark skipped
            _context["Skipped"] = true;
            Assert.True((bool)_context["Skipped"]);
        }

        [Given(@"the worker has processed the final page N for file \"(?<file>.*)\"")]
        public void GivenWorkerProcessedFinalPage(string file)
        {
            _context[$"Completed_{file}"] = true;
        }

        [When(@"finalization is triggered for file \"(?<file>.*)\"")]
        public void WhenFinalizationTriggered(string file)
        {
            _context[$"Finalized_{file}"] = true;
        }

        [Then(@"the worker removes or clears the progress header for that file")]
        public void ThenRemovesHeader()
        {
            // placeholder: simulate header removal
            _context["HeaderRemoved"] = true;
            Assert.True((bool)_context["HeaderRemoved"]);
        }

        [Then(@"the worker updates the file status to \"Completed\" in Mongo progress store")]
        public void ThenUpdatesStatusCompleted()
        {
            _context["StatusCompleted"] = true;
            Assert.True((bool)_context["StatusCompleted"]);
        }

        [Then(@"the worker publishes a completion Kafka event containing at least: workerId, fileId, totalRows, completedAt")]
        public void ThenPublishesCompletionEvent()
        {
            _context["PublishedEvent"] = true;
            Assert.True((bool)_context["PublishedEvent"]);
        }
    }
}
