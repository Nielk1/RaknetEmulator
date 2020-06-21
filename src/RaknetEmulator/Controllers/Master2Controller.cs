using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RaknetEmulator.Models;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Net;
using RaknetEmulator.Plugins;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace RaknetEmulator.Controllers
{
    public class PostGameResponse
    {
        public Dictionary<string, JToken> POST { get; set; }
    }

    public class Master2Controller : Controller
    {
        private readonly GameListContext _gameListContext;
        private readonly GameListModuleManager _gameListModuleManager;

        //Object _GamelistNullLock = new Object();

        bool s_AllowEmptyGameID = false;
        int s_TimeoutDefault = 60;
        int s_TimeoutMin = 15;
        int s_TimeoutMax = 300; // 900 on private list servers

        public Master2Controller(GameListContext gameListContext, GameListModuleManager gameListModuleManager)
        {
            _gameListContext = gameListContext;
            _gameListModuleManager = gameListModuleManager;
        }

        public IActionResult Index()
        {
            return Json(new { Error = "Resource not found" });
        }

        //[HttpGet]
        //[HttpDelete]
        //[HttpPost]
        //[HttpPut]
        public IActionResult GameList()
        {
            //return View();
            Response.Headers["Connection"] = "close";
            switch (Request.Method)
            {
                case "GET":
                    return GetGames();
                case "POST":
                case "PUT":
                    return PostGame();
                case "DELETE":
                //case "PATCH":
                    return DeleteGame();
                default:
                    Response.Headers.Add("Allow", "GET, POST, PUT, DELETE");
                    return StatusCode(405); // Method Not Allowed
            }
        }

        private IActionResult GetGames()
        {
            _gameListContext.CleanStaleGames();

            JObject Paramaters = new JObject();
            Request.Query.ToList().ForEach(query =>
            {
                Paramaters[query.Key] = query.Value.ToString();
            });

            IGameListModule Plugin = null;
            Plugin = _gameListModuleManager.GetLikelyPlugins(Paramaters, Request.Path.Value, Request.Method).FirstOrDefault();

            // transform GET paramaters to match default logic
            Plugin?.TransformGetParamaters(ref Paramaters);

            // check gameId
            string __gameId = Paramaters["__gameId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(__gameId) && !s_AllowEmptyGameID)
                return StatusCode(400); // Bad Request

            // check geoIP
            string geoIP = Paramaters["__geoIP"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(geoIP))
                geoIP = Request.HttpContext.Connection.RemoteIpAddress?.ToString();

            // excluded data columns
            List<string> excludedColumns = new List<string>();
            string strExcludedCols = Paramaters["__excludeCols"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(strExcludedCols))
                excludedColumns.AddRange(strExcludedCols.Split(','));

            JObject responseObject = new JObject();
            JArray GameArray = new JArray();
            responseObject["GET"] = GameArray;

            List<GameData> RawGames = _gameListContext.GetGames(__gameId).ToList();
            Dictionary<string, JObject> ExtraResponseData = new Dictionary<string, JObject>();

            Plugin?.PreProcessGameList(ref Paramaters, ref RawGames, ref ExtraResponseData);

            RawGames.ForEach(dr =>
            {
                JObject obj = new JObject();
                if (!excludedColumns.Contains("__gameId")) obj["__gameId"] = dr.gameId;
                if (!excludedColumns.Contains("__rowId")) obj["__rowId"] = dr.rowId;
                //if (!excludedColumns.Contains("__updatePW")) obj["__updatePW"] = dr.updatePw;
                if (!excludedColumns.Contains("__addr")) obj["__addr"] = dr.addr;
                //if (!excludedColumns.Contains("__clientReqId")) obj["__clientReqId"] = dr.clientReqId;
                if (!excludedColumns.Contains("__timeoutSec")) obj["__timeoutSec"] = dr.timeoutSec;

                foreach (CustomGameDataField pair in dr.GameAttributes)
                    if (!excludedColumns.Contains(pair.Key))
                        obj[pair.Key] = JToken.Parse(pair.Value);

                foreach (var pair in dr.CustomAttributes)
                    if (!excludedColumns.Contains(pair.Key))
                        obj[pair.Key] = pair.Value;

                GameArray.Add(obj);
            });

            responseObject["requestURL"] = $"{Request.HttpContext.Request.Host}{Request.HttpContext.Request.Path}{Request.HttpContext.Request.QueryString}";

            if (Plugin != null)
                responseObject["plugin"] = Plugin.DisplayName.ToString();

            foreach(var pair in ExtraResponseData)
                responseObject[pair.Key] = pair.Value;

            Plugin?.TransformGetResponse(ref responseObject);

            return Json(responseObject);
        }

        private IActionResult PostGame()
        {
            using (var reader = new StreamReader(Request.Body))
            {
                JObject postedObject;
                try
                {
                    // read posted data
                    postedObject = JObject.Parse(reader.ReadToEnd());
                }
                catch
                {
                    return StatusCode(400); // Bad Request
                }

                IGameListModule Plugin = null;
                Plugin = _gameListModuleManager.GetLikelyPlugins(postedObject, Request.Path.Value, Request.Method).FirstOrDefault();

                Plugin?.TransformPostParamaters(ref postedObject);

                /**
                * __gameId
                * Optional: Depends on server setting. Not optional on public server.
                * Default: If optional, defaults to an unnamed game.
                * This is a unique identifier for your game, of your choosing.If __gameId is 
                * unknown, the server will either create it or fail, depending on the server
                * setting.On the public server, the server will create it.You may specify
                * passwords for this game on creation with the control fields __updatePW and __readPW.
                **/
                string inputGameId = postedObject["__gameId"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(inputGameId))
                {
                    if (!s_AllowEmptyGameID)
                        return StatusCode(400); // Bad Request
                    inputGameId = string.Empty;
                }

                /**
                * __clientReqId
                * Optional: Yes
                * Default: NIL.
                * The intent of __clientReqId is if you have multiple games on the same computer, 
                * you can choose which game to update or delete on a subsequent request.On
                * success, the value passed to __clientReqId is returned to you, along with
                * __gameId and __rowId of the row and game added or updated. While optional, if
                * you do not pass __clientReqId there is no way to know what __rowId was assigned
                * to your game, so no way to later update or delete the row.
                **/
                long inputClientReqId = -1;
                if (postedObject["__clientReqId"] != null)
                    inputClientReqId = postedObject["__clientReqId"].Value<long>();


                /**
                * __timeoutSec
                * Optional: Yes
                * Default: 60 seconds
                * Minimum: 15 seconds
                * Maximum: 300 seconds on the public test server. 900 seconds on private servers.
                * This parameter controls how long your game will be listed until it is deleted by
                * the server.You must execute POST or PUT at least this often for your server to
                * maintain continuous visibility. If your server crashes, then for the remainder
                * of the timeout the server will be listed but unconnectable.
                **/
                int inputTimeoutSec = s_TimeoutDefault; // default
                if (postedObject["__timeoutSec"] != null)
                {
                    if (!int.TryParse(postedObject["__timeoutSec"].Value<string>(), out inputTimeoutSec))
                    {
                        inputTimeoutSec = s_TimeoutDefault; // reinforce default
                    }
                }
                //if (inputTimeoutSec > s_TimeoutMax) inputTimeoutSec = s_TimeoutMax; // 900 on private list servers
                //if (inputTimeoutSec < s_TimeoutMin) inputTimeoutSec = s_TimeoutMin;

                if ((inputTimeoutSec > s_TimeoutMax) || (inputTimeoutSec < s_TimeoutMin))
                    return StatusCode(400); // Bad Request


                /**
                * __geoIP
                * Optional: Yes
                * Default: Whatever IP you connected to the server with (See __addr)
                * This parameter allows you to override what IP address is used for Geographic
                * lookup.You will get more accurate results if you do a traceroute to your ISP,
                * and pass that IP address with __geoIP, rather than letting the system determine
                * your IP automatically.
                **/
                string geoIP = Request.Query["__geoIP"];
                //string geoIP = __geoIP;
                if (geoIP != null && !IsValidIP(geoIP))
                    geoIP = null;


                /**
                * __rowPW
                * Optional: Yes
                * Default: NIL.
                * If __rowPW was specified when the row was created, you must also specify this 
                * value to update the row when using __rowId. The purpose of this value is to 
                * prevent players of other games from updating your own row. If a row required a 
                * password but it was not specified, or the password was wrong, error code 401 
                * will be returned.
                **/
                string inputRowPW = postedObject["__rowPW"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(inputRowPW))
                    inputRowPW = null;


                // Game level password for reading.
                // Not much point as it can just be wiresharked.
                // Seems to never change for a given game.
                /**
                * __readPW
                * Optional: Yes
                * Default: Empty string / no password.
                * This password is used for the GET operation. If specified when the a new game is 
                * created, this field specifies what password to set for future requests.
                **/
                //string inputReadPW = postedObject["__readPW"].Value<string>();
                //if (inputReadPW == null || inputReadPW.Length == 0)
                //{
                //    inputReadPW = string.Empty;
                //}


                // Probably another game level password but for writing.
                // Not much point as it can just be wiresharked.
                // Seems to never change for a given game.
                /**
                * __updatePW
                * Optional: Yes
                * Default: Empty string / no password.
                * This password is used for POST, PUT, and DELETE operations. If specified when 
                * the a new game is created, this field specifies what password to set for future 
                * requests.
                **/
                //string inputUpdatePW = postedObject["__updatePW"].Value<string>();
                //if (inputUpdatePW == null || inputUpdatePW.Length == 0)
                //{
                //    inputUpdatePW = string.Empty;
                //}


                // process input variables
                string inputAddr = Request.HttpContext.Connection.RemoteIpAddress?.ToString();


                /**
                * __rowId
                * Optional: Yes
                * Default: NIL.
                * If specified, a row with this ID will be overwritten, instead of creating a new 
                * row. After uploading a row the first time, you should use this __rowId on 
                * subsequent POST / PUT requests for the same game.
                **/
                long inputRowId = -1;
                if (postedObject["__rowId"] != null)
                    inputRowId = postedObject["__rowId"].Value<long>();

                // prepare variables for holding check data
                long lookupRowId = -1;
                string lookupRowPw = string.Empty;

                if (inputRowId < 0)
                {
                    // no input row ID, so this game is either new or something's gone wrong, try to grab a rowId and rowPw
                    // this is a special feature of our implementation, though it was taken from the kebbz gamelist php implementation
                    // this might be removed, it existed on the php list for easier injection of games from what I could tell
                    GameData dat = _gameListContext.CheckGame(inputAddr, inputClientReqId);
                    if (dat != null)
                    {
                        lookupRowId = dat.rowId;
                        lookupRowPw = dat.rowPW;
                    }
                }
                else
                {
                    // grab the existing game's rowPw
                    GameData dat = _gameListContext.CheckGame(inputRowId);
                    if (dat != null)
                    {
                        lookupRowId = dat.rowId;
                        lookupRowPw = dat.rowPW;
                    }
                }

                // no game already exists
                if ((lookupRowId < 0) || ((inputRowPW == null && lookupRowPw == null) || (inputRowPW == lookupRowPw)))
                {
                    // process custom fields
                    Dictionary<string, string> customValues = new Dictionary<string, string>();
                    postedObject.Properties().ToList().ForEach(dr =>
                    {
                        if (!dr.Name.StartsWith("__") && dr.Value.Type != JTokenType.Null)
                            customValues[dr.Name] = (dr.Value.Type == JTokenType.String ? ("\"" + dr.Value.ToString() + "\"") : dr.Value.ToString());
                    });

                    GameData tmpGame = null;
                    if (lookupRowId < 0)
                    {
                        // create game
                        tmpGame = _gameListContext.AddGame(inputGameId, DateTime.UtcNow, inputTimeoutSec, inputRowPW, inputClientReqId, inputAddr, customValues);
                    }
                    else if ((inputRowPW == null && lookupRowPw == null) || (inputRowPW == lookupRowPw))
                    {
                        // update game
                        tmpGame = _gameListContext.UpdateGame(lookupRowId, DateTime.UtcNow, inputTimeoutSec, inputClientReqId, inputAddr, customValues);
                    }

                    if (tmpGame == null)
                        return StatusCode(500); // Error

                    PostGameResponse retVal = new PostGameResponse()
                    {
                        POST = new Dictionary<string, JToken>()
                        {
                            { "__clientReqId", inputClientReqId},
                            { "__rowId", lookupRowId},
                            { "__gameId", inputGameId},
                        }
                    };

                    Plugin?.TransformPostResponse(ref retVal);

                    return Json(retVal);
                }
                else if (inputRowPW != lookupRowPw)
                {
                    return StatusCode(401); // Unauthorized
                }
                else
                {
                    return StatusCode(400); // Bad Request
                }
            }
        }

        private IActionResult DeleteGame()
        {
            JObject Paramaters = new JObject();
            Request.Query.ToList().ForEach(query =>
            {
                Paramaters[query.Key] = query.Value.ToString();
            });

            // If there's a post body, use it overriding the query string
            try
            {
                using (var reader = new StreamReader(Request.Body))
                {
                    // read posted data
                    JObject postedObject = JObject.Parse(reader.ReadToEnd());
                    postedObject.Properties().ToList().ForEach(property =>
                    {
                        try
                        {
                            Paramaters[property.Name] = property.Value.Value<string>();
                        }
                        catch { }
                    });
                }
            }
            catch { }

            IGameListModule Plugin = null;
            Plugin = _gameListModuleManager.GetLikelyPlugins(Paramaters, Request.Path.Value, Request.Method).FirstOrDefault();
            
            // transform DELETE paramaters to match default logic
            Plugin?.TransformDeleteParamaters(ref Paramaters);

            // check input rowId
            string rawRowId = Paramaters["__rowId"].Value<string>();
            if (string.IsNullOrWhiteSpace(rawRowId))
                return StatusCode(400); // Bad Request

            // process input rowId
            long lookupRowId = -1;
            if (!long.TryParse(rawRowId, out lookupRowId))
                return StatusCode(400); // Bad Request

            GameData tmpDat = _gameListContext.CheckGame(lookupRowId);
            if (tmpDat == null)
                return StatusCode(400);


            // process input rowPw
            string inputRowPw = Paramaters["__rowPW"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(inputRowPw))
                inputRowPw = string.Empty;

            // prepare variables for holding check data
            string lookupRowPw = string.Empty;

            // get RowPw for game
            lookupRowPw = tmpDat.rowPW;

            if (tmpDat.rowId < 0)
                return StatusCode(400); // Bad Request

            if (lookupRowPw == null)
                lookupRowPw = string.Empty;

            if (inputRowPw == lookupRowPw)
            {
                // delete the game
                _gameListContext.DeleteGame(tmpDat.rowId);
                return StatusCode(200); // OK
            }
            else
            {
                return StatusCode(401); // Unauthorized
            }
        }


        private static bool IsValidIP(string address)
        {
            if (!Regex.IsMatch(address, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b"))
                return false;

            IPAddress dummy;
            return IPAddress.TryParse(address, out dummy);
        }
    }
}
