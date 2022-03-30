using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using OpenTK.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    /// Interaction logic for AxisBindingControl.xaml
    /// </summary>
    public partial class AxisBindingControl : UserControl
    {
        private InputDeviceManager _inputDeviceManager;
        public InputBinding ControlInputBinding { get; set; }
        public string InputName { get; set; }

        public AxisBindingControl()
        {
            InitializeComponent();
        }

        private void On_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateGraphPoints();
        }

        public InputDeviceManager InputDeviceManager
        {
            get { return _inputDeviceManager; }
            set
            {
                _inputDeviceManager = value;
                LoadInputSettings();
            }
        }

        public void LoadInputSettings()
        {
            DeviceLabel.Content = InputName;

            var currentInputProfile = GlobalSettingsStore.Instance.ProfileSettingsStore.GetCurrentInputProfile();

            if (currentInputProfile != null)
            {
                var devices = currentInputProfile;
                if (currentInputProfile.ContainsKey(ControlInputBinding)
                    && devices[ControlInputBinding] is InputAxisDevice axisDevice)
                {
                    Device.Text = axisDevice.DeviceName.Substring(0, 18);
                    DeviceText.Text = axisDevice.Axis;
                    CurvatureSlider.Value = axisDevice.Curvature;
                }
                else
                {
                    TuningExpander.IsEnabled = false;
                    TuningExpander.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                DeviceText.Text = "None";
                Device.Text = "None";
            }
        }


        private void DeviceButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceClear.IsEnabled = false;
            DeviceButton.IsEnabled = false;

            InputDeviceManager.AssignAxis(device =>
            {
                DeviceClear.IsEnabled = true;
                DeviceButton.IsEnabled = true;

                Device.Text = device.DeviceName.Substring(0, 20);
                DeviceText.Text = device.Axis + " Axis";

                device.InputBind = ControlInputBinding;
                TuningExpander.IsEnabled = true;
                TuningExpander.Visibility = Visibility.Visible;

                UpdateGraphPoints();
                GlobalSettingsStore.Instance.ProfileSettingsStore.SetControlSetting(device);
            });
        }

        private void DeviceClear_Click(object sender, RoutedEventArgs e)
        {
            GlobalSettingsStore.Instance.ProfileSettingsStore.RemoveControlSetting(ControlInputBinding);

            Device.Text = "None";
            DeviceText.Text = "None";
            TuningExpander.IsEnabled = false;
            TuningExpander.Visibility = Visibility.Collapsed;
        }

        private void UpdateGraphPoints()
        {
            AxisVisualisation.Children.Clear();
            double[] xValues = Enumerable.Range(0, (int)AxisVisualisation.ActualWidth+1).Select(x => (double)x/AxisVisualisation.ActualWidth).ToArray();
            double[] yValues = xValues.Select(x => 1 - AxisTuningHelper.GetCurvaturePointValue(
                x, CurvatureSlider.Value, Inverted.IsChecked is true)).ToArray();

            if(yValues.Length == 0)
            {
                return;
            }

            PathFigure path = new PathFigure();
            path.StartPoint = new Point(xValues[0] * AxisVisualisation.ActualWidth, yValues[0] * AxisVisualisation.ActualHeight);
            PathSegmentCollection paths = new PathSegmentCollection();

            for (int i = 1; i < xValues.Length; i++)
            { 
                LineSegment segment = new LineSegment();
                segment.Point = new Point(xValues[i] * AxisVisualisation.ActualWidth, yValues[i] * AxisVisualisation.ActualHeight);
                paths.Add(segment);
            }

            path.Segments = paths;
            PathFigureCollection pathFigures = new PathFigureCollection();
            pathFigures.Add(path);

            PathGeometry pathGeometry = new PathGeometry();
            pathGeometry.Figures = pathFigures;

            Path finalPath = new Path();
            finalPath.Stroke = Brushes.Black;
            finalPath.StrokeThickness = 1;
            finalPath.Data = pathGeometry;

            AxisVisualisation.Children.Add(finalPath);
        }

        private void CurvatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Slider slider = sender as Slider;
            _inputDeviceManager.UpdateAxisTune(ControlInputBinding, slider.Value, Inverted.IsChecked is true);
            UpdateGraphPoints();
        }

        private void Inverted_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox check = sender as CheckBox;
            _inputDeviceManager.UpdateAxisTune(ControlInputBinding, CurvatureSlider.Value, check.IsChecked is true);
            UpdateGraphPoints();
        }

        private void ResetCurvatureButton_Click(object sender, RoutedEventArgs e)
        {
            CurvatureSlider.Value = 0;
        }
    }
}
