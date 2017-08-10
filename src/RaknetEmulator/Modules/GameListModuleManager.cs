using Microsoft.AspNet.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.PlatformAbstractions;
using RaknetEmulator.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

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
