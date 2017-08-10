using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RaknetEmulator.Models;
using RaknetEmulator.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RaknetEmulator.Modules.BZ2
{
    public class Battlezone2GameList : IGameListModule
    {
        internal class ListSource
        {
            public string ProxySource { get; set; }
            public string Url { get; set; }
            public int Timeout { get; set; }
            public int Stale { get; set; }
            public int MaxStale { get; set; }
            public DateTime Date { get; set; }
            public JObject LastData { get; set; }

            public SemaphoreSlim WebRequestActive;

            public ListSource()
            {
                WebRequestActive = new SemaphoreSlim(1, 1);
            }
        }

        public string GameID { get { return "BZ2"; } }
        public string Name { get { return "Battlezone 2 GameList"; } }
        //public Version Version { get { return typeof(Battlezone2GameList).Assembly.GetName().Version; } }
        //public string DisplayName { get { return Name + @" (" + Version.ToString() + @")"; } }
        public string DisplayName { get { return Name; } }

        //private object KebbzLock = new object();
        //private JObject KebbzData;
        //private DateTime KebbzDataDate;

        List<ListSource> sources;
        Dictionary<IPEndPoint, PongCacheData> pongCache;

        public Battlezone2GameList(IConfiguration Configuration)
        {
            //sources = new List<ListSource>();
            pongCache = new Dictionary<IPEndPoint, PongCacheData>();

            sources = Configuration.GetSection("Plugins:BZ2:RemoteSources")
                .GetChildren()
                .Select(conf => new ListSource()
                {
                    ProxySource = conf["ProxySource"],
                    Url = conf["Url"],
                    Timeout = conf.GetValue<int>("Timeout"),
                    Stale = conf.GetValue<int>("Stale"),
                    MaxStale = conf.GetValue<int>("MaxStale"),
                }).ToList();

            //sources.Add(new ListSource()
            //{
            //    ProxySource = "masterserver.matesfamily.org",
            //    Url = @"http://masterserver.matesfamily.org/testServer?__gameId=BZ2",
            //    Timeout = 3000,
            //    Stale = 10000,
            //    MaxStale = 30000,
            //});

            //sources.Add(new ListSource()
            //{
            //    ProxySource = "gamelist.kebbz.com",
            //    Url = @"http://gamelist.kebbz.com/testServer?__gameId=BZ2",
            //    Timeout = 2000,
            //    Stale = 10000,
            //    MaxStale = 30000,
            //});
        }

        public void InterceptQueryStringForGet(ref Microsoft.AspNetCore.Http.IQueryCollection queryString) { }

        public void PreProcessGameList(Microsoft.AspNetCore.Http.IQueryCollection queryString, ref List<GameData> rawGames, ref Dictionary<string, JObject> ExtraData)
        {
            bool DoProxy = true;
            if (queryString.ContainsKey("__pluginProxy") && !bool.TryParse(queryString["__pluginProxy"], out DoProxy))
            {
                DoProxy = true;
            }

            bool ShowSource = false;
            if (queryString.ContainsKey("__pluginShowSource") && !bool.TryParse(queryString["__pluginShowSource"], out ShowSource))
            {
                ShowSource = false;
            }

            bool ShowStatus = false;
            if (queryString.ContainsKey("__pluginShowStatus") && !bool.TryParse(queryString["__pluginShowStatus"], out ShowStatus))
            {
                ShowStatus = false;
            }

            bool QueryServers = false;
            if (queryString.ContainsKey("__pluginQueryServers") && !bool.TryParse(queryString["__pluginQueryServers"], out QueryServers))
            {
                QueryServers = false;
            }

            if (QueryServers)
            {
                lock (pongCache)
                {
                    pongCache.Where(dr => dr.Value.cacheTime.AddSeconds(30) < DateTime.UtcNow).Select(dr => dr.Key).ToList().ForEach(dr => pongCache.Remove(dr));
                }
            }

            long rowIdCounter = -1;

            {
                GameData hardCodedGame = new GameData()
                {
                    addr = @"0.0.0.0:17770",
                    clientReqId = 0,
                    gameId = "BZ2",
                    lastUpdate = DateTime.UtcNow,
                    rowId = rowIdCounter--,
                    rowPW = string.Empty,
                    timeoutSec = 300,
                    //updatePw = string.Empty
                };
                hardCodedGame.GameAttributes.Add(new CustomGameDataField() { Key = "n", Value = @"IonDriver Bismuth (Raknet Master2 Emulator)" });
                hardCodedGame.GameAttributes.Add(new CustomGameDataField() { Key = "l", Value = @"1" });
                hardCodedGame.GameAttributes.Add(new CustomGameDataField() { Key = "m", Value = @"bismuth" });
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("d", string.Empty));
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("k", 1.ToString()));
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("t", 7.ToString()));
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("r", @"@ZA@d1"));
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("v", @"S1"));

                rawGames.Add(hardCodedGame);
            }

            JObject statusData = null;
            if (DoProxy && ShowStatus)
            {
                statusData = new JObject();
            }

            if (DoProxy)
            {
                object counterLock = new object();
                int counter = sources.Count;

                sources.AsParallel().ForAll(dr =>
                {
                    RemoteCallStatus cacheType = RemoteCallStatus.cached;
                    try
                    {
                        if (dr.Date == null || dr.Date.AddMilliseconds(dr.Stale) < DateTime.UtcNow)
                        {
                            cacheType = RemoteCallStatus.none;
                            //lock (dr)
                            dr.WebRequestActive.Wait(); //await dr.WebRequestActive.WaitAsync();
                            try
                            {
                                // make sure another thread didn't do this for us while we were locked
                                if (dr.Date == null || dr.Date.AddMilliseconds(dr.Stale) < DateTime.UtcNow)
                                {
                                    try
                                    {
                                        using (var client = new HttpClient())
                                        {
                                            client.Timeout = TimeSpan.FromMilliseconds(dr.Timeout);
                                            //var response = await client.GetAsync(dr.Url);
                                            var responseTask = client.GetAsync(dr.Url);
                                            responseTask.Wait();
                                            if (responseTask.IsCompleted && !responseTask.IsCanceled && !responseTask.IsFaulted)
                                            {
                                                var response = responseTask.Result;

                                                if (response.IsSuccessStatusCode)
                                                {
                                                    dr.LastData = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                                                    dr.Date = DateTime.UtcNow;
                                                    cacheType = RemoteCallStatus.@new;
                                                }
                                                else if (dr.Date == null || dr.Date.AddMilliseconds(dr.MaxStale) < DateTime.UtcNow)
                                                {
                                                    // we failed to get data and it's too old, so destroy the cache
                                                    dr.LastData = null;
                                                    dr.Date = DateTime.UtcNow;
                                                    cacheType = RemoteCallStatus.expired;
                                                }
                                            }
                                            else if (dr.Date == null || dr.Date.AddMilliseconds(dr.MaxStale) < DateTime.UtcNow)
                                            {
                                                // we failed to get data and it's too old, so destroy the cache
                                                dr.LastData = null;
                                                dr.Date = DateTime.UtcNow;
                                                cacheType = RemoteCallStatus.expired;
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        if (dr.Date == null || dr.Date.AddMilliseconds(dr.MaxStale) < DateTime.UtcNow)
                                        {
                                            dr.LastData = null;
                                            dr.Date = DateTime.UtcNow;
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                dr.WebRequestActive.Release();
                            }
                        }
                    }
                    //catch(Exception ex)
                    //{
                    //    Console.WriteLine(ex.ToString());
                    //}
                    finally
                    {
                        if (ShowStatus)
                        {
                            var tmp = new JObject();
                            tmp["updated"] = dr.Date;
                            tmp["status"] = cacheType.ToString();
                            tmp["success"] = dr.LastData != null;
                            lock (statusData)
                            {
                                statusData[dr.ProxySource] = tmp;
                            }
                        }

                        lock (counterLock)
                        {
                            counter--;
                        }
                    }
                }); // the async member inside appears to be causing it to not join, but I can't await it so I have to do a block

                while (counter > 0)
                {
                    Thread.Sleep(100);
                }

                HashSet<int> usedPorts = new HashSet<int>() { 17770 }; // start with hardcoded game already there
                // clone list so that it can't be modified under us by another thread
                List<ListSource> sourcesClone = sources.ToList();
                foreach (ListSource source in sourcesClone)
                {
                    if (source.LastData != null)
                    {
                        rawGames.AddRange(((JArray)(source.LastData["GET"])).Cast<JObject>().ToList().Select(dr =>
                        {
                            string addressPossibleRemap = string.Empty;
                            try
                            {
                                string[] urlbits = dr["__addr"].Value<string>().Split(':');
                                if (urlbits.Length > 0 && urlbits.Length <= 2 && urlbits[0] == "0.0.0.0")
                                {
                                    int portNum = 17770;
                                    string port = urlbits.Length > 1 ? urlbits[1] : "17770";
                                    if (!int.TryParse(port, out portNum))
                                    {
                                        portNum = 17770;
                                    }

                                    while (usedPorts.Contains(portNum))
                                    {
                                        portNum++;
                                    }
                                    usedPorts.Add(portNum);
                                    if (portNum != 17770)
                                    {
                                        addressPossibleRemap = urlbits[0] + ":" + portNum;
                                    }
                                    else
                                    {
                                        addressPossibleRemap = dr["__addr"].Value<string>();
                                    }
                                }
                                else
                                {
                                    addressPossibleRemap = dr["__addr"].Value<string>();
                                }
                            }
                            catch
                            {
                                addressPossibleRemap = dr["__addr"].Value<string>();
                            }

                            try
                            {
                                GameData remoteGame = new GameData()
                                {
                                    addr = addressPossibleRemap,//dr["__addr"].Value<string>(),
                                    clientReqId = dr["__clientReqId"]?.Value<long>(),
                                    gameId = dr["__gameId"]?.Value<string>() ?? "BZ2",
                                    lastUpdate = DateTime.UtcNow,
                                    rowId = rowIdCounter--,
                                    rowPW = string.Empty,
                                    timeoutSec = dr["__timeoutSec"]?.Value<long>() ?? 60,
                                    //updatePw = string.Empty
                                };

                                dr.Properties().ToList().ForEach(dx =>
                                {
                                    if (!dx.Name.StartsWith("__"))
                                    {
                                        remoteGame.GameAttributes.Add(new CustomGameDataField() { Key = dx.Name, Value = dx.Value.Value<string>() });
                                    }
                                });

                                if (ShowSource)
                                    remoteGame.GameAttributes.Add(new CustomGameDataField() { Key = "proxySource", Value = source.ProxySource });

                                //rawGames.Add(kebbzGame);
                                return remoteGame;
                            }
                            catch (Exception ex)
                            {

                            }
                            return null;
                        }).Where(dr => dr != null));
                    }
                }
            }

            if (QueryServers)
            {
                rawGames.AsParallel().ForAll(game =>
                {
                    {
                        RaknetPong packetOut = new RaknetPong();// { Players = true, Teams = true, ServerInfo = false };

                        IPEndPoint endPoint = null;
                        {
                            Uri url;
                            IPAddress _ip;
                            if (Uri.TryCreate(String.Format("http://{0}", game.addr), UriKind.Absolute, out url) &&
                               IPAddress.TryParse(url.Host, out _ip))
                            {
                                endPoint = new IPEndPoint(_ip, url.IsDefaultPort ? 17770 : url.Port);
                            }
                        }

                        if (endPoint != null)
                        {
                            try
                            {
                                lock (pongCache)
                                {
                                    if (pongCache.ContainsKey(endPoint))
                                    {
                                        game.CustomAttributes.Add("pong", pongCache[endPoint].data);
                                        return;
                                    }
                                }

                                UdpClient udpServer = new UdpClient(0);

                                byte[] dataSend = packetOut.GetPacket();
                                var receivedData = udpServer.ReceiveAsync();
                                udpServer.SendAsync(dataSend, dataSend.Length, endPoint).Wait();

                                receivedData.Wait(1000);
                                if (receivedData.IsCompleted && !receivedData.IsCanceled && !receivedData.IsFaulted)
                                {
                                    var receivedData2 = receivedData.Result;

                                    RaknetPongResponse rakPong = null;

                                    using (MemoryStream mem = new MemoryStream(receivedData2.Buffer))
                                    using (BinaryReader reader = new BinaryReader(mem))
                                    {
                                        UInt32 PacketType = reader.ReadUInt32();
                                        if (PacketType != 0x0000001c) return;

                                        byte nul = reader.ReadByte();
                                        if (nul != 0x00) return;

                                        UInt32 Pong = reader.ReadUInt32();
                                        if (Pong != packetOut.Ping) return;

                                        UInt64 Unknown1 = reader.ReadUInt64();

                                        byte Switch1 = reader.ReadByte();
                                        if (Switch1 != 0x00 && Switch1 != 0xff) return;

                                        byte Switch2 = reader.ReadByte();
                                        if (Switch2 != 0x00 && Switch2 != 0xff) return;

                                        byte Switch3 = reader.ReadByte();
                                        if (Switch3 != 0x00 && Switch3 != 0xff) return;

                                        nul = reader.ReadByte();
                                        if (nul != 0x00) return;

                                        UInt32 Unknown2 = reader.ReadUInt32();
                                        UInt32 Unknown3 = reader.ReadUInt32();
                                        UInt32 Unknown4 = reader.ReadUInt32();

                                        rakPong = new RaknetPongResponse(reader);

                                        byte[] ZLibData = reader.ReadBytes(rakPong.m_CompressedLen);

                                        byte[] Remainder = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position + 1));

                                        ZLibData = ZLibData.Skip(2).ToArray();
                                        byte[] uncompressedData = new byte[1038];
                                        FixedBufferDecompress(ZLibData, ref uncompressedData);


                                        using (var compressedStream = new MemoryStream(uncompressedData))
                                        using (var compressedStreamReader = new BinaryReader(compressedStream))
                                        {
                                            rakPong.CompressedData = new CompressibleRaknetPongResponse(compressedStreamReader);
                                            rakPong.CompressedData.CurrentPlayerCount = rakPong.CurPlayers;
                                        }
                                    }

                                    if (rakPong != null)
                                    {
                                        JObject customRakObject = JObject.FromObject(rakPong);
                                        game.CustomAttributes.Add("pong", customRakObject);

                                        lock (pongCache)
                                        {
                                            pongCache[endPoint] = new PongCacheData()
                                            {
                                                cacheTime = DateTime.UtcNow,
                                                data = customRakObject
                                            };
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                });
            }

            if (statusData != null)
            {
                ExtraData["proxyStatus"] = statusData;
            }

            //return rawGames;
        }

        private static bool IsValidIP(string address)
        {
            if (!Regex.IsMatch(address, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b"))
                return false;

            IPAddress dummy;
            return IPAddress.TryParse(address, out dummy);
        }

        static void FixedBufferDecompress(byte[] data, ref byte[] destArray)
        {
            using (var compressedStream = new MemoryStream(data))
            using (var zipStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
            using (var resultStream = new MemoryStream())
            {
                try
                {
                    int idx = 0;
                    int byteVal = -1;
                    do
                    {
                        byteVal = zipStream.ReadByte();
                        destArray[idx] = (byte)byteVal;
                        idx++;
                    } while (byteVal > -1 && idx < destArray.Length);
                }
                catch { }
            }
        }

        private static string DecodeAscii(byte[] buffer)
        {
            int count = Array.IndexOf<byte>(buffer, 0, 0);
            if (count < 0) count = buffer.Length;
            return Encoding.ASCII.GetString(buffer, 0, count);
        }

        struct PongCacheData
        {
            public JObject data;
            public DateTime cacheTime;
        }

        class RaknetPongResponse
        {
            public byte DataVersion; // To ignore malformed data
            [JsonIgnore]
            public UInt32 m_BitfieldBits;
            public byte TimeLimit;
            public byte KillLimit;
            public byte GameTimeMinutes;

            public UInt16 MaxPing;
            public UInt16 GameVersion; // == NETWORK_GAME_VERSION
            [JsonIgnore]
            public UInt16 m_CompressedLen; // # of bytes in m_PaddedData that are used 
                                           // 16 bytes to here

            public CompressibleRaknetPongResponse CompressedData;

            public bool bDataValid { get { return (m_BitfieldBits & 0x01) == 0x01; } }                    //                            1 // : 1;
            public bool bPassworded { get { return (m_BitfieldBits & 0x02) == 0x02; } }                   //                           10 // : 1; // 2 bits
            public byte CurPlayers { get { return (byte)((m_BitfieldBits & 0x3C) >> (6 - 4)); } }         //                       111100 // : 4; // 6 bits
            public byte MaxPlayers { get { return (byte)((m_BitfieldBits & 0x3C0) >> (10 - 4)); } }       //                   1111000000 // : 4; // 10 bits
            public byte TPS { get { return (byte)((m_BitfieldBits & 0x7C00) >> (15 - 5)); } }             //              111110000000000 // : 5; // 15 bits
            public bool bLockedDown { get { return (m_BitfieldBits & 0x8000) == 0x8000; } }               //             1000000000000000 // : 1; // 16 bits
            public byte GameType { get { return (byte)((m_BitfieldBits & 0x30000) >> (18 - 2)); } }       //           110000000000000000 // : 2; // 18 bits, == ivar5 type
            public byte ServerInfoMode { get { return (byte)((m_BitfieldBits & 0xE0000) >> (21 - 3)); } } //        111000000000000000000 // : 3; // 21 bits == ServerInfoMode type
            public bool TeamsOn { get { return (m_BitfieldBits & 0x200000) == 0x200000; } }               //       1000000000000000000000 // : 1; // 22 bits == ivar3
            public byte GameSubType { get { return (byte)((m_BitfieldBits & 0x7C00000) >> (27 - 5)); } }  //  111110000000000000000000000 // : 5; // 27 bits == ivar7
            public bool OnlyOneTeam { get { return (m_BitfieldBits & 0x8000000) == 0x8000000; } }         // 1000000000000000000000000000 // : 1; // 28 bits == ivar12

            public RaknetPongResponse(BinaryReader reader)
            {
                DataVersion = reader.ReadByte();
                m_BitfieldBits = reader.ReadUInt32();
                TimeLimit = reader.ReadByte();
                KillLimit = reader.ReadByte();
                GameTimeMinutes = reader.ReadByte();

                MaxPing = reader.ReadUInt16();
                GameVersion = reader.ReadUInt16();
                m_CompressedLen = reader.ReadUInt16();
            }
        }

        class CompressibleRaknetPongResponse
        {
            [JsonIgnore]
            public byte[] m_SessionName;
            [JsonIgnore]
            public byte[] m_MapName;
            [JsonIgnore]
            public byte[] m_Mods;
            [JsonIgnore]
            public byte[] m_MapURL;
            [JsonIgnore]
            public byte[] m_MOTD;

            [JsonIgnore]
            public int CurrentPlayerCount = -1;

            public string SessionName { get { return DecodeAscii(m_SessionName); } }
            public string MapName { get { return DecodeAscii(m_MapName); } }
            public string Mods { get { return DecodeAscii(m_Mods); } }
            public string MapURL { get { return DecodeAscii(m_MapURL); } }
            public string MOTD { get { return DecodeAscii(m_MOTD); } }

            [JsonIgnore]
            public RaknetPongPlayerInfo[] m_Players;

            public RaknetPongPlayerInfo[] Players { get { return CurrentPlayerCount > -1 ? m_Players.Take(CurrentPlayerCount).ToArray(): m_Players; } }

            private const int NET_MAX_PLAYERS = 16;

            public CompressibleRaknetPongResponse(BinaryReader reader)
            {
                m_SessionName = reader.ReadBytes(44);
                m_MapName = reader.ReadBytes(32);
                m_Mods = reader.ReadBytes(128);
                m_MapURL = reader.ReadBytes(96);
                m_MOTD = reader.ReadBytes(128);

                m_Players = new RaknetPongPlayerInfo[NET_MAX_PLAYERS];
                for (int i = 0; i < m_Players.Length; i++)
                {
                    m_Players[i] = new RaknetPongPlayerInfo(reader);
                }
            }
        }

        class RaknetPongPlayerInfo
        {
            [JsonIgnore]
            public byte[] m_UserName;
            public byte Kills;
            public byte Deaths;
            public byte Team;
            public short Score;

            public string UserName { get { return DecodeAscii(m_UserName); } }

            private const int MAX_PLAYERNAME_LEN = 32;

            public RaknetPongPlayerInfo(BinaryReader reader)
            {
                m_UserName = reader.ReadBytes(MAX_PLAYERNAME_LEN + 1);
                Kills = reader.ReadByte();
                Deaths = reader.ReadByte();
                Team = reader.ReadByte();
                Score = reader.ReadInt16();
            }
        };

        class RaknetPong
        {
            private UInt32 Header = 0x00000002;

            public UInt32 Ping { get; set; }

            public bool ServerInfo { get; set; }
            public bool Players { get; set; }
            public bool Teams { get; set; }

            public RaknetPong()
            {
                Random tmp = new Random();
                Ping = (UInt32)tmp.Next();
                Players = true;
                Teams = true;
                ServerInfo = false;
            }

            public byte[] GetPacket()
            {
                byte[] arr = new byte[4 + 1 + 4 + 3 + 1 + 4 + 4 + 4 + 8];
                using (MemoryStream memStream = new MemoryStream(arr, 0, arr.Length, true, false))
                using (BinaryWriter writer = new BinaryWriter(memStream))
                {
                    writer.Write(BitConverter.GetBytes(Header));
                    writer.Write((byte)0x00);
                    writer.Write(BitConverter.GetBytes(Ping));
                    writer.Write((byte)(ServerInfo ? 0xff : 0x00));
                    writer.Write((byte)(Players ? 0xff : 0x00));
                    writer.Write((byte)(Teams ? 0xff : 0x00));
                    writer.Write((byte)0x00);
                    writer.Write(0xfefefefe);
                    writer.Write(0xfdfdfdfd);
                    writer.Write(BitConverter.GetBytes(0x78563412));
                    writer.Write(BitConverter.GetBytes(0x870a7abef0009007));
                }
                return arr;
            }
        }

        private enum RemoteCallStatus
        {
            none,
            @new,
            expired,
            cached,
        }
    }
}