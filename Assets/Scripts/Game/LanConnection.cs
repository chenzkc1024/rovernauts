using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Rovernauts
{
    /// <summary>One framed TCP connection. Polled by the game thread without blocking.</summary>
    internal sealed class LanConnection : IDisposable
    {
        internal const int Port = 48600;
        internal const int MaxFrame = 256 * 1024;
        readonly Socket socket;
        readonly byte[] incoming = new byte[MaxFrame + 4];
        int received, expected;
        byte[] outgoing;
        int sent;

        public bool CanSend => outgoing == null;

        public LanConnection(Socket socket)
        {
            this.socket = socket;
            socket.NoDelay = true;
            socket.Blocking = false;
        }

        public void Send(byte[] payload)
        {
            if (!CanSend) return;
            if (payload.Length < 1 || payload.Length > MaxFrame) throw new IOException("Invalid LAN frame size.");
            outgoing = new byte[4 + payload.Length];
            int count = payload.Length;
            outgoing[0] = (byte)(count >> 24);
            outgoing[1] = (byte)(count >> 16);
            outgoing[2] = (byte)(count >> 8);
            outgoing[3] = (byte)count;
            Buffer.BlockCopy(payload, 0, outgoing, 4, count);
            Flush();
        }

        public void Flush()
        {
            if (CanSend || !socket.Poll(0, SelectMode.SelectWrite)) return;
            int count = socket.Send(outgoing, sent, outgoing.Length - sent, SocketFlags.None, out var error);
            if (error == SocketError.WouldBlock) return;
            if (error != SocketError.Success || count == 0) throw new IOException("LAN send failed.");
            sent += count;
            if (sent == outgoing.Length)
            {
                outgoing = null;
                sent = 0;
            }
        }

        public bool Receive(int maxPayload, out ArraySegment<byte> payload)
        {
            payload = default;
            if (expected == 0)
            {
                if (!ReadTo(4)) return false;
                int size = (incoming[0] << 24) | (incoming[1] << 16) | (incoming[2] << 8) | incoming[3];
                if (size < 1 || size > maxPayload) throw new IOException("Invalid LAN message size.");
                expected = size + 4;
            }
            if (!ReadTo(expected)) return false;
            payload = new ArraySegment<byte>(incoming, 4, expected - 4);
            received = expected = 0;
            return true;
        }

        bool ReadTo(int length)
        {
            if (received >= length) return true;
            if (!socket.Poll(0, SelectMode.SelectRead)) return false;
            int count = socket.Receive(incoming, received, length - received, SocketFlags.None, out var error);
            if (error == SocketError.WouldBlock) return false;
            if (error != SocketError.Success || count == 0) throw new IOException("LAN peer disconnected.");
            received += count;
            return received == length;
        }

        public void Dispose() => socket.Dispose();

        internal static bool TryLocalAddress(string text, out IPAddress address)
        {
            address = null;
            string[] parts = text.Split('.');
            if (parts.Length != 4) return false;
            var bytes = new byte[4];
            for (int i = 0; i < parts.Length; i++)
                if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out bytes[i])) return false;
            address = new IPAddress(bytes);
            return IsPrivate(address);
        }

        internal static bool IsPrivate(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
        }

        internal static IEnumerable<UnicastIPAddressInformation> LocalInterfaces()
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var endpoint in adapter.GetIPProperties().UnicastAddresses)
                    if (IsPrivate(endpoint.Address)) yield return endpoint;
            }
        }

        internal static bool IsSameSubnet(IPAddress address, UnicastIPAddressInformation local)
        {
            if (!IsPrivate(address)) return false;
            var remote = address.GetAddressBytes();
            var own = local.Address.GetAddressBytes();
            var mask = local.IPv4Mask.GetAddressBytes();
            for (int i = 0; i < 4; i++)
                if ((remote[i] & mask[i]) != (own[i] & mask[i])) return false;
            return true;
        }
    }
}
