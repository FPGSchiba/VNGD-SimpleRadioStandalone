using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NLog;
using Xamarin.Forms;
using Xamarin.Forms.Internals;
using IMultiValueConverter = System.Windows.Data.IMultiValueConverter;
using IValueConverter = System.Windows.Data.IValueConverter;
using Point = System.Windows.Point;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.Components
{
    public partial class BalanceControl : UserControl
    {
        private bool _isPressed = false;
        private Canvas _templateCanvas = null;
        private Logger _logger = LogManager.GetCurrentClassLogger();
        
        // The value of the control.
        public double Radius { get; set; }
        
        public BalanceControl()
        {
            DataContext = this;
            
            InitializeComponent();
        }

        

        private void Ellipse_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //Enable moving mouse to change the value.
            _isPressed = true;
        }

        private void Ellipse_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            //Disable moving mouse to change the value.
            _isPressed = false;
        }

        private void Ellipse_MouseMove(object sender, MouseEventArgs e)
        {
            //Find the parent canvas.
            if (_templateCanvas == null)
            {
                _templateCanvas = MyHelper.FindParent<Canvas>(e.Source as Ellipse);
                if (_templateCanvas == null)
                {
                    _logger.Debug("Not finding the parent canvas.");
                    return;
                }
            }
            //Canculate the current rotation angle and set the value.
            
            Point newPos = e.GetPosition(_templateCanvas);
            double angle = GetAngleR(newPos);
            
            if (_isPressed)
            {
                knob.Value = (knob.Maximum - knob.Minimum) * angle / (2 * Math.PI);
            }
        }
        
        public double GetAngleR(Point pos)
        {
            //Calculate out the distance(r) between the center and the position
            Point center = new Point(Radius, Radius);
            double xDiff = center.X - pos.X;
            double yDiff = center.Y - pos.Y;
            double r = Math.Sqrt(xDiff * xDiff + yDiff * yDiff);

            if (_isPressed && pos.Y - 5 > Radius)
            {
                _isPressed = false; 
            }
            if (pos.Y < Radius && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                _isPressed = true;
            }
            
            //Calculate the angle
            double angle = Math.Acos((pos.Y > Radius ? center.Y - Radius : center.Y - pos.Y) / r);
            _logger.Trace("r:{0}, x:{1} y:{2},angle:{3}.", r, pos.X, pos.Y, angle);
            if (pos.X < Radius)
                angle = 2 * Math.PI - angle;
            if (Double.IsNaN(angle))
                return 0.0;
            else
                return angle;
        }

        private void Ellipse_OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if(!_isPressed)
                // TODO: Change this to look at the current angle and adjust the value based on that
                knob.Value += e.Delta / 120;
        }
    }

    //The converter used to convert the value to the rotation angle.
    public class ValueAngleConverter : IMultiValueConverter
    {
        #region IMultiValueConverter Members

        public object Convert(object[] values, Type targetType, object parameter, 
                      System.Globalization.CultureInfo culture)
        {
            double value = (double)values[0];
            double minimum = (double)values[1];
            double maximum = (double)values[2];

            return MyHelper.GetAngle(value, maximum, minimum);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, 
              System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    //Convert the value to text.
    public class ValueTextConverter : IValueConverter
    {

        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, 
                  System.Globalization.CultureInfo culture)
        {
            double v = (double)value;
            return String.Format("{0:F2}", v);
        }

        public object ConvertBack(object value, Type targetType, object parameter, 
            System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    public class SizeConverter : IValueConverter
    {
        public object Convert(object value, Type  targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                return (double)value * 2;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                return (double)value / 2;
            }
            return null;
        }
    }

    public class KnobConverter : IValueConverter
    {
        public object Convert(object value, Type  targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                return MyHelper.GetKnobSizeFromRadius((double)value);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
    
    public class OffsetConverter : IValueConverter
    {
        public object Convert(object value, Type  targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                var knobSize = MyHelper.GetKnobSizeFromRadius((double)value);
                return MyHelper.GetKnobOffset((double)value, knobSize);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
    public static class MyHelper
    {
        //Get the parent of an item.
        public static T FindParent<T>(FrameworkElement current)
          where T : FrameworkElement
        {
            do
            {
                current = VisualTreeHelper.GetParent(current) as FrameworkElement;
                if (current is T)
                {
                    return (T)current;
                }
            }
            while (current != null);
            return null;
        }

        //Get the rotation angle from the value
        public static double GetAngle(double value, double maximum, double minimum)
        {
            // TODO: Change this method to clamp between 90 and 270 so we stay in the top half of the circle
            double current = (value / (maximum - minimum)) * 360;
            if (current == 360)
                current = 359.999;

            return current;
        }

        public static double GetKnobSizeFromRadius(double radius)
        {
            return radius / 4;
        }
        
        public static double GetKnobOffset(double radius, double knobSize)
        {
            return radius - knobSize / 2;
        }
    }
}

