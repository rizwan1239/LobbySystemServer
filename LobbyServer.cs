using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Network;
using Network.Enums;
using Network.Extensions;
using Network.Converter;
using LobbySystem;
using LobbySystem.Packets;

namespace LobbySystemServer
{
    internal class LobbyServer
    {
        private ServerConnectionContainer? ServerContainer;
        private List<Lobby> Lobbies = new List<Lobby>();

        internal async void Start()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(ConsoleUtil.DrawInConsoleBox("Server is Online"));
            //1. Start to listen on a port
            ServerContainer = ConnectionFactory.CreateSecureServerConnectionContainer(1234, start: true);
            await ServerContainer.Start();

            //2. Apply optional settings.

            #region Optional settings

            ServerContainer.ConnectionLost += (a, b, c) => Console.WriteLine($"{ServerContainer.Count} {b.ToString()} Connection lost {a.IPRemoteEndPoint.Port}. Reason {c.ToString()}");
            ServerContainer.ConnectionEstablished += connectionEstablished;
            ServerContainer.AllowUDPConnections = true;
            ServerContainer.UDPConnectionLimit = 2;

            #endregion Optional settings

            await ServerContainer.Start();
            Console.ReadLine();
        }

        private void connectionEstablished(Connection connection, ConnectionType type)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            string message = $"{connection.IPRemoteEndPoint.Address.MapToIPv4()}:{connection.IPRemoteEndPoint.Port} connected.";
            Console.WriteLine(message);

            connection.ConnectionClosed += OnConnectionClosed;
            connection.RegisterStaticPacketHandler<LobbyCreationRequest>(OnLobbyCreationRequest);
            connection.RegisterStaticPacketHandler<LobbyJoinRequest>(OnLobbyJoinRequest);
            connection.RegisterStaticPacketHandler<MatchJoinRequest>(OnMatchJoinRequest);
        }

        private void OnConnectionClosed(CloseReason reason, Connection connection)
        {
            //CheckLobby(connection);
            connection.ConnectionClosed -= OnConnectionClosed;
            Console.ForegroundColor = ConsoleColor.Red;
            string message = $"{connection.IPRemoteEndPoint.Address.MapToIPv4()}:{connection.IPRemoteEndPoint.Port} gone offline. Reason: {reason}";
            Console.WriteLine(message);
        }

        private void OnLobbyCreationRequest(LobbyCreationRequest packet, Connection connection)
        {
            LobbyPlayer lobbyPlayer = new LobbyPlayer();
            lobbyPlayer.ID = packet.UserID;
            lobbyPlayer.IsLeader = true;
            lobbyPlayer.Name = packet.Name;
            lobbyPlayer.IP = connection.IPRemoteEndPoint.Address.MapToIPv4().ToString();

            Lobby newLobby = new Lobby();
            newLobby.LobbyID = RandomHex.Generate();
            newLobby.LobbyPlayers?.Add(lobbyPlayer);

            Lobbies?.Add(newLobby);

            var tempLobby = Utils.Clone(newLobby);
            foreach (LobbyPlayer player in tempLobby.LobbyPlayers)
                player.IP = null;

            var jsonData = JsonConvert.SerializeObject(tempLobby);
            connection.Send(new LobbyCreationResponse(false, jsonData, packet));

            Console.ForegroundColor = ConsoleColor.Yellow;
            string message = $"Lobby {newLobby.LobbyID} created by {lobbyPlayer.Name}.";
            Console.WriteLine(ConsoleUtil.DrawInConsoleBox(message));
        }

        private void OnLobbyJoinRequest(LobbyJoinRequest packet, Connection connection)
        {
            LobbyPlayer lobbyPlayer = new LobbyPlayer();
            lobbyPlayer.ID = packet.UserID;
            lobbyPlayer.IsLeader = false;
            lobbyPlayer.Name = packet.Name;
            lobbyPlayer.IP = connection.IPRemoteEndPoint.Address.MapToIPv4().ToString();

            var lobby = Lobbies.Find(x => x.LobbyID == packet.LobbyID);
            lobby.LobbyPlayers.Add(lobbyPlayer);

            var jsonData = JsonConvert.SerializeObject(lobby);
            connection.Send(new LobbyJoinResponse(true, jsonData, packet));
            SendUpdateLobby(lobby);

            Console.ForegroundColor = ConsoleColor.Yellow;
            string message = $"{lobbyPlayer.Name} joined Lobby {lobby.LobbyID}.";
            Console.WriteLine(ConsoleUtil.DrawInConsoleBox(message));
        }

        private void SendUpdateLobby(Lobby lobby)
        {
            var jsonData = JsonConvert.SerializeObject(lobby);
            ServerContainer?.TCP_Connections.ForEach(c =>
            {
                if (c.IsAlive)
                {
                    if (c.IPRemoteEndPoint.Address.MapToIPv4().ToString() == lobby.LobbyPlayers[0].IP)
                    {
                        c.Send(new LobbyUpdateRequest(true, jsonData));
                    }
                }
            });
        }

        private void OnMatchJoinRequest(MatchJoinRequest packet, Connection connection)
        {
            var lobby = Lobbies.Find(x => x.LobbyID == packet.LobbyID);
            ServerContainer?.TCP_Connections.ForEach(c =>
            {
                if (c.IsAlive)
                {
                    string IP = c.IPRemoteEndPoint.Address.MapToIPv4().ToString();
                    foreach (LobbyPlayer player in lobby.LobbyPlayers)
                    {
                        if (IP == player.IP && IP != connection.IPRemoteEndPoint.Address.MapToIPv4().ToString())
                        {
                            c.Send(new MatchJoinRequest(packet.RoomName, lobby.LobbyID));
                        }
                    }
                }
            });

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            string message = $"Players with Lobby ID {lobby.LobbyID} entered match {packet.RoomName}.";
            Console.WriteLine(ConsoleUtil.DrawInConsoleBox(message));
        }

        void CheckLobby(Connection connection)
        {
            var lobby = Lobbies.Find(x => x.LobbyPlayers[0].IP == connection.IPRemoteEndPoint.Address.MapToIPv4().ToString());

            if (lobby?.LobbyPlayers.Count <= 0)
                Lobbies.Remove(lobby);
            else
            {
                var myPlayer = lobby?.LobbyPlayers.Find(x => x.IP == connection.IPRemoteEndPoint.Address.MapToIPv4().ToString());

                lobby.LobbyPlayers.Remove(myPlayer);
                lobby.LobbyPlayers[0].IsLeader = true;
                SendUpdateLobby(lobby);
            }
        }
    }
}
