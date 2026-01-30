using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.PINS.Drivers;
using System.ComponentModel.Composition;

namespace NINA.PINS {

    [Export(typeof(IPluginManifest))]
    public class PINS : PluginBase {
        public static IWeatherDataMediator WeatherDataMediator { get; private set; }
        public static PowerBoxDriver ConnectedPowerBox { get; set; }

        [ImportingConstructor]
        public PINS(IWeatherDataMediator weatherDataMediator) {
            WeatherDataMediator = weatherDataMediator;
        }
    }
}