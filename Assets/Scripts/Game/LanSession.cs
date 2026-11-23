using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rovernauts
{
    public enum CrewMode { Solo, Local, LanDriver, LanGunner }

    internal sealed class LanSession : IDisposable
    {
        internal enum Page { Closed, Menu, Host, Join, Connecting, Guest }
        static readonly byte[] Handshake = { (byte)'R', (byte)'V', (byte)'R', 1 };
        const float Timeout = 5f;
        readonly LanSnapshot outgoing = new LanSnapshot();
        readonly LanWorld world = new LanWorld();
        readonly byte[] input = new byte[1];
        TcpListener listener;
        Socket connecting;
        Task pendingConnect;
        LanConnection peer;
        UnicastIPAddressInformation[] interfaces;
        bool loopback, ready;
        float lastReceived, nextSend;
        byte lastButtons;

        internal Page Screen { get; private set; }
        internal string Address { get; private set; } = "";
        internal string HostAddresses { get; private set; } = "";
        internal string Status { get; private set; }
        internal bool Ready => ready;
        internal byte GunnerButtons { get; private set; }
        internal LanSnapshot Latest => world.Latest;

        internal LanSession(string status) => Status = status ?? "";

        internal bool Tick(GameManager gm, Keyboard kb)
        {
            if (Screen == Page.Closed)
            {
                if (gm.State != GameState.Menu || kb?.lKey.wasPressedThisFrame != true) return false;
                Screen = Page.Menu;
                Status = "";
                return true;
            }

            try
            {
                switch (Screen)
                {
                    case Page.Menu:
                        if (kb?.escapeKey.wasPressedThisFrame == true) Screen = Page.Closed;
                        else if (kb?.hKey.wasPressedThisFrame == true) Host(gm);
                        else if (kb?.jKey.wasPressedThisFrame == true)
                        {
                            Screen = Page.Join;
                            Status = "";
                        }
                        break;
                    case Page.Join:
                        if (kb?.escapeKey.wasPressedThisFrame == true) Screen = Page.Menu;
                        else if (kb != null)
                        {
                            EditAddress(kb);
                            if (Confirm(kb)) Join(gm, Address);
                        }
                        break;
                    case Page.Connecting:
                        if (kb?.escapeKey.wasPressedThisFrame == true) CloseLobby(gm);
                        else PollConnection();
                        break;
                    case Page.Host:
                        if (gm.State == GameState.Menu && kb?.escapeKey.wasPressedThisFrame == true)
                        {
                            CloseLobby(gm);
                            break;
                        }
                        TickHost(gm);
                        if (gm.State != GameState.Menu) return false;
                        if (ready)
                        {
                            if (kb?.eKey.wasPressedThisFrame == true) gm.StartExpedition();
                            else if (Confirm(kb)) gm.StartRun();
                        }
                        break;
                    case Page.Guest:
                        if (kb?.f8Key.wasPressedThisFrame == true
                            || (gm.State == GameState.Menu && kb?.escapeKey.wasPressedThisFrame == true)
                            || ((gm.State is GameState.Paused or GameState.Victory or GameState.GameOver)
                                && (kb?.rKey.wasPressedThisFrame == true || Confirm(kb))))
                        {
                            gm.Restart();
                            break;
                        }
                        TickGuest(gm, kb);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ArgumentException or NetworkInformationException)
            {
                Debug.LogWarning("[LAN] " + ex.Message);
                if (gm.State != GameState.Menu)
                {
                    gm.Restart("LAN connection ended. Start a new session to reconnect.");
                }
                else if (Screen == Page.Host && listener != null)
                {
                    DropPeer();
                    Status = "Gunner disconnected. Waiting for another player.";
                }
                else
                {
                    bool joining = Screen is Page.Connecting or Page.Guest;
                    CloseLobby(gm);
                    Screen = joining ? Page.Join : Page.Menu;
                    Status = joining ? "Could not connect. Check the host address and firewall." : "Could not host a LAN session.";
                }
            }
            return true;
        }

        internal void Host(GameManager gm, bool testLoopback = false)
        {
            loopback = testLoopback;
            interfaces = loopback ? Array.Empty<UnicastIPAddressInformation>() : LanConnection.LocalInterfaces().ToArray();
            if (!loopback && interfaces.Length == 0)
            {
                Status = "Connect to a private IPv4 network first.";
                return;
            }
            listener = new TcpListener(loopback ? IPAddress.Loopback : IPAddress.Any, LanConnection.Port);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start(1);
            HostAddresses = loopback ? "127.0.0.1" : string.Join("  /  ", interfaces.Select(i => i.Address.ToString()).Distinct());
            gm.Mode = CrewMode.LanDriver;
            Screen = Page.Host;
            Status = "Waiting for a gunner.";
        }

        internal void Join(GameManager gm, string address, bool testLoopback = false)
        {
            IPAddress remote;
            if (testLoopback)
            {
                remote = IPAddress.Loopback;
            }
            else if (!LanConnection.TryLocalAddress(address, out remote)
                || !LanConnection.LocalInterfaces().Any(i => LanConnection.IsSameSubnet(remote, i)))
            {
                Status = "Enter a private IPv4 address on your local subnet.";
                return;
            }

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            connecting = socket;
            // The Windows Mono player does not support the socket selector used by ConnectAsync.
            pendingConnect = Task.Run(() => socket.Connect(remote, LanConnection.Port));
            lastReceived = Time.unscaledTime;
            gm.Mode = CrewMode.LanGunner;
            Screen = Page.Connecting;
            Status = "Connecting...";
        }

        void PollConnection()
        {
            if (!pendingConnect.IsCompleted)
            {
                CheckTimeout();
                return;
            }
            pendingConnect.GetAwaiter().GetResult();
            peer = new LanConnection(connecting);
            connecting = null;
            pendingConnect = null;
            peer.Send(Handshake);
            lastReceived = Time.unscaledTime;
            Screen = Page.Guest;
            Status = "Waiting for the host.";
        }

        void TickHost(GameManager gm)
        {
            if (peer == null && listener.Pending())
            {
                var socket = listener.AcceptSocket();
                var remoteAddress = ((IPEndPoint)socket.RemoteEndPoint).Address;
                var localAddress = ((IPEndPoint)socket.LocalEndPoint).Address;
                if (loopback ? IPAddress.IsLoopback(remoteAddress)
                    : interfaces.Any(i => i.Address.Equals(localAddress) && LanConnection.IsSameSubnet(remoteAddress, i)))
                {
                    peer = new LanConnection(socket);
                    lastReceived = Time.unscaledTime;
                }
                else socket.Dispose();
            }
            if (peer == null) return;

            peer.Flush();
            for (int i = 0; i < 8 && peer.Receive(ready ? 1 : Handshake.Length, out var data); i++)
            {
                if (!ready)
                {
                    if (!data.SequenceEqual(Handshake)) throw new IOException("LAN version mismatch.");
                    ready = true;
                    Status = "Gunner connected. Choose a mission.";
                }
                else
                {
                    if (data.Count != 1 || data.Array[data.Offset] > 31) throw new IOException("Invalid gunner input.");
                    GunnerButtons = data.Array[data.Offset];
                }
                lastReceived = Time.unscaledTime;
            }
            CheckTimeout();
            if (!ready || !peer.CanSend || Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + LanWorld.Interval;
            outgoing.Capture(gm);
            peer.Send(Encoding.UTF8.GetBytes(JsonUtility.ToJson(outgoing)));
            outgoing.cues.Clear();
        }

        void TickGuest(GameManager gm, Keyboard kb)
        {
            peer.Flush();
            LanSnapshot newest = null;
            for (int i = 0; i < 8 && peer.Receive(LanConnection.MaxFrame, out var data); i++)
            {
                var snapshot = JsonUtility.FromJson<LanSnapshot>(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count));
                if (snapshot == null || snapshot.version != Handshake[3] || snapshot.enemies == null
                    || snapshot.shots == null || snapshot.meteors == null || snapshot.cues == null
                    || snapshot.upgrades == null || snapshot.offers == null)
                    throw new IOException("Invalid LAN snapshot or version.");
                if (newest != null) snapshot.cues.InsertRange(0, newest.cues);
                newest = snapshot;
                lastReceived = Time.unscaledTime;
            }
            CheckTimeout();
            if (newest != null) world.Receive(gm, newest);
            world.Tick(gm.Rover);

            byte buttons = gm.State == GameState.Playing ? Turret.ReadButtons(kb, false) : (byte)0;
            if (!peer.CanSend || (buttons == lastButtons && Time.unscaledTime < nextSend)) return;
            input[0] = lastButtons = buttons;
            nextSend = Time.unscaledTime + LanWorld.Interval;
            peer.Send(input);
        }

        void CheckTimeout()
        {
            if (Time.unscaledTime - lastReceived > Timeout) throw new IOException("LAN connection timed out.");
        }

        static bool Confirm(Keyboard kb) => kb != null
            && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);

        void EditAddress(Keyboard kb)
        {
            if (kb.backspaceKey.wasPressedThisFrame && Address.Length > 0) Address = Address.Substring(0, Address.Length - 1);
            if (Address.Length >= 15) return;
            if (kb.periodKey.wasPressedThisFrame || kb.numpadPeriodKey.wasPressedThisFrame)
                Address += ".";
            else
            {
                for (int i = 0; i <= 9; i++)
                {
                    var top = i == 0 ? Key.Digit0 : (Key)((int)Key.Digit1 + i - 1);
                    var numpad = (Key)((int)Key.Numpad0 + i);
                    if (!kb[top].wasPressedThisFrame && !kb[numpad].wasPressedThisFrame) continue;
                    Address += (char)('0' + i);
                    break;
                }
            }
        }

        internal void AddCue(string name, float volume)
        {
            if (ready && outgoing.cues.Count < 32)
                outgoing.cues.Add(new LanSnapshot.Cue { name = name, volume = volume });
        }

        void DropPeer()
        {
            peer?.Dispose();
            peer = null;
            ready = false;
            GunnerButtons = lastButtons = 0;
            nextSend = 0f;
            outgoing.cues.Clear();
        }

        void CloseLobby(GameManager gm)
        {
            Dispose();
            gm.Mode = CrewMode.Solo;
            Screen = Page.Menu;
            Status = "";
        }

        public void Dispose()
        {
            DropPeer();
            listener?.Stop();
            listener = null;
            connecting?.Dispose();
            connecting = null;
            // Read any connection fault even if the user cancelled while the connection was pending.
            pendingConnect?.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            pendingConnect = null;
        }
    }
}
