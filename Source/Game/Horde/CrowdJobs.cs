namespace CallOfSlop;

/// <summary>
/// A small fixed pool of worker threads for the crowd's per-frame work (docs/zombies/MASS-HORDE.md, "Rendering"):
/// <see cref="Run"/> splits a job into <see cref="Stripes"/> stripes, runs stripe 0 on the calling thread and the
/// others on the workers (stripe k always on worker k, so a stripe's own scratch arrays are only ever touched by one
/// thread at a time), and returns when all are done. Nothing allocates per call: the body is a delegate the caller
/// keeps, the workers sleep on their own semaphores. Up to 8 stripes (the logical processors, less one for the
/// rest of the frame when there are more than two); one stripe, run inline, on a single-core machine or when
/// <see cref="Serial"/> is set (the self-test's determinism check).
/// </summary>
public sealed class CrowdJobs
{
    public static readonly CrowdJobs Shared = new(Math.Clamp(Environment.ProcessorCount > 2 ? Environment.ProcessorCount - 1 : Environment.ProcessorCount, 1, 8));

    private readonly Thread[] _threads;
    private readonly SemaphoreSlim[] _go;
    private readonly ManualResetEventSlim _done = new(false);
    private Action<int>? _body;
    private int _left;
    private Exception? _error;

    /// <summary>Run every job on the calling thread, one stripe (still split the same way, so results match).</summary>
    public static bool Serial;

    /// <summary>How many stripes a job is split into (the same whether it runs in parallel or not).</summary>
    public int Stripes { get; }

    public CrowdJobs(int stripes)
    {
        Stripes = Math.Max(1, stripes);
        // (A first blocking wait makes the event's lock object: done here, once, so a run that has to block later (a busy
        // machine) allocates nothing; the self-tests count every byte.)
        _done.Reset();
        _done.Wait(1);
        _threads = new Thread[Stripes - 1];
        _go = new SemaphoreSlim[Stripes - 1];
        for (var k = 0; k < _threads.Length; k++)
        {
            var me = k + 1;
            _go[k] = new SemaphoreSlim(0);
            _threads[k] = new Thread(() => Loop(me)) { IsBackground = true, Name = $"Crowd {me}" };
            _threads[k].Start();
        }
    }

    /// <summary>Runs <paramref name="body"/>(stripe) for every stripe and waits for them all.</summary>
    public void Run(Action<int> body)
    {
        if (Serial || _threads.Length == 0)
        {
            for (var s = 0; s < Stripes; s++)
                body(s);
            return;
        }
        _body = body;
        _error = null;
        _left = Stripes;
        _done.Reset();
        foreach (var g in _go)
            g.Release();
        Work(0);
        _done.Wait();
        _body = null;
        if (_error != null)
            throw new InvalidOperationException("a crowd job failed", _error);
    }

    private void Work(int stripe)
    {
        try
        {
            _body!(stripe);
        }
        catch (Exception ex)
        {
            _error = ex;
        }
        finally
        {
            if (Interlocked.Decrement(ref _left) == 0)
                _done.Set();
        }
    }

    private void Loop(int stripe)
    {
        var go = _go[stripe - 1];
        while (true)
        {
            go.Wait();
            Work(stripe);
        }
    }

    /// <summary>The range [from, to) of stripe <paramref name="s"/> of <paramref name="n"/> items.</summary>
    public void Range(int s, int n, out int from, out int to)
    {
        from = (int)((long)n * s / Stripes);
        to = (int)((long)n * (s + 1) / Stripes);
    }
}
