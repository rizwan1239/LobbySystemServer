using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LobbySystem
{
    [System.Serializable]
    public class Lobby
    {
        public string? LobbyID { get; set; }
        public string? LobbyRegion;
        public List<LobbyPlayer> LobbyPlayers = new List<LobbyPlayer>();
    }

    [System.Serializable]
    public class LobbyPlayer
    {
        public string? Name { get; set; }
        public int? ID { get; set; }
        public bool? IsLeader { get; set; }
        public string? IP { get; set; }
    }

}
