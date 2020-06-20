using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RaknetEmulator.Controllers;
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

namespace RaknetEmulator.Modules.BZCC
{
    public class BattlezoneCCGameList : IGameListModule
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

        public string GameID => "BZCC";
        public string Name => "Battlezone CC GameList";
        //public Version Version { get { return typeof(BattlezoneCCGameList).Assembly.GetName().Version; } }
        //public string DisplayName { get { return Name + @" (" + Version.ToString() + @")"; } }
        public string DisplayName => Name;
        public string CustomRowIdKey => "rid";
        public string CustomGameIdKey => "gid";

        List<ListSource> sources;

        public BattlezoneCCGameList(IConfiguration Configuration)
        {
            sources = Configuration.GetSection("Plugins:BZCC:RemoteSources")
                .GetChildren()
                .Select(conf => new ListSource()
                {
                    ProxySource = conf["ProxySource"],
                    Url = conf["Url"],
                    Timeout = conf.GetValue<int>("Timeout"),
                    Stale = conf.GetValue<int>("Stale"),
                    MaxStale = conf.GetValue<int>("MaxStale"),
                }).ToList();
        }

        public void InterceptDataInForGet(ref Dictionary<string,string> queryString)
        {
            string excluded = queryString.ContainsKey("__excludeCols") ? queryString["__excludeCols"] : null;
            queryString["__excludeCols"] = string.Join(",", (excluded?.Split(',') ?? new string[] { }).Append("__addr").Distinct());
        }

        public void InterceptDataForDelete(ref Dictionary<string, string> paramaters)
        {
            paramaters["__rowPW"] = paramaters["rpwd"];
            paramaters["rpwd"] = null;
        }

        public void PreProcessGameList(ref Dictionary<string,string> queryString, ref List<GameData> rawGames, ref Dictionary<string, JObject> ExtraData)
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

            long rowIdCounter = -1;

            /*{
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
                hardCodedGame.GameAttributes.Add(new CustomGameDataField() { Key = "n", Value = @"SW9uRHJpdmVyIEJpc211dGggKE1hc3RlciBFbXVsYXRvcikAAAAAAA==" }); // IonDriver Bismuth (Master Emulator)
                hardCodedGame.GameAttributes.Add(new CustomGameDataField() { Key = "l", Value = @"1" });
                hardCodedGame.GameAttributes.Add(new CustomGameDataField() { Key = "m", Value = @"bismuth" });
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("d", string.Empty));
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("k", 1.ToString()));
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("t", 7.ToString()));
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("r", @"@ZA@d1"));
                //hardCodedGame.GameAttributes.Add(new CustomGameDataField("v", @"S1"));

                rawGames.Add(hardCodedGame);
            }*/

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
                            //string addressPossibleRemap = string.Empty;
                            //addressPossibleRemap = "0.0.0.0";

                            try
                            {
                                GameData remoteGame = new GameData()
                                {
                                    addr = null,//addressPossibleRemap,//dr["__addr"].Value<string>(),
                                    clientReqId = dr["__clientReqId"]?.Value<long>(),
                                    gameId = dr["__gameId"]?.Value<string>() ?? "BZCC",
                                    lastUpdate = DateTime.UtcNow,
                                    rowId = rowIdCounter--,
                                    rowPW = string.Empty,
                                    timeoutSec = dr["__timeoutSec"]?.Value<long>() ?? 60,
                                    //updatePw = string.Empty
                                };

                                dr.Properties().ToList().ForEach(dx =>
                                {
                                    if (!dx.Name.StartsWith("__") && dx.Value.Type != JTokenType.Null)
                                    {
                                        remoteGame.GameAttributes.Add(new CustomGameDataField() { Key = dx.Name, Value = (dx.Value.Type == JTokenType.String ? ("\"" + dx.Value.ToString() + "\"") : dx.Value.ToString()) });
                                    }
                                });

                                if (ShowSource)
                                    remoteGame.GameAttributes.Add(new CustomGameDataField() { Key = "proxySource", Value = $"\"{source.ProxySource}\"" });

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

            if (statusData != null)
            {
                ExtraData["proxyStatus"] = statusData;
            }

            //return rawGames;
        }

        public void PostProcessGameList(ref JArray gameArray)
        {
            foreach(JObject game in gameArray)
            {
                if (game["__gameId"] != null)
                {
                    game["gid"] = game["__gameId"];
                    game.Remove("__gameId");
                }

                if (game["__rowId"] != null)
                {
                    game["rid"] = game["__rowId"];
                    game.Remove("__rowId");
                }

                if (game["__clientReqId"] != null)
                {
                    game["cri"] = game["__clientReqId"];
                    game.Remove("__clientReqId");
                }

                if (game["__timeoutSec"] != null)
                {
                    game["ts"] = game["__timeoutSec"];
                    game.Remove("__timeoutSec");
                }
            }
        }

        public void InterceptDataInForPost(ref JObject postedObject)
        {
            if (postedObject["gid"] != null)
            {
                postedObject["__gameId"] = postedObject["gid"];
                postedObject.Remove("gid");
            }

            if (postedObject["cri"] != null)
            {
                postedObject["__clientReqId"] = postedObject["cri"];
                postedObject.Remove("cri");
            }

            if (postedObject["ts"] != null)
            {
                postedObject["__timeoutSec"] = postedObject["ts"];
                postedObject.Remove("ts");
            }

            if (postedObject["rpwd"] != null)
            {
                postedObject["__rowPW"] = postedObject["rpwd"];
                postedObject.Remove("rpwd");
            }
        }

        public void InterceptDataOutForPost(ref PostGameResponse retVal)
        {
            if (retVal.POST.ContainsKey("__gameId"))
            {
                retVal.POST["gid"] = retVal.POST["__gameId"];
                retVal.POST.Remove("__gameId");
            }

            if (retVal.POST.ContainsKey("__clientReqId"))
            {
                retVal.POST["cri"] = retVal.POST["__clientReqId"];
                retVal.POST.Remove("__clientReqId");
            }

            if (retVal.POST.ContainsKey("__rowId"))
            {
                retVal.POST["rid"] = retVal.POST["__rowId"];
                retVal.POST.Remove("__rowId");
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