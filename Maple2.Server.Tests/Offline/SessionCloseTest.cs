using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Maple2.Server.Core.Network;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Offline;

public class SessionCloseTest {
    private sealed class TestSession(TcpClient client) : Session(client) {
        protected override PatchType Type => PatchType.Ignore;
    }

    private sealed class Lines : ILogEventSink {
        public readonly List<string> Items = [];

        public void Emit(LogEvent logEvent) {
            lock (Items) {
                Items.Add(logEvent.RenderMessage());
            }
        }
    }

    // Migration sends GameToGame and disconnects at once. The client must still get the packet when it reads late and
    // has sent data the server never read (both made the old close reset the connection and drop the packet).
    [Test]
    public void Disconnect_RightAfterSend_ClientStillReceivesThePacket() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            using var client = new TcpClient();
            client.Connect((IPEndPoint) listener.LocalEndpoint);
            var session = new TestSession(listener.AcceptTcpClient());

            client.GetStream().Write(new byte[64]);
            Thread.Sleep(100);
            session.Send(0x01, 0x02, 0x03, 0x04);
            session.Disconnect();
            Thread.Sleep(300); // closed before the client reads

            client.ReceiveTimeout = 5000;
            var buffer = new byte[256];
            int total = 0, read;
            Assert.DoesNotThrow(() => {
                while ((read = client.GetStream().Read(buffer, 0, buffer.Length)) > 0) {
                    total += read;
                }
            }, "connection was reset instead of closed");
            Assert.That(total, Is.GreaterThan(4), "the packet sent before Disconnect never arrived");
        } finally {
            listener.Stop();
        }
    }

    // A frozen client reads nothing while the server's sends fill its socket; the session must wait for it (BT-134).
    [Test]
    public void Send_ClientReadsNothingForSevenSeconds_SessionKeepsTheConnection() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            using var client = new TcpClient { ReceiveBufferSize = 8192 };
            client.Connect((IPEndPoint) listener.LocalEndpoint);
            using var session = new TestSession(listener.AcceptTcpClient());

            const int count = 400;
            byte[] packet = new byte[16 * 1024];
            packet[0] = 0x01;
            for (int i = 0; i < count; i++) {
                session.Send(packet);
            }
            Thread.Sleep(7000); // the client reads nothing: its socket fills and the send worker waits

            client.ReceiveTimeout = 5000;
            var buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            Assert.DoesNotThrow(() => {
                while (total < (long) count * packet.Length && (read = client.GetStream().Read(buffer, 0, buffer.Length)) > 0) {
                    total += read;
                }
            }, "the session reset the connection while the client was not reading");
            Assert.That(total, Is.GreaterThanOrEqualTo((long) count * packet.Length), "the session ended before the client read its packets");
        } finally {
            listener.Stop();
        }
    }

    // NT-03 (BT-231): a connection the client ends leaves one Information line with the socket error (0 for a close, 10054 for a
    // reset) and the seconds since the client's last bytes. Today a close leaves no line and a reset only a Debug one.
    [Test]
    [NonParallelizable] // Log.Logger is process-wide.
    public void ClientEndsTheConnection_TheSessionLogsTheSocketError() {
        ILogger before = Log.Logger;
        var lines = new Lines();
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Sink(lines).CreateLogger();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string[] Ends() {
            lock (lines.Items) {
                return lines.Items.Where(line => line.StartsWith("Receive ended")).ToArray();
            }
        }
        try {
            foreach (bool reset in new[] { false, true }) {
                var client = new TcpClient();
                client.Connect((IPEndPoint) listener.LocalEndpoint);
                using var session = new TestSession(listener.AcceptTcpClient());
                session.Start();
                client.ReceiveTimeout = 5000;
                Assert.That(client.GetStream().Read(new byte[256], 0, 256), Is.GreaterThan(0), "the handshake");
                if (reset) {
                    client.Client.LingerState = new LingerOption(true, 0);
                    client.Client.Dispose(); // no shutdown first, so the connection is reset
                } else {
                    client.Close();
                }
                // The receive thread writes the line when it sees the end; a fixed 500 ms was too short once under load (BT-295).
                for (int i = 0; i < 100 && Ends().Length < (reset ? 2 : 1); i++) {
                    Thread.Sleep(50);
                }
            }

            string[] ends = Ends();
            Assert.That(ends, Has.Length.EqualTo(2), "one line per connection the client ended");
            Assert.That(ends[0], Does.Contain("socket error 0,"), "a close by the client");
            Assert.That(ends[1], Does.Contain("socket error 10054,"), "a reset");
            Assert.That(ends[0], Does.Contain("s since the last packet"));
        } finally {
            listener.Stop();
            Log.Logger = before;
        }
    }
}
