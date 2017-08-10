using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RaknetEmulator.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGeneration.DotNet;
using Microsoft.AspNet.Mvc.Infrastructure;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.Extensions.FileProviders;
using RaknetEmulator.Plugins;

namespace RaknetEmulator
{
    public class Startup
    {
        private string _contentRootPath = "";

        public Startup(IHostingEnvironment env)
        {
            _contentRootPath = env.ContentRootPath;

            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc();

            string conn = Configuration["Data:RaknetDB:ConnectionString"];
            if (conn.Contains("%CONTENTROOTPATH%"))
            {
                conn = conn.Replace("%CONTENTROOTPATH%", _contentRootPath);
            }
            //services.AddDbContext<GameListContext>(options => options.UseSqlServer(conn));
            services.AddDbContext<GameListContext>(options => options.UseSqlite(conn));

            services.AddSingleton<IConfiguration>(Configuration);
            services.AddSingleton<IGameListModuleManager, GameListModuleManager>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }
            else
            {
                //app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "",
                    defaults: new { controller = "Master2", action = "Index" });

                routes.MapRoute(
                    name: "testServer",
                    template: "testServer",
                    defaults: new { controller = "Master2", action = "GameList" });
            });
        }
    }
}
