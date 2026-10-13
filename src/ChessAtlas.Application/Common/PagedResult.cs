namespace ChessAtlas.Application.Common;

/// <summary>A page of results plus the totals the UI needs to render pagination and empty states.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
    public bool IsEmpty => Items.Count == 0;

    public static PagedResult<T> Empty(int page = 1, int pageSize = 20) => new([], 0, page, pageSize);

    public PagedResult<TOut> Select<TOut>(Func<T, TOut> selector) =>
        new(Items.Select(selector).ToList(), TotalCount, Page, PageSize);
}

/// <summary>
/// Outcome of an operation that can fail in ways the user must see: a partial import, a rejected
/// PGN, an engine that will not start. Carrying the failures beats throwing them, because most of
/// these are "N of M worked" rather than all-or-nothing.
/// </summary>
public sealed record OperationOutcome
{
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];

    public bool Succeeded { get; private set; } = true;

    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<string> Warnings => _warnings;

    public int ItemsProcessed { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
    public int ItemsSkipped { get; set; }

    public void Fail(string message)
    {
        Succeeded = false;
        _errors.Add(message);
    }

    public void Warn(string message) => _warnings.Add(message);

    /// <summary>Records a per-item failure and keeps a bounded sample rather than millions of strings.</summary>
    public void RecordItemFailure(string message, int sampleLimit = 50)
    {
        ItemsFailed++;
        if (_errors.Count < sampleLimit) _errors.Add(message);
    }

    public static OperationOutcome Success() => new();
}

/// <summary>Progress reports for long operations, shaped for a progress bar plus a status line.</summary>
public sealed record OperationProgress(
    string Stage,
    long Completed,
    long Total,
    string? Message = null)
{
    public double? Fraction => Total <= 0 ? null : Math.Clamp(Completed / (double)Total, 0, 1);
    public string Label => Message ?? $"{Stage} {Completed}/{Total}";
}
