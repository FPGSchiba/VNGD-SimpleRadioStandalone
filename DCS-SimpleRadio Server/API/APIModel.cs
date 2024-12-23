using System.Threading.Tasks;
using Caliburn.Micro;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.API
{
    internal class APIModel
    {
        private readonly IEventAggregator _eventAggregator;
        private static readonly Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public APIModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            Logger.Debug("Starting HTTP Server");

            StartAPI();
        }

        private void StartAPI()
        {
            Task.Factory.StartNew(() =>
            {
                var builder = CreateWebHostBuilder(new string[] { });
                builder.ConfigureLogging((loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.AddNLog();
                }));
                var app = builder.Build();

                app.Run();
            });
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            return WebHost.CreateDefaultBuilder(args)
                .UseUrls("http://*:5000")
                .UseStartup<Startup>();
        }
    }
}
