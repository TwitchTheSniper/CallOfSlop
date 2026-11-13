using System.Net;
using System.Net.Sockets;

namespace CallOfSlop;

/// <summary>
/// A self-test's bad network between loopback peers: a UDP relay on its own port that forwards each client to the host
/// through a socket of its own, adding a delay, a jitter and an independent loss to every datagram, each way, from its
/// own random numbers. RakNet's own simulator (<see cref="Net.SimulateBadNetwork"/>) draws its loss from the library's
/// shared random numbers, and with three peers in one process its losses came together: every link, both ways, went dark
/// for about a second at once (2026-11-13, "Sandbox physics gun feel": a watcher's copy ran 50 ticks past its last pose),
/// which no network a player is on does and no game can carry through.
/// </summary>
internal sealed class SelfTestLossyRelay : IDisposable
{
    private readonly Socket _front;
    private readonly IPEndPoint _host;
    private readonly Dictionary<EndPoint, Socket> _backs = new();
    private readonly List<(long Due, Socket From, EndPoint To, byte[] Data, int Length)> _queue = new(256);
    private readonly object _lock = new();
    private readonly Random _rng;
    private readonly Thread _thread;
    private volatile bool _stop;
    private int _delayMs, _jitterMs;
    private double _loss;

    public ushort Port { get; }

    public SelfTestLossyRelay(ushort hostPort, ushort port, int seed)
    {
        _rng = new Random(seed);
        _host = new IPEndPoint(IPAddress.Loopback, hostPort);
        _front = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _front.Bind(new IPEndPoint(IPAddress.Loopback, port));
        Port = port;
        _thread = new Thread(Run) { IsBackground = true, Name = "selftest relay", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Each way: a delay, a jitter (0 to this many ms on top) and a loss (percent), for every datagram from now on.</summary>
    public void Set(int delayMs, int jitterMs, float lossPercent)
    {
        lock (_lock)
        {
            _delayMs = delayMs;
            _jitterMs = jitterMs;
            _loss = lossPercent / 100.0;
        }
    }

    private void Run()
    {
        var buf = new byte[2048];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var sockets = new List<Socket>(4);
        while (!_stop)
        {
            sockets.Clear();
            sockets.Add(_front);
            lock (_lock)
                sockets.AddRange(_backs.Values);
            try
            {
                Socket.Select(sockets, null, null, 1000); // (1 ms)
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            foreach (var s in sockets)
            {
                while (s.Available > 0)
                {
                    EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    int n;
                    try
                    {
                        n = s.ReceiveFrom(buf, ref from);
                    }
                    catch (SocketException)
                    {
                        continue; // (a port unreachable from the far side)
                    }
                    Socket via;
                    EndPoint to;
                    if (s == _front)
                    {
                        lock (_lock)
                        {
                            if (!_backs.TryGetValue(from, out via!))
                            {
                                via = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                                via.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                                _backs[from] = via;
                            }
                        }
                        to = _host;
                    }
                    else
                    {
                        via = _front;
                        to = _backs.First(kv => kv.Value == s).Key;
                    }
                    lock (_lock)
                    {
                        if (_rng.NextDouble() < _loss)
                            continue;
                        var due = clock.ElapsedMilliseconds + _delayMs + (_jitterMs > 0 ? _rng.Next(_jitterMs + 1) : 0);
                        _queue.Add((due, via, to, buf[..n], n));
                    }
                }
            }
            var now = clock.ElapsedMilliseconds;
            lock (_lock)
                for (var i = 0; i < _queue.Count;)
                {
                    var q = _queue[i];
                    if (q.Due > now)
                    {
                        i++;
                        continue;
                    }
                    _queue.RemoveAt(i);
                    try
                    {
                        q.From.SendTo(q.Data, 0, q.Length, SocketFlags.None, q.To);
                    }
                    catch (SocketException)
                    {
                    }
                }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(500);
        _front.Dispose();
        lock (_lock)
            foreach (var s in _backs.Values)
                s.Dispose();
    }
}
