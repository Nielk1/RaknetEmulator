using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using RaknetEmulator.Models;

namespace RaknetEmulator.Migrations
{
    [DbContext(typeof(GameListContext))]
    partial class GameListContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.1.0-rtm-22752");

            modelBuilder.Entity("RaknetEmulator.Models.CustomGameDataField", b =>
                {
                    b.Property<string>("gameCustFieldId")
                        .ValueGeneratedOnAdd();

                    b.Property<long>("GameDataRowId");

                    b.Property<string>("Key");

                    b.Property<string>("Value");

                    b.HasKey("gameCustFieldId");

                    b.HasIndex("GameDataRowId");

                    b.ToTable("GameAttributes");
                });

            modelBuilder.Entity("RaknetEmulator.Models.GameData", b =>
                {
                    b.Property<long>("rowId")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("addr");

                    b.Property<long?>("clientReqId");

                    b.Property<string>("gameId");

                    b.Property<DateTime>("lastUpdate");

                    b.Property<string>("rowPW");

                    b.Property<long>("timeoutSec");

                    b.HasKey("rowId");

                    b.ToTable("Games");
                });

            modelBuilder.Entity("RaknetEmulator.Models.CustomGameDataField", b =>
                {
                    b.HasOne("RaknetEmulator.Models.GameData", "GameData")
                        .WithMany("GameAttributes")
                        .HasForeignKey("GameDataRowId")
                        .OnDelete(DeleteBehavior.Cascade);
                });
        }
    }
}
