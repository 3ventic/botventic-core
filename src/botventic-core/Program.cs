using BotVentic.Twitch;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace BotVentic
{
    public class Program
    {
        private static object _lock = new object();
        private static volatile bool _updatingEmotes = false;

        public static List<EmoteInfo> Emotes { get; private set; }
        public static string BttvTemplate { get; private set; }

        public static void Main(string[] args)
        {
            Console.WriteLine("Starting BotVentic");
            var emotetask = UpdateAllEmotesAsync();
            var bot = new Bot(Config.FromFile("config.json"));
            bot.Run();
            emotetask.Wait();
            Console.WriteLine("Stopped BotVentic");
        }


        /// <summary>
        /// Update the list of all emoticons
        /// </summary>
        public static async Task UpdateAllEmotesAsync()
        {
            lock (_lock)
            {
                if (_updatingEmotes)
                    return;
                else
                    _updatingEmotes = true;
            }
            Console.WriteLine("Loading emotes!");

            if (Emotes == null)
                Emotes = new List<EmoteInfo>();

            List<EmoteInfo> emotes = new List<EmoteInfo>();
            await UpdateFFZEmotes(emotes);
            await UpdateBttvEmotes(emotes);
            await UpdateTwitchEmotes(emotes);
            Emotes = emotes;
            _updatingEmotes = false;

            Console.WriteLine("Emotes acquired!");
        }


        /// <summary>
        /// Update the list of emoticons
        /// </summary>
        private static async Task UpdateTwitchEmotes(List<EmoteInfo> e)
        {
            var emotes = JsonConvert.DeserializeObject<EmoticonImages>(await RequestAsync("https://api.twitch.tv/kraken/chat/emoticon_images"));

            if (emotes == null || emotes.Emotes == null)
            {
                Console.WriteLine("Error loading twitch emotes!");
                return;
            }

            emotes.Emotes.Sort((a, b) =>
            {
                int aSet = 0;
                int bSet = 0;

                if (a != null && a.Set != null)
                    aSet = a.Set ?? 0;
                if (b != null && b.Set != null)
                    bSet = b.Set ?? 0;

                if (aSet == bSet)
                    return 0;

                if (aSet == 0 || aSet == 457)
                    return 1;

                if (bSet == 0 || bSet == 457)
                    return -1;

                return aSet - bSet;
            });

            foreach (var em in emotes.Emotes)
            {
                e.Add(new EmoteInfo(em.Id, em.Code, EmoteType.Twitch, em.Set ?? 0));
            }
        }


        /// <summary>
        /// Update list of betterttv emoticons
        /// </summary>
        private static async Task UpdateBttvEmotes(List<EmoteInfo> e)
        {
            var emotes = JsonConvert.DeserializeObject<BttvEmoticonImages>(await RequestAsync("https://api.betterttv.net/2/emotes"));

            if (emotes == null || emotes.Template == null || emotes.Emotes == null)
            {
                Console.WriteLine("Error loading bttv emotes");
                return;
            }

            BttvTemplate = emotes.Template;

            foreach (var em in emotes.Emotes)
            {
                e.Add(new EmoteInfo(em.Id, em.Code, EmoteType.Bttv));
            }
        }


        /// <summary>
        /// Update the list of FrankerFaceZ emoticons
        /// </summary>
        private static async Task UpdateFFZEmotes(List<EmoteInfo> e)
        {
            var emotes = JsonConvert.DeserializeObject<FFZEmoticonSets>(await RequestAsync("http://api.frankerfacez.com/v1/set/global"));

            if (emotes == null || emotes.Sets == null || emotes.Sets.Values == null)
            {
                Console.WriteLine("Error loading ffz emotes");
                return;
            }

            foreach (FFZEmoticonImages set in emotes.Sets.Values)
            {
                if (set != null && set.Emotes != null)
                {
                    foreach (var em in set.Emotes)
                    {
                        e.Add(new EmoteInfo(em.Id, em.Code, EmoteType.Ffz));
                    }
                }
            }
        }


        /// <summary>
        /// Get URL
        /// </summary>
        /// <param name="uri">URL to request</param>
        /// <returns>Response body</returns>
        public static async Task<string> RequestAsync(string uri)
        {
            WebRequest request = WebRequest.Create(uri);

            // Change our user agent string to something more informative
            request.Headers["User-Agent"] = "BotVentic/2.0";
            try
            {
                string data;
                using (WebResponse response = await request.GetResponseAsync())
                {
                    using (System.IO.Stream stream = response.GetResponseStream())
                    {
                        System.IO.StreamReader reader = new System.IO.StreamReader(stream);
                        data = reader.ReadToEnd();
                    }
                }
                return data;
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
