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
        public PostGameSubResponse POST { get; set; }
    }

    public class PostGameSubResponse
    {
        public long __clientReqId { get; set; }
        public long __rowId { get; set; }
        public string __gameId { get; set; }
    }

    public class Master2Controller : Controller
    {
        private readonly GameListContext _gameListContext;
        private readonly IGameListModuleManager _gameListModuleManager;

        //Object _GamelistNullLock = new Object();

        bool s_AllowEmptyGameID = false;
        int s_TimeoutDefault = 60;
        int s_TimeoutMin = 15;
        int s_TimeoutMax = 300; // 900 on private list servers

        public Master2Controller(GameListContext gameListContext, IGameListModuleManager gameListModuleManager)
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
            switch (Request.Method)
            {
                case "GET":
                    return GetGames();
                case "POST":
                case "PUT":
                    return PostGame();
                case "DELETE":
                    return DeleteGame();
                default:
                    Response.Headers.Add("Allow", "GET, POST, PUT, DELETE");
                    return StatusCode(405); // Method Not Allowed
            }
        }

        private IActionResult GetGames()
        {
            _gameListContext.CleanStaleGames();

            string __gameId = Request.Query["__gameId"];
            if (__gameId == null || __gameId.Length == 0)
            {
                //context.Response.StatusCode = 400; // Bad Request
                //return;
                return StatusCode(400);
            }

            IGameListModule Plugin = null;
            //((Global)(context.ApplicationInstance)).GameListPlugins.TryGetValue(__gameId, out Plugin);
            _gameListModuleManager.GameListPlugins.TryGetValue(__gameId, out Plugin);

            Microsoft.AspNetCore.Http.IQueryCollection QueryString = Request.Query;

            if (Plugin != null) Plugin.InterceptQueryStringForGet(ref QueryString);

            string geoIP = QueryString["__geoIP"];
            if (geoIP == null || geoIP.Length == 0)
            {
                geoIP = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
            }

            List<string> excludedColumns = new List<string>();
            string strExcludedCols = QueryString["__excludeCols"];
            if (strExcludedCols != null && strExcludedCols.Length > 0)
            {
                excludedColumns.AddRange(strExcludedCols.Split(','));
            }

            JObject responseObject = new JObject();

            JArray GameArray = new JArray();
            responseObject["GET"] = GameArray;

            List<GameData> RawGames = _gameListContext.GetGames(__gameId).ToList();
            Dictionary<string, JObject> ExtraData = new Dictionary<string, JObject>();

            if (Plugin != null) Plugin.PreProcessGameList(QueryString, ref RawGames, ref ExtraData);

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
                {
                    if (!excludedColumns.Contains(pair.Key))
                        obj[pair.Key] = pair.Value;
                }

                foreach (var pair in dr.CustomAttributes)
                {
                    if (!excludedColumns.Contains(pair.Key))
                        obj[pair.Key] = pair.Value;
                }

                GameArray.Add(obj);
            });

            //// holdover from when the rewrite engine was used rather than directly setting this handler
            //if (context.Request.ServerVariables["HTTP_X_ORIGINAL_URL"] != null)
            //{
            //    responseObject["requestURL"] = context.Request.Url.GetLeftPart(UriPartial.Authority) + context.Request.ServerVariables["HTTP_X_ORIGINAL_URL"]; 
            //}
            //else
            //{
            //    responseObject["requestURL"] = context.Request.Url.ToString();
            //}

            responseObject["requestURL"] = $"{Request.HttpContext.Request.Host}{Request.HttpContext.Request.Path}{Request.HttpContext.Request.QueryString}";

            if (Plugin != null)
                responseObject["plugin"] = Plugin.DisplayName.ToString();

            foreach(var pair in ExtraData)
            {
                responseObject[pair.Key] = pair.Value;
            }

            //Response.Write(responseObject.ToString(Newtonsoft.Json.Formatting.None));
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
                    //context.Response.StatusCode = 400; // Bad Request
                    //return;
                    return StatusCode(400);
                }

                /**
                * __gameId
                * Optional: Depends on server setting. Not optional on public server.
                * Default: If optional, defaults to an unnamed game.
                * This is a unique identifier for your game, of your choosing.If __gameId is 
                * unknown, the server will either create it or fail, depending on the server
                * setting.On the public server, the server will create it.You may specify
                * passwords for this game on creation with the control fields __updatePW and __readPW.
                **/
                string inputGameId = postedObject["__gameId"].Value<string>();
                if (inputGameId == null || inputGameId.Length == 0)
                {
                    if (s_AllowEmptyGameID)
                    {
                        inputGameId = string.Empty;
                    }
                    else
                    {
                        //Response.StatusCode = 400; // Bad Request
                        //return;
                        return StatusCode(400);
                    }
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
                {
                    inputClientReqId = postedObject["__clientReqId"].Value<long>();
                }


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
                {
                    //Response.StatusCode = 400; // Bad Request
                    //return;
                    return StatusCode(400);
                }


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
                {
                    geoIP = null;
                }


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
                string inputRowPW = postedObject["__rowPW"].Value<string>();
                if (inputRowPW == null || inputRowPW.Length == 0)
                {
                    inputRowPW = null;
                }


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
                {
                    inputRowId = postedObject["__rowId"].Value<long>();
                }

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
                if (lookupRowId < 0)
                {
                    // process custom fields
                    Dictionary<string, string> customRowFields = new Dictionary<string, string>();
                    postedObject.Properties().ToList().ForEach(dr =>
                    {
                        if (!dr.Name.StartsWith("__"))
                        {
                            customRowFields[dr.Name] = dr.Value.Value<string>();
                        }
                    });

                    // insert new game, get the rowId created
                    var tmpGame = _gameListContext.AddGame(inputGameId, DateTime.UtcNow, inputTimeoutSec, inputRowPW, inputClientReqId, inputAddr, customRowFields);
                    if (tmpGame != null)
                    {
                        lookupRowId = tmpGame.rowId;
                    }
                }
                else if ((inputRowPW == null && lookupRowPw == null) || (inputRowPW == lookupRowPw))
                {
                    // process custom fields
                    Dictionary<string, string> customValues = new Dictionary<string, string>();
                    postedObject.Properties().ToList().ForEach(dr =>
                    {
                        if (!dr.Name.StartsWith("__"))
                        {
                            customValues[dr.Name] = dr.Value.Value<string>();
                        }
                    });

                    // update game
                    _gameListContext.UpdateGame(lookupRowId, DateTime.UtcNow, inputTimeoutSec, inputClientReqId, inputAddr, customValues);
                }
                else if (inputRowPW != lookupRowPw)
                {
                    //Response.StatusCode = 401; // Unauthorized
                    //return;
                    return StatusCode(401);
                }
                else
                {
                    //Response.StatusCode = 400; // Bad Request
                    //return;
                    return StatusCode(400);
                }

                // building response object
                //JObject responseObject = new JObject();
                //JObject responseSubObject = new JObject();
                //responseObject["POST"] = responseSubObject;

                // send the data the caller needs to update this game later
                //responseSubObject["__clientReqId"] = inputClientReqId;
                //responseSubObject["__rowId"] = lookupRowId;
                //responseSubObject["__gameId"] = inputGameId;

                PostGameResponse retVal = new PostGameResponse()
                {
                    POST = new PostGameSubResponse()
                    {
                        __clientReqId = inputClientReqId,
                        __rowId = lookupRowId,
                        __gameId = inputGameId
                    }
                };

                // write response
                return Json(retVal);
            }
        }

        private IActionResult DeleteGame()
        {
            Dictionary<string, string> paramaters = new Dictionary<string, string>();

            Request.Query.ToList().ForEach(query =>
            {
                paramaters[query.Key] = query.Value;
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
                            paramaters[property.Name] = property.Value<string>();
                        }
                        catch { }
                    });
                }
            }
            catch { }

            // check input rowId
            string rawRowId = paramaters["__rowId"];
            if (rawRowId == null || rawRowId.Length == 0)
            {
                //Response.StatusCode = 400; // Bad Request
                //return;
                return StatusCode(400);
            }

            // process input rowId
            long inputRowId = -1;
            if (!long.TryParse(rawRowId, out inputRowId))
            {
                //context.Response.StatusCode = 400; // Bad Request
                //return;
                return StatusCode(400);
            }

            // process input rowPw
            string inputRowPw = paramaters["__rowPW"];
            //if (inputRowPw == null || inputRowPw.Length == 0)
            //{
            //    context.Response.StatusCode = 400; // Bad Request
            //    return;
            //}
            if (inputRowPw == null)
            {
                inputRowPw = string.Empty;
            }

            // prepare variables for holding check data
            string lookupRowPw = string.Empty;

            // get RowPw for game
            GameData tmpDat = _gameListContext.CheckGame(inputRowId);
            if (tmpDat != null)
            {
                inputRowId = tmpDat.rowId;
                lookupRowPw = tmpDat.rowPW;
            }

            if (inputRowId < 0)
            {
                //Response.StatusCode = 400; // Bad Request
                //return;
                return StatusCode(400);
            }

            if (lookupRowPw == null)
            {
                lookupRowPw = string.Empty;
            }

            if (inputRowPw == lookupRowPw)
            {
                // delete the game
                //_gameListContext.DeleteGame(inputRowId);
                return StatusCode(200);
            }
            else
            {
                //Response.StatusCode = 401; // Unauthorized
                //return;
                return StatusCode(401);
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
