using System.Text;
using AMKeepSafe.Core.Checks;
using AMKeepSafe.Core.Logging;
using AMKeepSafe.Tests.Support;

namespace AMKeepSafe.Tests.Checks;

public class SafetyCheckRunnerTests
{
    private static readonly CheckCatalog Catalog = CheckCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes("""
        {
          "statusLabels": { "good": "G", "attention": "A", "problem": "P", "info": "I", "unknown": "U" },
          "summary": { "allClear": "All clear.", "someToLookAt": "Look.", "someNotChecked": "Some not checked.", "whatWeCheckedLink": "See" },
          "couldNotCheck": { "status": "unknown", "summary": "Couldn't check." },
          "checks": [
            { "id": "one", "title": "One", "whatWeCheck": "1", "outcomes": { "ok": { "status": "good", "summary": "One is fine." }, "bad": { "status": "problem", "summary": "One isn't fine." } } },
            { "id": "two", "title": "Two", "whatWeCheck": "2", "outcomes": { "ok": { "status": "good", "summary": "Two is fine." } } },
            { "id": "three", "title": "Three", "whatWeCheck": "3", "outcomes": { "ok": { "status": "good", "summary": "Three is fine." } } }
          ]
        }
        """)));

    private sealed class StubCheck(string id, Func<CancellationToken, Task<CheckOutcome>> run) : ISafetyCheck
    {
        public string Id => id;

        public IReadOnlyCollection<string> OutcomeCodes => ["ok", "bad"];

        public Task<CheckOutcome> RunAsync(CheckDefinition definition, CancellationToken cancellationToken) => run(cancellationToken);
    }

    private static StubCheck Returns(string id, string code, string? detail = null) =>
        new(id, _ => Task.FromResult(new CheckOutcome(code, Detail: detail)));

    private readonly MemoryLog _log = new();

    private SafetyCheckRunner Runner(params ISafetyCheck[] checks) => new(checks, Catalog, _log, TimeSpan.FromMilliseconds(300));

    [Fact]
    public async Task Results_come_back_in_the_order_of_checks_json_whatever_order_they_finish()
    {
        var slow = new StubCheck("one", async ct => { await Task.Delay(100, ct); return new CheckOutcome("ok"); });

        var results = await Runner(Returns("three", "ok"), slow, Returns("two", "ok")).RunAsync();

        Assert.Equal(["one", "two", "three"], results.Select(r => r.CheckId));
    }

    [Fact]
    public async Task A_check_that_throws_shows_as_couldnt_check_and_the_others_still_run()
    {
        var broken = new StubCheck("one", _ => throw new InvalidOperationException("boom"));

        var results = await Runner(broken, Returns("two", "ok")).RunAsync();

        Assert.Equal(CheckStatus.Unknown, results[0].Status);
        Assert.Equal("Couldn't check.", results[0].Summary);
        Assert.Equal(CheckStatus.Good, results[1].Status);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("'one' failed") && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task A_check_that_takes_too_long_is_stopped_and_shows_as_couldnt_check()
    {
        var hanging = new StubCheck("one", async ct => { await Task.Delay(Timeout.Infinite, ct); return new CheckOutcome("ok"); });

        var results = await Runner(hanging).RunAsync();

        Assert.Equal(CheckStatus.Unknown, Assert.Single(results).Status);
        Assert.True(_log.Has(LogLevel.Warning, "took longer than"));
    }

    [Fact]
    public async Task Windows_not_answering_is_a_warning_not_an_error()
    {
        var unreadable = new StubCheck("one", _ => throw new CouldNotReadException("No answer from Windows."));

        var results = await Runner(unreadable).RunAsync();

        Assert.Equal(CheckStatus.Unknown, results[0].Status);
        Assert.True(_log.Has(LogLevel.Warning, "No answer from Windows"));
        Assert.DoesNotContain(_log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_check_saying_it_couldnt_tell_shows_as_couldnt_check()
    {
        var results = await Runner(Returns("one", CheckCatalog.CouldNotCheckCode, "Windows said nothing.")).RunAsync();

        Assert.Equal(CheckStatus.Unknown, results[0].Status);
        Assert.True(_log.Has(LogLevel.Warning, "Windows said nothing"));
    }

    [Fact]
    public async Task An_outcome_with_no_wording_is_logged_as_an_error_and_shown_as_couldnt_check()
    {
        var results = await Runner(Returns("two", "bad")).RunAsync();

        Assert.Equal(CheckStatus.Unknown, results[0].Status);
        Assert.True(_log.Has(LogLevel.Error, "no wording"));
    }

    [Fact]
    public async Task Every_result_is_logged_with_what_was_found()
    {
        await Runner(Returns("one", "bad", "Firewall off for Public."), Returns("two", "ok")).RunAsync();

        Assert.True(_log.Has(LogLevel.Warning, "Check 'one': Problem (bad). Found: Firewall off for Public."));
        Assert.True(_log.Has(LogLevel.Info, "Check 'two': Good (ok)."));
        Assert.True(_log.Has(LogLevel.Info, "Safety check started: 2 checks."));
        Assert.True(_log.Has(LogLevel.Info, "Safety check finished. Problem: 1, Good: 1"));
    }

    [Fact]
    public async Task Checks_with_no_entry_in_the_catalog_are_skipped()
    {
        var results = await Runner(Returns("one", "ok"), Returns("not-in-file", "ok")).RunAsync();

        Assert.Equal(["one"], results.Select(r => r.CheckId));
    }

    [Fact]
    public async Task Progress_counts_up_to_the_total()
    {
        var reports = new List<(int Done, int Total)>();
        var progress = new SyncProgress(reports.Add);

        await Runner(Returns("one", "ok"), Returns("two", "ok"), Returns("three", "ok")).RunAsync(progress);

        Assert.Equal((0, 3), reports[0]);
        Assert.Equal((3, 3), reports[^1]);
        Assert.Equal(4, reports.Count);
    }

    [Fact]
    public async Task Cancelling_stops_the_check_rather_than_showing_results()
    {
        using var cancel = new CancellationTokenSource();
        var hanging = new StubCheck("one", async ct => { await Task.Delay(Timeout.Infinite, ct); return new CheckOutcome("ok"); });
        var runner = new SafetyCheckRunner([hanging], Catalog, _log, TimeSpan.FromMinutes(5));

        var running = runner.RunAsync(cancellationToken: cancel.Token);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    // Progress<T> posts to a thread pool, which makes the order unpredictable in a test.
    private sealed class SyncProgress(Action<(int, int)> report) : IProgress<(int Done, int Total)>
    {
        private readonly object _gate = new();

        public void Report((int Done, int Total) value)
        {
            lock (_gate)
            {
                report(value);
            }
        }
    }
}
