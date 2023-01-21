namespace LobbySystemServer
{
    public static class Program
    {
        public static void Main()
        {
            LobbyServer lobby = new LobbyServer();
            lobby.Start();
        }
    }
}
