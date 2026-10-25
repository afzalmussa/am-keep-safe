using AMKeepSafe.Core.Logging;

namespace AMKeepSafe.Core.Checks;

/// <summary>
/// Runs every check, a few at a time. A check that fails or takes too long shows as "couldn't
/// check" rather than stopping the others, and every result goes in the log (trust rule 9).
/// </summary>
public sealed class SafetyCheckRunner(IReadOnlyList<ISafetyCheck> checks, CheckCatalog catalog, IAppLog log, TimeSpan? perCheckTimeout = null)
{
    private const int RunAtOnce = 4;

    // Waiting updates are looked up online, which can be slow, so allow plenty of time.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    private readonly TimeSpan _timeout = perCheckTimeout ?? DefaultTimeout;

    /// <param name="progress">Told how many checks have finished, out of how many.</param>
    public async Task<IReadOnlyList<CheckResult>> RunAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var toRun = catalog.Checks
            .Select(definition => (Definition: definition, Check: checks.FirstOrDefault(c => c.Id == definition.Id)))
            .Where(pair => pair.Check is not null)
            .ToList();

        log.Info($"Safety check started: {toRun.Count} checks.");
        var done = 0;
        progress?.Report((0, toRun.Count));

        using var gate = new SemaphoreSlim(RunAtOnce);
        var tasks = toRun.Select(async pair =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RunOneAsync(pair.Check!, pair.Definition, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
                progress?.Report((Interlocked.Increment(ref done), toRun.Count));
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        log.Info($"Safety check finished. {Describe(results)}");
        return results;
    }

    private async Task<CheckResult> RunOneAsync(ISafetyCheck check, CheckDefinition definition, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_timeout);

        CheckOutcome outcome;
        try
        {
            outcome = await check.RunAsync(definition, limit.Token).ConfigureAwait(false);
        }
        catch (CouldNotReadException ex)
        {
            log.Warning($"Check '{check.Id}' couldn't read what it needed: {ex.Message}");
            return catalog.RenderCouldNotCheck(definition);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.Warning($"Check '{check.Id}' took longer than {_timeout.TotalSeconds:0} seconds and was stopped.");
            return catalog.RenderCouldNotCheck(definition);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error($"Check '{check.Id}' failed.", ex);
            return catalog.RenderCouldNotCheck(definition);
        }

        if (outcome.Code == CheckCatalog.CouldNotCheckCode)
        {
            log.Warning($"Check '{check.Id}' couldn't tell. {outcome.Detail}");
            return catalog.RenderCouldNotCheck(definition);
        }

        if (!definition.Outcomes.ContainsKey(outcome.Code))
        {
            log.Error($"Check '{check.Id}' returned '{outcome.Code}', which has no wording in checks.json.");
        }

        var result = catalog.Render(definition, outcome);
        var detail = string.IsNullOrWhiteSpace(outcome.Detail) ? "" : $" Found: {outcome.Detail}";
        var items = result.Items.Count == 0 ? "" : $" Items: {string.Join("; ", result.Items)}.";
        log.Write(
            result.Status.NeedsAction() ? LogLevel.Warning : LogLevel.Info,
            $"Check '{check.Id}': {result.Status} ({result.OutcomeCode}).{detail}{items}");
        return result;
    }

    private static string Describe(IReadOnlyList<CheckResult> results) =>
        string.Join(", ", results.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}"));
}
