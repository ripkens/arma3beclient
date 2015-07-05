﻿/* * * * * * * * * * * * * * * * * * * * * * * * * * * * * *
 * BattleNET v1.3 - BattlEye Library and Client            *
 *                                                         *
 *  Copyright (C) 2013 by it's authors.                    *
 *  Some rights reserved. See license.txt, authors.txt.    *
 * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BattleNET
{
    public class BattlEyeClient
    {
        private BattlEyeDisconnectionType? disconnectionType;
        private bool keepRunning;
        private BattlEyeLoginCredentials loginCredentials;
        private SortedDictionary<int, string[]> packetQueue;
        private DateTime packetReceived;
        private DateTime packetSent;
        private int sequenceNumber;
        private Socket socket;

        public BattlEyeClient(BattlEyeLoginCredentials loginCredentials)
        {
            this.loginCredentials = loginCredentials;
        }

        public bool Connected
        {
            get { return socket != null && socket.Connected; }
        }

        public bool ReconnectOnPacketLoss { get; set; }

        public int CommandQueue
        {
            get { return packetQueue.Count; }
        }

        public BattlEyeConnectionResult Connect()
        {
            packetSent = DateTime.Now;
            packetReceived = DateTime.Now;

            sequenceNumber = 0;
            packetQueue = new SortedDictionary<int, string[]>();
            keepRunning = true;

            var remoteEp = new IPEndPoint(loginCredentials.Host, loginCredentials.Port);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveBufferSize = int.MaxValue,
                ReceiveTimeout = 5000
            };

            try
            {
                socket.Connect(remoteEp);

                if (SendLoginPacket(loginCredentials.Password) == BattlEyeCommandResult.Error)
                    return BattlEyeConnectionResult.ConnectionFailed;

                var bytesReceived = new byte[4096];
                var bytes = 0;

                bytes = socket.Receive(bytesReceived, bytesReceived.Length, 0);

                if (bytesReceived[7] == 0x00)
                {
                    if (bytesReceived[8] == 0x01)
                    {
                        OnConnect(loginCredentials, BattlEyeConnectionResult.Success);

                        Receive();
                    }
                    else
                    {
                        OnConnect(loginCredentials, BattlEyeConnectionResult.InvalidLogin);
                        return BattlEyeConnectionResult.InvalidLogin;
                    }
                }
            }
            catch
            {
                if (disconnectionType == BattlEyeDisconnectionType.ConnectionLost)
                {
                    Disconnect(BattlEyeDisconnectionType.ConnectionLost);
                    Connect();
                    return BattlEyeConnectionResult.ConnectionFailed;
                }
                OnConnect(loginCredentials, BattlEyeConnectionResult.ConnectionFailed);
                return BattlEyeConnectionResult.ConnectionFailed;
            }

            return BattlEyeConnectionResult.Success;
        }

        private BattlEyeCommandResult SendLoginPacket(string command)
        {
            try
            {
                if (!socket.Connected)
                    return BattlEyeCommandResult.NotConnected;

                var packet = ConstructPacket(BattlEyePacketType.Login, 0, command);
                socket.Send(packet);

                packetSent = DateTime.Now;
            }
            catch
            {
                return BattlEyeCommandResult.Error;
            }

            return BattlEyeCommandResult.Success;
        }

        private BattlEyeCommandResult SendAcknowledgePacket(string command)
        {
            try
            {
                if (!socket.Connected)
                    return BattlEyeCommandResult.NotConnected;

                var packet = ConstructPacket(BattlEyePacketType.Acknowledge, 0, command);
                socket.Send(packet);

                packetSent = DateTime.Now;
            }
            catch
            {
                return BattlEyeCommandResult.Error;
            }

            return BattlEyeCommandResult.Success;
        }

        public int SendCommand(string command, bool log = true)
        {
            return SendCommandPacket(command, log);
        }

        private int SendCommandPacket(string command, bool log = true)
        {
            var packetID = sequenceNumber;

            try
            {
                if (!socket.Connected)
                    return 256;

                var packet = ConstructPacket(BattlEyePacketType.Command, sequenceNumber, command);

                packetSent = DateTime.Now;

                if (log)
                {
                    packetQueue.Add(sequenceNumber, new[] {command, packetSent.ToString()});
                }

                socket.Send(packet);

                sequenceNumber = (sequenceNumber == 255) ? 0 : sequenceNumber + 1;
            }
            catch
            {
                return 256;
            }

            return packetID;
        }

        public int SendCommand(BattlEyeCommand command, string parameters = "")
        {
            return SendCommandPacket(command, parameters);
        }

        private int SendCommandPacket(BattlEyeCommand command, string parameters = "")
        {
            var packetID = sequenceNumber;

            try
            {
                if (!socket.Connected)
                    return 256;

                var packet = ConstructPacket(BattlEyePacketType.Command, sequenceNumber,
                    Helpers.StringValueOf(command) + parameters);

                packetSent = DateTime.Now;

                packetQueue.Add(sequenceNumber,
                    new[] {Helpers.StringValueOf(command) + parameters, packetSent.ToString()});

                socket.Send(packet);

                sequenceNumber = (sequenceNumber == 255) ? 0 : sequenceNumber + 1;
            }
            catch
            {
                return 256;
            }

            return packetID;
        }

        private byte[] ConstructPacket(BattlEyePacketType packetType, int sequenceNumber, string command)
        {
            string type;

            switch (packetType)
            {
                case BattlEyePacketType.Login:
                    type = Helpers.Hex2Ascii("FF00");
                    break;
                case BattlEyePacketType.Command:
                    type = Helpers.Hex2Ascii("FF01");
                    break;
                case BattlEyePacketType.Acknowledge:
                    type = Helpers.Hex2Ascii("FF02");
                    break;
                default:
                    return new byte[] {};
            }

            if (packetType != BattlEyePacketType.Acknowledge)
            {
                if (command != null) command = Encoding.GetEncoding(1252).GetString(Encoding.UTF8.GetBytes(command));
            }

            var count = Helpers.Bytes2String(new[] {(byte) sequenceNumber});

            var byteArray =
                new CRC32().ComputeHash(
                    Helpers.String2Bytes(type + ((packetType != BattlEyePacketType.Command) ? "" : count) + command));

            var hash =
                new string(
                    Helpers.Hex2Ascii(BitConverter.ToString(byteArray).Replace("-", ""))
                        .ToCharArray()
                        .Reverse()
                        .ToArray());

            var packet = "BE" + hash + type + ((packetType != BattlEyePacketType.Command) ? "" : count) + command;

            return Helpers.String2Bytes(packet);
        }

        public void Disconnect()
        {
            keepRunning = false;

            if (socket != null && socket.Connected)
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }

            OnDisconnect(loginCredentials, BattlEyeDisconnectionType.Manual);
        }

        private void Disconnect(BattlEyeDisconnectionType? disconnectionType)
        {
            if (disconnectionType == BattlEyeDisconnectionType.ConnectionLost)
                this.disconnectionType = BattlEyeDisconnectionType.ConnectionLost;

            keepRunning = false;

            if (socket != null && socket.Connected)
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }

            if (disconnectionType != null)
                OnDisconnect(loginCredentials, disconnectionType);
        }

        private void Receive()
        {
            var state = new StateObject();
            state.WorkSocket = socket;

            disconnectionType = null;

            socket.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback, state);

            new Thread(delegate()
            {
                while (socket.Connected && keepRunning)
                {
                    var timeoutClient = (int) (DateTime.Now - packetSent).TotalSeconds;
                    var timeoutServer = (int) (DateTime.Now - packetReceived).TotalSeconds;

                    if (timeoutClient >= 5)
                    {
                        if (timeoutServer >= 20)
                        {
                            Disconnect(BattlEyeDisconnectionType.ConnectionLost);
                            keepRunning = true;
                        }
                        else
                        {
                            if (packetQueue.Count == 0)
                            {
                                SendCommandPacket(null, false);
                            }
                        }
                    }

                    if (socket.Connected && packetQueue.Count > 0 && socket.Available == 0)
                    {
                        try
                        {
                            var key = packetQueue.First().Key;
                            var value = packetQueue[key][0];
                            var date = DateTime.Parse(packetQueue[key][1]);
                            var timeDiff = (int) (DateTime.Now - date).TotalSeconds;

                            if (timeDiff > 5)
                            {
                                SendCommandPacket(value, false);
                                packetQueue.Remove(key);
                            }
                        }
                        catch
                        {
                            // Prevent possible crash when packet is received at the same moment it's trying to resend it.
                        }
                    }

                    Thread.Sleep(1000);
                }

                if (!socket.Connected)
                {
                    if (ReconnectOnPacketLoss && keepRunning)
                    {
                        Connect();
                    }
                    else if (!keepRunning)
                    {
                        //let the thread finish without further action
                    }
                    else
                    {
                        OnDisconnect(loginCredentials, BattlEyeDisconnectionType.ConnectionLost);
                    }
                }
            }) {IsBackground = true}.Start();
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                var state = (StateObject) ar.AsyncState;
                var client = state.WorkSocket;

                // this method can be called from the middle of a .Disconnect() call
                // test with Debug > Exception > CLR exs on
                if (!client.Connected)
                {
                    return;
                }

                var bytesRead = client.EndReceive(ar);

                if (state.Buffer[7] == 0x02)
                {
                    SendAcknowledgePacket(Helpers.Bytes2String(new[] {state.Buffer[8]}));
                    OnBattlEyeMessage(Helpers.Bytes2String(state.Buffer, 9, bytesRead - 9), 256);
                }
                else if (state.Buffer[7] == 0x01)
                {
                    if (bytesRead > 9)
                    {
                        if (state.Buffer[7] == 0x01 && state.Buffer[9] == 0x00)
                        {
                            if (state.Buffer[11] == 0)
                            {
                                state.PacketsTodo = state.Buffer[10];
                            }

                            if (state.PacketsTodo > 0)
                            {
                                state.Message.Append(Helpers.Bytes2String(state.Buffer, 12, bytesRead - 12));
                                state.PacketsTodo--;
                            }

                            if (state.PacketsTodo == 0)
                            {
                                OnBattlEyeMessage(state.Message.ToString(), state.Buffer[8]);
                                state.Message = new StringBuilder();
                                state.PacketsTodo = 0;
                            }
                        }
                        else
                        {
                            // Temporary fix to avoid infinite loops with multi-packet server messages
                            state.Message = new StringBuilder();
                            state.PacketsTodo = 0;

                            OnBattlEyeMessage(Helpers.Bytes2String(state.Buffer, 9, bytesRead - 9), state.Buffer[8]);
                        }
                    }

                    if (packetQueue.ContainsKey(state.Buffer[8]))
                    {
                        packetQueue.Remove(state.Buffer[8]);
                    }
                }

                packetReceived = DateTime.Now;

                client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, ReceiveCallback,
                    state);
            }
            catch
            {
                // do nothing
            }
        }

        private void OnBattlEyeMessage(string message, int id)
        {
            if (BattlEyeMessageReceived != null)
                BattlEyeMessageReceived(new BattlEyeMessageEventArgs(message, id));
        }

        private void OnConnect(BattlEyeLoginCredentials loginDetails, BattlEyeConnectionResult connectionResult)
        {
            if (connectionResult == BattlEyeConnectionResult.ConnectionFailed ||
                connectionResult == BattlEyeConnectionResult.InvalidLogin)
                Disconnect(null);

            if (BattlEyeConnected != null)
                BattlEyeConnected(new BattlEyeConnectEventArgs(loginDetails, connectionResult));
        }

        private void OnDisconnect(BattlEyeLoginCredentials loginDetails, BattlEyeDisconnectionType? disconnectionType)
        {
            if (BattlEyeDisconnected != null)
                BattlEyeDisconnected(new BattlEyeDisconnectEventArgs(loginDetails, disconnectionType));
        }

        public event BattlEyeMessageEventHandler BattlEyeMessageReceived;
        public event BattlEyeConnectEventHandler BattlEyeConnected;
        public event BattlEyeDisconnectEventHandler BattlEyeDisconnected;
    }

    public class StateObject
    {
        public const int BufferSize = 2048;
        public byte[] Buffer = new byte[BufferSize];
        public StringBuilder Message = new StringBuilder();
        public int PacketsTodo;
        public Socket WorkSocket;
    }
}