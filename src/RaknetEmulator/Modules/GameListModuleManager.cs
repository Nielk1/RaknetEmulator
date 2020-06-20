using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RaknetEmulator.Plugins
{
    public class GameListModuleManager
    {
        public Dictionary<string, IGameListModule> GameListPlugins { get; set; }
        public IEnumerable<string> RowIdKeys => GameListPlugins.Values.Select(dr => dr.CustomRowIdKey).Where(dr => !string.IsNullOrWhiteSpace(dr));
        public IEnumerable<string> GameIdKeys => GameListPlugins.Values.Select(dr => dr.CustomGameIdKey).Where(dr => !string.IsNullOrWhiteSpace(dr));

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
    }
}
