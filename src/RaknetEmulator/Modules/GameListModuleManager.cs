using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RaknetEmulator.Plugins
{
    public class GameListModuleManager
    {
        public Dictionary<string, IGameListModule> GameListPlugins { get; set; }

        public GameListModuleManager(IConfiguration Configuration)
        {
            GameListPlugins = new Dictionary<string, IGameListModule>();

            foreach (Type item in typeof(IGameListModule).GetTypeInfo().Assembly.GetTypes())
            {
                //if (!item.IsClass) continue;
                if (item.GetInterfaces().Contains(typeof(IGameListModule)))
                {
                    IGameListModule plugin = (IGameListModule)Activator.CreateInstance(item, Configuration);
                    GameListPlugins.Add(plugin.GameID, plugin);
                }
            }
        }

        public IEnumerable<IGameListModule> GetLikelyPlugins(JObject Paramaters, string Path, string Method)
        {
            string[] PathArray = Path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return GameListPlugins.Values
                .Select(dr => new { Item = dr, Rank = dr.IsPluginLikely(Paramaters, PathArray, Method) })
                .Where(dr => dr.Rank > 0.0f)
                .OrderByDescending(dr => dr.Rank)
                .Select(dr => dr.Item);
        }
    }
}
