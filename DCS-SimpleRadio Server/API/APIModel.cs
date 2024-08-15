using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Server.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Management;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.API
{
    internal class APIModel
    {
        private readonly IEventAggregator _eventAggregator;
        private volatile bool _stop = true;

        public APIModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            StartAPI();
        }

        private void StartAPI()
        {
            // Use this here: https://github.com/mkArtakMSFT/Samples/blob/master/WpfAspNetCore/MainWindow.xaml.cs
            // kinda shitty would be easier with TS...
        }
    }
}
