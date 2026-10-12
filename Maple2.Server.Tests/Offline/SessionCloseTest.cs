using System.Net;
using System.Net.Sockets;
using System.Threading;
using Maple2.Server.Core.Network;

namespace Maple2.Server.Tests.Offline;

public class SessionCloseTest {
    private sealed class TestSession(TcpClient client) : Session(client) {
        protected override PatchType Type => PatchType.Ignore;
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
}
