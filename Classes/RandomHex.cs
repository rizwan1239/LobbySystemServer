using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LobbySystemServer
{
    internal static class RandomHex
    {
        public static string Generate()
        {
            Random random = new Random();
            var bytes = new byte[4];
            random.NextBytes(bytes);

            var hexArray = Array.ConvertAll(bytes, x => x.ToString("X2"));
            var hexStr = string.Concat(hexArray);

            return hexStr;
        }
    }
}
