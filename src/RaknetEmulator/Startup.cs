using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RaknetEmulator.Models;
using Microsoft.EntityFrameworkCore;
using RaknetEmulator.Plugins;
using Newtonsoft.Json.Serialization;
using Microsoft.Extensions.Hosting;

namespace RaknetEmulator
{
    public class Startup
    {
        private string _contentRootPath = "";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddControllers();
            //services.AddMvc();
                //.AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver());

            services.Configure<IISOptions>(options =>
            {
                options.ForwardClientCertificate = false;
            });

            //_contentRootPath = Configuration["Data:RaknetDB:ContentRootPath"];
            string conn = Configuration["Data:RaknetDB:ConnectionString"];
            if (conn.Contains("%CONTENTROOTPATH%"))
            {
                conn = conn.Replace("%CONTENTROOTPATH%", _contentRootPath);
            }
            //services.AddDbContext<GameListContext>(options => options.UseSqlServer(conn));
            services.AddDbContext<GameListContext>(options => options.UseSqlite(conn));

            services.AddSingleton<IConfiguration>(Configuration);
            services.AddSingleton<GameListModuleManager>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            //loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            //loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                //app.UseBrowserLink();
            }
            else
            {
                //app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseRouting();

            //app.UseMvc();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "",
                    defaults: new { controller = "Master2", action = "Index" });

                //routes.MapRoute(
                //    name: "testServer",
                //    template: "testServer",
                //    defaults: new { controller = "Master2", action = "GameList" });

                //routes.MapRoute(
                //    name: "lobbyServer",
                //    template: "lobbyServer",
                //    defaults: new { controller = "Master2", action = "GameList", defaultgameid = "BZCC" });

                endpoints.MapControllerRoute(
                    name: "GameList",
                    pattern: "{*url}",
                    defaults: new { controller = "Master2", action = "GameList" });
            });
        }
    }
}
