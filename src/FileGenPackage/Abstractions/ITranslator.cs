namespace FileGenPackage.Abstractions;

/// <summary>
/// Per-file translator for converting database rows to output format.
/// </summary>
public interface ITranslator
{
    /// <summary>
    /// Translate a single row to output string.
    /// </summary>
    string TranslateRow(IReadOnlyDictionary<string, object?> row);

    /// <summary>
    /// Translate multiple rows. Default uses TranslateRow per row.
    /// </summary>
    IEnumerable<string> TranslateBatch(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        return rows.Select(TranslateRow);
    }
}

/// <summary>
/// Registry mapping translator IDs to translator instances.
/// </summary>
public interface ITranslatorRegistry
{
    ITranslator GetTranslator(string translatorId);
    void Register(string translatorId, ITranslator translator);
}

public class TranslatorRegistry : ITranslatorRegistry
{
    private readonly Dictionary<string, ITranslator> _translators = new();

    public ITranslator GetTranslator(string translatorId)
    {
        if (!_translators.TryGetValue(translatorId, out var translator))
            throw new KeyNotFoundException($"Translator '{translatorId}' not registered");
        return translator;
    }

    public void Register(string translatorId, ITranslator translator)
    {
        _translators[translatorId] = translator;
    }
}
