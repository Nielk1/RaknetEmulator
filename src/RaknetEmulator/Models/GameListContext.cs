using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RaknetEmulator.Models
{
    public class GameListContext : DbContext
    {
        static object Locker = new object();

        public GameListContext(DbContextOptions<GameListContext> options) : base(options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GameData>(b =>
            {
                b.HasKey(e => e.rowId);
                b.Property(e => e.rowId).ValueGeneratedOnAdd();
                b.Ignore(e => e.CustomAttributes);
            });

            modelBuilder.Entity<CustomGameDataField>(b =>
            {
                b.HasKey(e => e.gameCustFieldId);
                b.Property(e => e.gameCustFieldId).ValueGeneratedOnAdd();
            });
        }

        public DbSet<GameData> Games { get; set; }
        public DbSet<CustomGameDataField> GameAttributes { get; set; }

        public void CleanStaleGames()
        {
            lock (Locker)
            {
                var gamesFound = this.Games.Where(dr => dr.lastUpdate.AddSeconds(dr.timeoutSec) <= DateTime.UtcNow);
                var gameAttributesFound = gamesFound.SelectMany(dr => dr.GameAttributes);
                this.GameAttributes.RemoveRange(gameAttributesFound);
                this.Games.RemoveRange(gamesFound);
                SaveChanges();
            }
        }

        public IEnumerable<GameData> GetGames(string __gameId)
        {
            lock (Locker)
            {
                var FoundGames = this.Games.Where(dr => dr.gameId == __gameId).ToList();
                foreach (var game in FoundGames)
                {
                    game.GameAttributes = GameAttributes.Where(gameAttribute => gameAttribute.GameDataRowId == game.rowId).ToList();
                }
                return FoundGames;
            }
        }

        public GameData AddGame(string gameId, DateTime lastUpdate, int timeoutSec, string rowPW, long clientReqId, string addr, Dictionary<string, string> customValues)
        {
            lock (Locker)
            {
                GameData game = new GameData() {
                    gameId = gameId,
                    lastUpdate = lastUpdate,
                    timeoutSec = timeoutSec,
                    rowPW = rowPW,
                    clientReqId = clientReqId,
                    addr = addr
                };
                Games.Add(game);
                //SaveChanges();
                //Entry(game).GetDatabaseValues();
                game.GameAttributes = customValues.ToList().Select(dr => new CustomGameDataField()
                {
                    Key = dr.Key,
                    Value = dr.Value,
                    GameData = game,
                    GameDataRowId = game.rowId
                }).ToList();
                GameAttributes.AddRange(game.GameAttributes);
                SaveChanges();
                return game;
            }
        }

        public void DeleteGame(long rowId)
        {
            lock (Locker)
            {
                var gamesFound = this.Games.Where(dr => dr.rowId == rowId);
                var gameAttributesFound = gamesFound.SelectMany(dr => dr.GameAttributes);
                this.GameAttributes.RemoveRange(gameAttributesFound);
                this.Games.RemoveRange(gamesFound);
                SaveChanges();
            }
        }

        public GameData UpdateGame(long rowId, DateTime lastUpdate, int timeoutSec, long clientReqId, string addr, Dictionary<string, string> customValues)
        {
            lock (Locker)
            {
                GameData game = this.Games.Where(dr => dr.rowId == rowId).FirstOrDefault();
                //this.GameAttributes.RemoveRange(game.GameAttributes);
                //game.GameAttributes.Clear();
                this.GameAttributes.RemoveRange(this.GameAttributes.Where(dr => dr.GameDataRowId == game.rowId));
                game.lastUpdate = lastUpdate;
                game.timeoutSec = timeoutSec;
                game.clientReqId = clientReqId;
                game.addr = addr;
                game.GameAttributes = customValues.ToList().Select(dr => new CustomGameDataField() {
                    Key = dr.Key,
                    Value = dr.Value,
                    GameData = game,
                    GameDataRowId = game.rowId
                }).ToList();
                GameAttributes.AddRange(game.GameAttributes);
                SaveChanges();

                return game;
            }
        }

        public GameData CheckGame(string addr, long? clientReqId)
        {
            lock (Locker)
            {
                return this.Games.Where(dr => dr.addr == addr && (!clientReqId.HasValue || dr.clientReqId == clientReqId.Value)).FirstOrDefault();
            }
        }

        public GameData CheckGame(long rowId)
        {
            lock (Locker)
            {
                return this.Games.Where(dr => dr.rowId == rowId).FirstOrDefault();
            }
        }
    }
}
