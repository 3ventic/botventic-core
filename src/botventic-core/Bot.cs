﻿using Discord;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BotVentic
{
    public class Bot
    {
        private Config _config;
        private DiscordClient _client;
        private ConcurrentQueue<Message[]> _replies = new ConcurrentQueue<Message[]>();
        private Dictionary<ulong, ulong> _lastHandledMessageOnChannel = new Dictionary<ulong, ulong>();

        public Bot(Config config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("Config must not be null.");
            }

            _config = config;
        }

        internal void Run()
        {
            _client = new DiscordClient();
            _client.MessageReceived += _client_MessageReceived;
            _client.MessageUpdated += _client_MessageUpdated;
            _client.ExecuteAndWait(async () => await _client.Connect(_config.Token, TokenType.Bot));
        }

        private async void _client_MessageUpdated(object client, MessageUpdatedEventArgs e)
        {
            // Don't handle own message or any message containing embeds that was *just* replied to
            if (e != null && e.Before != null && !e.Before.IsAuthor && ((e.Before.Embeds != null && e.Before.Embeds.Length == 0) || !IsMessageLastRepliedTo(e.Before.Channel.Id, e.Before.Id)))
            {
                if (_lastHandledMessageOnChannel.ContainsKey(e.Before.Channel.Id))
                    _lastHandledMessageOnChannel.Remove(e.Before.Channel.Id);

                bool calcDate = (DateTime.Now - e.Before.Timestamp).Minutes < _config.EditThreshold;
                string server = e.Before.Server == null ? "1-1" : e.Before.Server.Name;
                string user = e.Before.User == null ? "?" : e.Before.User.Name;
                string rawtext = e.Before.RawText ?? "";
                Console.WriteLine($"[{server}][Edit] {user}: {rawtext}");
                string reply = null;
                string[] words = rawtext.Split(' ');

                reply = await HandleCommands((DiscordClient) client, reply, words);

                if (reply == null)
                {
                    reply = HandleEmotesAndConversions(reply, words);
                }

                if (!string.IsNullOrWhiteSpace(reply) && calcDate)
                {
                    Message botRelation = GetExistingBotReplyOrNull(e.Before.Id);
                    if (botRelation == null)
                    {
                        await SendReply(e.After, e.After.Channel.Id, e.After.Id, reply);
                    }
                    else if (botRelation != null)
                    {
                        try
                        {
                            await botRelation.Edit(reply);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }
                    }
                }
            }
        }

        private async void _client_MessageReceived(object client, MessageEventArgs e)
        {
            if (e != null && e.Message != null && !e.Message.IsAuthor)
            {
                string server = e.Message.Server == null ? "1-1" : e.Message.Server.Name;
                string user = e.Message.User == null ? "?" : e.Message.User.Name;
                string rawtext = e.Message.RawText ?? "";
                Console.WriteLine("[{0}][Message] {1}: {2}", server, user, rawtext);
                string reply = null;
                string[] words = rawtext.Split(' ');

                // Private message, check for invites
                if (e.Server == null)
                {
                    await SendReply(e.Message, e.Message.Channel.Id, e.Message.Id, $"You can add the bot via {_config.AuthUrl}");
                    return;
                }

                reply = await HandleCommands((DiscordClient) client, reply, words);

                if (reply == null)
                    reply = HandleEmotesAndConversions(reply, words);

                if (!string.IsNullOrWhiteSpace(reply))
                {
                    await SendReply(e.Message, e.Message.Channel.Id, e.Message.Id, reply);
                }
            }
        }

        private async Task SendReply(Message message, ulong channelId, ulong messageId, string reply)
        {
            if (_client.MessageQueue.Count > 20)
            {
                Console.WriteLine("Too many messages queued at once.");
                return;
            }
            try
            {
                _lastHandledMessageOnChannel[channelId] = messageId;
                Message x = await _client.GetChannel(channelId).SendMessage(reply);
                AddBotReply(x, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private bool IsMessageLastRepliedTo(ulong channelId, ulong messageId)
        {
            return (_lastHandledMessageOnChannel.ContainsKey(channelId) && _lastHandledMessageOnChannel[channelId] == messageId);
        }

        private string HandleEmotesAndConversions(string reply, string[] words)
        {
            for (int i = words.Length - 1; i >= 0; --i)
            {
                string word = words[i];
                bool found = false;
                if (word.StartsWith("#"))
                {
                    string code = word.Substring(1, word.Length - 1);
                    found = IsWordEmote(code, ref reply);
                }
                else if (word.StartsWith(":") && word.EndsWith(":") && word.Length > 2)
                {
                    string code = word.Substring(1, word.Length - 2);
                    found = IsWordEmote(code, ref reply, false);
                }
                if (found)
                    break;

                switch (word)
                {
                    case "C":
                        if (i >= 1)
                        {
                            int celsius;
                            if (int.TryParse(words[i - 1], out celsius))
                            {
                                reply = celsius + " \u00b0C = " + (celsius * 9 / 5 + 32) + " \u00b0F";
                            }
                        }
                        break;
                    case "F":
                        if (i >= 1)
                        {
                            int fahrenheit;
                            if (int.TryParse(words[i - 1], out fahrenheit))
                            {
                                reply = fahrenheit + " \u00b0F = " + ((fahrenheit - 32) * 5 / 9) + " \u00b0C";
                            }
                        }
                        break;
                }
            }

            return reply;
        }


        private bool IsWordEmote(string code, ref string reply, bool caseSensitive = true)
        {
            Func<string, string, bool> emoteComparer = (first, second) => { return caseSensitive ? (first == second) : (first.ToLower() == second.ToLower()); };
            bool found = false;
            int emoteset = -2;

            foreach (var emote in Program.Emotes)
            {
                if (emote.Code == code)
                {
                    if (emote.EmoteSet == 0 || emote.EmoteSet == 457)
                    {
                        reply = GetEmoteUrl(emote);
                        found = true;
                        break;
                    }
                    else if (emote.EmoteSet > emoteset)
                    {
                        reply = GetEmoteUrl(emote);
                        found = true;
                        emoteset = emote.EmoteSet;
                    }
                }
            }
            if (!found)
            {
                foreach (var emote in Program.Emotes)
                {
                    if (emoteComparer(code, emote.Code))
                    {
                        if (emote.EmoteSet == 0 || emote.EmoteSet == 457)
                        {
                            reply = GetEmoteUrl(emote);
                            found = true;
                            break;
                        }
                        else if (emote.EmoteSet > emoteset)
                        {
                            reply = GetEmoteUrl(emote);
                            found = true;
                            emoteset = emote.EmoteSet;
                        }
                    }
                }
            }
            return found;
        }

        private string GetEmoteUrl(EmoteInfo emote_info)
        {
            string reply = "";
            switch (emote_info.Type)
            {
                case EmoteType.Twitch:
                    reply = $"http://emote.3v.fi/2.0/{emote_info.Id}.png";
                    break;
                case EmoteType.Bttv:
                    reply = "https:" + Program.BttvTemplate.Replace("{{id}}", emote_info.Id).Replace("{{image}}", "2x");
                    break;
                case EmoteType.Ffz:
                    reply = $"http://cdn.frankerfacez.com/emoticon/{emote_info.Id}/2";
                    break;
            }

            return reply;
        }

        private async Task<string> HandleCommands(DiscordClient client, string reply, string[] words)
        {
            if (words == null || words.Length < 0)
                return "An error occurred.";

            switch (words[0])
            {
                case "!stream":
                    if (words.Length > 1)
                    {
                        string json = await Program.RequestAsync("https://api.twitch.tv/kraken/streams/" + words[1].ToLower() + "?stream_type=all");
                        if (json != null)
                        {
                            var streams = JsonConvert.DeserializeObject<Twitch.Streams>(json);
                            if (streams != null)
                            {
                                if (streams.Stream == null)
                                {
                                    reply = "The channel is currently *offline*";
                                }
                                else
                                {
                                    long ticks = DateTime.UtcNow.Ticks - streams.Stream.CreatedAt.Ticks;
                                    TimeSpan ts = new TimeSpan(ticks);
                                    reply = "**[" + NullToEmpty(streams.Stream.Channel.DisplayName) + "]**" + (streams.Stream.Channel.IsPartner ? @"\*" : "") + " " + (streams.Stream.IsPlaylist ? "(Playlist)" : "")
                                        + "\n**Title**: " + NullToEmpty(streams.Stream.Channel.Status).Replace("*", @"\*")
                                        + "\n**Game:** " + NullToEmpty(streams.Stream.Game) + "\n**Viewers**: " + streams.Stream.Viewers
                                        + "\n**Uptime**: " + ts.ToString(@"d' day" + (ts.Days == 1 ? "" : "s") + @" 'hh\:mm\:ss")
                                        + "\n**Quality**: " + streams.Stream.VideoHeight + "p" + Math.Ceiling(streams.Stream.FramesPerSecond);
                                }
                            }
                        }
                    }
                    else
                    {
                        reply = "**Usage:** !stream channel";
                    }
                    break;
                case "!channel":
                    if (words.Length > 1)
                    {
                        string json = await Program.RequestAsync("https://api.twitch.tv/kraken/channels/" + words[1].ToLower());
                        if (json != null)
                        {
                            var channel = JsonConvert.DeserializeObject<Twitch.Channel>(json);
                            if (channel != null && channel.DisplayName != null)
                            {
                                reply = "**[" + NullToEmpty(channel.DisplayName) + "]**"
                                    + "\n**Partner**: " + (channel.IsPartner ? "Yes" : "No")
                                    + "\n**Title**: " + NullToEmpty(channel.Status).Replace("*", @"\*")
                                    + "\n**Registered**: " + NullToEmpty(channel.Registered.ToString("yyyy-MM-dd HH:mm")) + " UTC"
                                    + "\n**Followers**: " + channel.Followers;
                            }
                        }
                    }
                    else
                    {
                        reply = "**Usage:** !channel channel";
                    }
                    break;
                case "!source":
                    reply = "https://github.com/3ventic/botventic-core";
                    break;
                case "!frozen":
                    if (words.Length >= 2 && words[1] != "pizza")
                        break;
                    // Fall through to frozenpizza
                    goto case "!frozenpizza";
                case "!frozenpizza":
                    reply = "*starts making a frozen pizza*";
                    break;
                case "!update":
                    if (words.Length > 1)
                    {
                        switch (words[1])
                        {
                            case "emotes":
                                await Program.UpdateAllEmotesAsync();
                                reply = "*updated list of known emotes*";
                                break;
                        }
                    }
                    break;
                case "!bot":
                    try
                    {
                        reply = $"Connected via `{client.GatewaySocket.Host}`\nConnected to {client.Servers.Count()} servers.";
                    }
                    catch (Exception ex) when (ex is ArgumentNullException || ex is OverflowException || ex is PlatformNotSupportedException)
                    {
                        reply = $"Error: {ex.Message}";
                        Console.WriteLine(ex.ToString());
                    }
                    break;
                case "!foodporn":
                    try
                    {
                        var rnd = new Random();
                        var page = rnd.Next(1, 10);
                        string downloadString = await Program.RequestAsync($"http://foodporndaily.com/explore/food/page/{page}/");
                        string regexImgSrc = @"<img[^>]*?src\s*=\s*[""']?([^'"" >]+?)[ '""][^>]*?>";
                        var matchesImgSrc = Regex.Matches(downloadString, regexImgSrc, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        int image = rnd.Next(1, matchesImgSrc.Count);
                        reply = matchesImgSrc[image].Groups[1].Value;
                    }
                    catch (Exception ex)
                    {
                        reply = $"Could not get the foodporn image. Error: {ex.Message }";
                        Console.WriteLine(ex.ToString());
                    }
                    break;
            }

            return reply;
        }

        private void AddBotReply(Message bot, Message user)
        {
            while (_replies.Count > _config.EditMax)
            {
                Message[] dummy;
                _replies.TryDequeue(out dummy);
            }
            _replies.Enqueue(new Message[] { bot, user });
        }

        private enum MessageIndex
        {
            BotReply,
            UserMessage
        }

        private Message GetExistingBotReplyOrNull(ulong id)
        {
            foreach (var item in _replies)
            {
                if (item[(int) MessageIndex.UserMessage].Id == id)
                {
                    return item[(int) MessageIndex.BotReply];
                }
            }
            return null;
        }

        private static string NullToEmpty(string str)
        {
            return (str == null) ? "" : str;
        }
    }
}
