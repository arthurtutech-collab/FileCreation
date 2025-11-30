using System.Collections.Generic;
using FileGenPackage.Abstractions;

namespace FileGenPackage.UnitTests.Helpers
{
    public class SimpleTranslator : ITranslator
    {
        public string TranslateRow(IReadOnlyDictionary<string, object?> row)
        {
            // join values with comma
            return string.Join(',', row.Values);
        }
    }
}
