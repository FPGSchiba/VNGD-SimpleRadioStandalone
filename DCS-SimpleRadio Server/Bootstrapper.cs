using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Server.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Server.UI.ClientAdmin;
using Ciribob.DCS.SimpleRadio.Standalone.Server.UI.MainWindow;
using NLog;
using NLog.Config;
using NLog.Targets;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server
{
    public class Bootstrapper : BootstrapperBase
    {
        private readonly SimpleContainer _simpleContainer = new SimpleContainer();

        public Bootstrapper()
        {
            Initialize();
            SetupLogging();

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            Analytics.Log("Server", "Startup", Guid.NewGuid().ToString());
        }

        private void SetupLogging()
        {
            var config = new LoggingConfiguration();

          //  var consoleTarget = new ColoredConsoleTarget {Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}"};
            //config.AddTarget("console", consoleTarget);

            var fileTarget = new FileTarget
            {
                FileName = "${basedir}/serverlog.txt",
                Layout = @"${longdate} | ${logger} | ${message} ${exception:format=toString,Data:maxInnerExceptionLevel=1}"
            };
            config.AddTarget("file", fileTarget);

//            var rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
//            config.LoggingRules.Add(rule1);

            var rule2 = new LoggingRule("*", LogLevel.Info, fileTarget);
            config.LoggingRules.Add(rule2);

            LogManager.Configuration = config;
        }


        protected override void Configure()
        {
            _simpleContainer.Singleton<IWindowManager, WindowManager>();
            _simpleContainer.Singleton<IEventAggregator, EventAggregator>();
            _simpleContainer.Singleton<ServerState>();

            _simpleContainer.Singleton<MainViewModel>();
            _simpleContainer.Singleton<ClientAdminViewModel>();
        }

        protected override object GetInstance(Type service, string key)
        {
            var instance = _simpleContainer.GetInstance(service, key);
            if (instance != null)
                return instance;

            throw new InvalidOperationException("Could not locate any instances.");
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _simpleContainer.GetAllInstances(service);
        }


        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            IDictionary<string, object> settings = new Dictionary<string, object>
            {
                {"Icon", new BitmapImage(new Uri("pack://application:,,,/SR-Server;component/server-10.ico"))},
                {"ResizeMode", ResizeMode.CanMinimize}
            };
            //create an instance of serverState to actually start the server
            _simpleContainer.GetInstance(typeof(ServerState), null);

            DisplayRootViewFor<MainViewModel>(settings);

            UpdaterChecker.CheckForUpdate();
        }

        protected override void BuildUp(object instance)
        {
            _simpleContainer.BuildUp(instance);
        }


        protected override void OnExit(object sender, EventArgs e)
        {
            var serverState = (ServerState) _simpleContainer.GetInstance(typeof(ServerState), null);
            serverState.StopServer();
        }
    }
}