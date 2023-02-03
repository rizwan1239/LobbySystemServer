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
            ConsoleUtil.ShowBoxLog("\nServer is Online", ConsoleColor.Cyan);
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

        /// <summary>
        /// Called when client's connection is establishes. Registers packet handlers.
        /// </summary>
        /// <param name="connection">Connection Data</param>
        /// <param name="type">Connection Type</param>
        private void connectionEstablished(Connection connection, ConnectionType type)
        {
            string message = $"{connection.IPRemoteEndPoint.Address.MapToIPv4()}:{connection.IPRemoteEndPoint.Port} connected.";
            ConsoleUtil.ShowLog(message, ConsoleColor.Green);

            connection.ConnectionClosed += OnConnectionClosed;
            connection.RegisterStaticPacketHandler<LobbyCreationRequest>(OnLobbyCreationRequest);
            connection.RegisterStaticPacketHandler<LobbyJoinRequest>(OnLobbyJoinRequest);
            connection.RegisterStaticPacketHandler<LobbyLeaveRequest>(OnLobbyLeaveRequest);
            connection.RegisterStaticPacketHandler<MatchJoinRequest>(OnMatchJoinRequest);
            connection.RegisterStaticPacketHandler<MatchLeaveWithPartyRequest>(OnMatchLeaveWithPartyRequest);
            connection.RegisterStaticPacketHandler<MatchmakingRequest>(OnMatchmakingRequest);
        }

        /// <summary>
        /// Called when connection is closed on client's side
        /// </summary>
        /// <param name="reason"></param>
        /// <param name="connection"></param>
        private void OnConnectionClosed(CloseReason reason, Connection connection)
        {
            //CheckLobby(connection);
            connection.ConnectionClosed -= OnConnectionClosed;
            Console.ForegroundColor = ConsoleColor.Red;
            string message = $"{connection.IPRemoteEndPoint.Address.MapToIPv4()}:{connection.IPRemoteEndPoint.Port} gone offline. Reason: {reason}";
            Console.WriteLine(message);
        }

        /// <summary>
        /// Called when a player sends request to create a lobby
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="connection"></param>
        private void OnLobbyCreationRequest(LobbyCreationRequest packet, Connection connection)
        {
            LobbyPlayer lobbyPlayer = new LobbyPlayer();
            lobbyPlayer.ID = packet.UserID;
            lobbyPlayer.IsLeader = true;
            lobbyPlayer.Name = packet.Name;
            lobbyPlayer.IP = connection.IPRemoteEndPoint.Address.MapToIPv4().ToString();

            Lobby newLobby = new Lobby();
            newLobby.LobbyID = RandomHex.Generate();
            newLobby.LobbyRegion = packet.Region;
            newLobby.LobbyPlayers?.Add(lobbyPlayer);

            Lobbies?.Add(newLobby);

            var jsonData = JsonConvert.SerializeObject(NoIPLobby(newLobby));
            connection.Send(new LobbyCreationResponse(false, jsonData, packet));

            string message = $"Lobby {newLobby.LobbyID} created by {lobbyPlayer.Name}.";
            ConsoleUtil.ShowBoxLog(message, ConsoleColor.Yellow);
        }

        /// <summary>
        /// Called when player requests server to join a lobby
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="connection"></param>
        private void OnLobbyJoinRequest(LobbyJoinRequest packet, Connection connection)
        {
            var lobby = Lobbies.Find(x => x.LobbyID == packet.LobbyID);
            if (lobby.LobbyRegion != packet.Region)
            {
                string errorMessage = "Region Conflict.";
                connection.Send(new LobbyJoinResponse(false, errorMessage, packet));
                return;
            }

            LobbyPlayer lobbyPlayer = new LobbyPlayer();
            lobbyPlayer.ID = packet.UserID;
            lobbyPlayer.IsLeader = false;
            lobbyPlayer.Name = packet.Name;
            lobbyPlayer.IP = connection.IPRemoteEndPoint.Address.MapToIPv4().ToString();

            lobby.LobbyPlayers.Add(lobbyPlayer);

            var jsonData = JsonConvert.SerializeObject(NoIPLobby(lobby));
            connection.Send(new LobbyJoinResponse(true, jsonData, packet));
            SendUpdateLobby(lobby);

            string message = $"{lobbyPlayer.Name} joined Lobby {lobby.LobbyID}.";
            ConsoleUtil.ShowBoxLog(message, ConsoleColor.Yellow);
        }

        /// <summary>
        /// Called when a player leaves the lobby
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="connection"></param>
        private void OnLobbyLeaveRequest(LobbyLeaveRequest packet, Connection connection)
        {
            Console.WriteLine(Lobbies.Count);
            var lobby = Lobbies.Find(x => x.LobbyID == packet.LobbyID);
            CheckLobby(lobby, packet.UserID);
            Console.WriteLine(Lobbies.Count);
        }

        /// <summary>
        /// Sends lobby update to other players in the lobby when there's a change (other player left/joined).
        /// </summary>
        /// <param name="lobby"></param>
        private void SendUpdateLobby(Lobby lobby)
        {
            var jsonData = JsonConvert.SerializeObject(NoIPLobby(lobby));
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

        /// <summary>
        /// Called when party leader requests server to take other lobby players to match
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="connection"></param>
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

            string message = $"Players with Lobby ID {lobby.LobbyID} entered match {packet.RoomName}.";
            ConsoleUtil.ShowBoxLog(message, ConsoleColor.DarkYellow);
        }

        /// <summary>
        /// Called when lobby leader requests server to leave match with party
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="connection"></param>
        private void OnMatchLeaveWithPartyRequest(MatchLeaveWithPartyRequest packet, Connection connection)
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
                            c.Send(new MatchLeaveWithPartyRequest(packet.RoomName, lobby.LobbyID));
                        }
                    }
                }
            });

            string message = $"Players with Lobby ID {lobby.LobbyID} left match {packet.RoomName}.";
            ConsoleUtil.ShowBoxLog(message, ConsoleColor.DarkYellow);
        }

        /// <summary>
        /// Called when lobby leader starts matchmaking
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="connection"></param>
        private void OnMatchmakingRequest(MatchmakingRequest packet, Connection connection)
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
                            c.Send(new MatchmakingRequest(lobby.LobbyID, packet.LeaderName));
                        }
                    }
                }
            });

            string message = $"{packet.LeaderName} started matchmaking for Lobby {lobby.LobbyID}.";
            ConsoleUtil.ShowBoxLog(message, ConsoleColor.DarkYellow);
        }

        /// <summary>
        /// Checks whether to re-assign lobby leader or remove the lobby if a player leaves or disconnects.
        /// </summary>
        /// <param name="lobby"></param>
        /// <param name="UserID"></param>
        void CheckLobby(Lobby lobby, int UserID)
        {
            var lobbyPlayer = lobby.LobbyPlayers.Find(x => x.ID == UserID);
            lobby.LobbyPlayers.Remove(lobbyPlayer);

            if (!lobby.LobbyPlayers.Any())
            {
                Lobbies.Remove(lobby);
            }
            else
            {
                lobby.LobbyPlayers.First().IsLeader = true;
                SendUpdateLobby(lobby);
            }
        }

        /// <summary>
        /// Lobby contains IPs of lobby players on server. This method sets them to null for security purposes before sending it to clients/lobby players.
        /// </summary>
        /// <param name="OriginalLobby"></param>
        /// <returns></returns>
        Lobby NoIPLobby(Lobby OriginalLobby)
        {
            var noIPLobby = Utils.Clone(OriginalLobby);
            foreach (LobbyPlayer player in noIPLobby.LobbyPlayers)
                player.IP = null;
            return noIPLobby;
        }
    }
}
