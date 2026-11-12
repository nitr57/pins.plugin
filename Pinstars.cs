using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.PINS.Drivers;
using NINA.PINS.SDK;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

namespace NINA.PINS {

    [Export(typeof(IPluginManifest))]
    public class PINS : PluginBase {
        public static IWeatherDataMediator WeatherDataMediator { get; private set; }
        public static PowerBoxDriver ConnectedPowerBox { get; set; }
        public static MeteoStationDriver ConnectedMeteoStation { get; set; }
        public static LensControlDriver ConnectedLensControl { get; set; }

        [ImportingConstructor]
        public PINS(IWeatherDataMediator weatherDataMediator) {
            WeatherDataMediator = weatherDataMediator;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        private static void OnProcessExit(object sender, EventArgs e) {
            // Ensure all SDKs release their serial ports even on ungraceful shutdown
            ConnectedLensControl?.Disconnect();
            ConnectedLensControl = null;
            ConnectedMeteoStation?.Disconnect();
            ConnectedMeteoStation = null;
            ConnectedPowerBox?.Disconnect();
            ConnectedPowerBox = null;

            // Fallback for an exit without plugin teardown: no SDK log callback into a runtime
            // that is shutting down
            PowerBoxSdkLog.Unregister();
        }

        public override Task Teardown() {
            // N.I.N.A. closes its logger after the plugin teardown: stop forwarding SDK messages
            PowerBoxSdkLog.Unregister();
            return base.Teardown();
        }
    }
}
