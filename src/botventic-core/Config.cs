using System.IO;
using Newtonsoft.Json;

namespace BotVentic
{
    public class Config
    {
        [JsonProperty("auth_url")]
        public string AuthUrl { get; set; } = "https://discordapp.com/oauth2/authorize?client_id=174449568304332800&scope=bot&permissions=19456";

        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("editthreshold")]
        public int EditThreshold { get; set; } = 1;

        [JsonProperty("editmax")]
        public int EditMax { get; set; } = 10;

        public static Config FromFile(string filepath)
        {
            if (File.Exists(filepath))
            {
                using (var sr = File.OpenText(filepath))
                {
                    return JsonConvert.DeserializeObject<Config>(sr.ReadToEnd());
                }
            }
            return null;
        }
    }
}