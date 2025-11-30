using System.Threading;
using System.Threading.Tasks;
using FileGenPackage.Abstractions;

namespace FileGenPackage.UnitTests.Helpers
{
    public class MockEventPublisher : IEventPublisher
    {
        public bool Published { get; private set; }
        public string? WorkerId { get; private set; }
        public string? FileId { get; private set; }
        public string? EventType { get; private set; }

        public Task PublishCompletedAsync(string workerId, string fileId, string eventType, CancellationToken ct = default)
        {
            Published = true;
            WorkerId = workerId;
            FileId = fileId;
            EventType = eventType;
            return Task.CompletedTask;
        }
    }
}
