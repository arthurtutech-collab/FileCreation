using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileGenPackage.Abstractions;

namespace FileGenPackage.UnitTests.Helpers
{
    public class InMemoryPageReader : IPageReader
    {
        private readonly List<List<IReadOnlyDictionary<string, object?>>> _pages;

        public InMemoryPageReader(IEnumerable<IEnumerable<IReadOnlyDictionary<string, object?>>> pages)
        {
            _pages = pages.Select(p => p.ToList().AsReadOnly()).Select(l => l.Cast<IReadOnlyDictionary<string, object?>>().ToList()).ToList();
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadPageAsync(int pageNumber, CancellationToken ct = default)
        {
            // pageNumber is 1-based in our convention here
            var idx = pageNumber - 1;
            if (idx < 0 || idx >= _pages.Count)
                return Task.FromResult((IReadOnlyList<IReadOnlyDictionary<string, object?>>)new List<IReadOnlyDictionary<string, object?>>());

            return Task.FromResult((IReadOnlyList<IReadOnlyDictionary<string, object?>>)_pages[idx]);
        }

        public Task<long> GetTotalRowCountAsync(CancellationToken ct = default)
        {
            long total = 0;
            foreach (var p in _pages) total += p.Count;
            return Task.FromResult(total);
        }
    }
}
