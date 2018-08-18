using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RaknetEmulator.Plugins
{
    public interface IGameListModuleManager
    {
        Dictionary<string, IGameListModule> GameListPlugins { get; set; }
    }

    public class GameListModuleManager : IGameListModuleManager
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
    }
}
