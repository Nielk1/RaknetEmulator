using Newtonsoft.Json.Linq;
using RaknetEmulator.Controllers;
using RaknetEmulator.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaknetEmulator.Plugins
{
    public interface IGameListModule
    {
        string GameID { get; }
        string Name { get; }
        //Version Version { get; }
        string DisplayName { get; }

        string CustomRowIdKey { get; }
        string CustomGameIdKey { get; }

        /// <summary>
        /// Change to alter QueryString values.
        /// </summary>
        /// <param name="queryString"></param>
        void InterceptDataInForGet(ref Dictionary<string,string> queryString);

        void InterceptDataForDelete(ref Dictionary<string, string> paramaters);

        /// <summary>
        /// Alterations to the game list may be made here immediatly after the database lookup
        /// </summary>
        /// <param name="queryString"></param>
        /// <param name="rawGames"></param>
        void PreProcessGameList(ref Dictionary<string,string> queryString, ref List<GameData> rawGames, ref Dictionary<string, JObject> ExtraData);
        void InterceptDataInForPost(ref JObject postedObject);
        void InterceptDataOutForPost(ref PostGameResponse retVal);
        void PostProcessGameList(ref JArray gameArray);



        //double GetLastResult { get; }
        //double Execute(double value1, double value2);

        //event EventHandler OnExecute;

        //void ExceptionTest(string input);
    }
}
