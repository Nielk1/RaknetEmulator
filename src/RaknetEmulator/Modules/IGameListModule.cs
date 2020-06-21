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

        /// <summary>
        /// Rank plugin as able to handle request from 0.0 to 1.0
        /// </summary>
        /// <param name="Paramaters">Paramaters sent on Query String or Post Body</param>
        /// <param name="Path">Array of path elements</param>
        /// <param name="Method">GET, POST, PUT, DELETE</param>
        /// <returns>0.0 to 1.0 range of confidence this plugin can handle this request</returns>
        float IsPluginLikely(JObject Paramaters, string[] Path, string Method);

        #region GET
        /// <summary>
        /// Chance to alter GET QueryString values.
        /// This is the opertunity to alter otherwise hard-coded paramaters.
        /// </summary>
        /// <param name="Paramaters">Request paramaters to be altered</param>
        void TransformGetParamaters(ref JObject Paramaters);

        /// <summary>
        /// Alterations to the game list may be made here immediatly after the database lookup
        /// </summary>
        /// <param name="Paramaters">Request paramaters</param>
        /// <param name="RawGames">Game list</param>
        /// <param name="ExtraData">Extra fields to attach to output</param>
        void PreProcessGameList(ref JObject Paramaters, ref List<GameData> RawGames, ref Dictionary<string, JObject> ExtraData);

        /// <summary>
        /// Chance to alter GET ResponseObject.
        /// This is the opertunity to alter otherwise hard-coded output.
        /// </summary>
        /// <param name="ResponseObject">Response object to be altered</param>
        void TransformGetResponse(ref JObject ResponseObject);
        #endregion GET


        #region POST
        /// <summary>
        /// Chance to alter POST QueryString values.
        /// </summary>
        /// <param name="Paramaters">Request paramaters to be altered</param>
        void TransformPostParamaters(ref JObject Paramaters);

        /// <summary>
        /// Chance to alter POST ResponseObject.
        /// This is the opertunity to alter otherwise hard-coded output.
        /// </summary>
        /// <param name="ResponseObject">Response object to be altered</param>
        void TransformPostResponse(ref PostGameResponse ResponseObject);
        #endregion POST

        #region DELETE
        /// <summary>
        /// Chance to alter DELETE QueryString values.
        /// </summary>
        /// <param name="Paramaters">Request paramaters to be altered</param>
        void TransformDeleteParamaters(ref JObject Paramaters);
        #endregion DELETE



        //double GetLastResult { get; }
        //double Execute(double value1, double value2);

        //event EventHandler OnExecute;

        //void ExceptionTest(string input);
    }
}
