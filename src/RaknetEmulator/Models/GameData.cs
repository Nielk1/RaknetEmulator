using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RaknetEmulator.Models
{
    public class GameData
    {
        //[Key]
        public long rowId { get; set; }

        public string gameId { get; set; }
        public string addr { get; set; }
        public long? clientReqId { get; set; }
        public long timeoutSec { get; set; }
        //public string updatePw; //"PandemicRIP"
        public DateTime lastUpdate { get; set; }
        public string rowPW { get; set; }

        public List<CustomGameDataField> GameAttributes { get; set; }
        public Dictionary<string, JToken> CustomAttributes { get; set; }

        public GameData()
        {
            GameAttributes = new List<CustomGameDataField>();
            CustomAttributes = new Dictionary<string, JToken>();
        }
    }

    public class CustomGameDataField
    {
        //[Key]
        public string gameCustFieldId { get; set; }

        public string Key { get; set; }
        public JToken Value { get; set; }

        public long GameDataRowId { get; set; }
        public GameData GameData { get; set; }

        public CustomGameDataField() { }
    }
}
