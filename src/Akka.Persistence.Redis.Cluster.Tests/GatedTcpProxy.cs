// -----------------------------------------------------------------------
// <copyright file="GatedTcpProxy.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    /// <summary>
    /// A local TCP proxy in front of a Redis node. While the gate is closed every accepted
    /// connection is dropped, so a multiplexer that connects through the proxy stays
    /// disconnected, as it would against a server that is not reachable yet. Once the gate
    /// is opened, connections are forwarded to the target.
    /// </summary>
    public sealed class GatedTcpProxy : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _targetHost;
        private readonly int _targetPort;
        private readonly CancellationTokenSource _cts = new();
        private volatile bool _open;

        public GatedTcpProxy(string targetHost, int targetPort)
        {
            _targetHost = targetHost;
            _targetPort = targetPort;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = AcceptLoopAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public void Open() => _open = true;

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch
                {
                    return;
                }

                if (!_open)
                {
                    client.Close();
                    continue;
                }

                _ = ForwardAsync(client);
            }
        }

        private async Task ForwardAsync(TcpClient client)
        {
            using (client)
            using (var upstream = new TcpClient())
            {
                try
                {
                    await upstream.ConnectAsync(_targetHost, _targetPort);
                    var clientStream = client.GetStream();
                    var upstreamStream = upstream.GetStream();
                    await Task.WhenAny(
                        clientStream.CopyToAsync(upstreamStream, 81920, _cts.Token),
                        upstreamStream.CopyToAsync(clientStream, 81920, _cts.Token));
                }
                catch
                {
                    // connection closed by either side
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
