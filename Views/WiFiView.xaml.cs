using NINA.PINS.Drivers;
using System.Windows;
using System.Windows.Controls;

namespace NINA.PINS.Views {

    /// <summary>
    /// Interaktionslogik für WiFiView.xaml
    /// </summary>
    public partial class WiFiView : UserControl {

        public WiFiView() {
            InitializeComponent();
        }

        private void PasswordChanged(PasswordBox box, PowerBoxDriver driver, TextBlock textBlock, Button button) {
            if (driver != null && box != null) {
                // Validate
                if (box.Password.Length < 8 || box.Password.Length > 63) {
                    textBlock.Visibility = Visibility.Visible;
                    textBlock.Text = "WPA rule";
                    button.IsEnabled = false;
                    driver.WiFiPASS = string.Empty;
                    return;
                }

                // Hide error message
                textBlock.Visibility = Visibility.Collapsed;

                driver.WiFiPASS = box.Password;
                button.IsEnabled = true;
            }
        }

        private void Client_PasswordChanged(object sender, RoutedEventArgs e) {
            PasswordChanged(sender as PasswordBox, DataContext as PowerBoxDriver, errorLabel2, ConnectButton);
        }

        private void Hotspot_PasswordChanged(object sender, RoutedEventArgs e) {
            PasswordChanged(sender as PasswordBox, DataContext as PowerBoxDriver, errorLabel, HotspotButton);
        }
    }
}