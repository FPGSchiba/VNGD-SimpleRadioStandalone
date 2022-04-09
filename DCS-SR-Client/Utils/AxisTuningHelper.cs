using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Utils
{
    public static class AxisTuningHelper
    {
        public static double GetCurvaturePointValue(double normalisedX, double curvature, bool invert)
        {
            // Formula to provide a sigmoid curve with an inflection point at 0.5 for normalised values of X
            // and where 0 < curvature < 1
            double numerator = (curvature - 1) * (2 * normalisedX - 1);
            numerator = invert ? -numerator : numerator;
            double denominator = 2 * (4 * curvature * (Math.Abs(normalisedX - 0.5)) - curvature - 1);
            double pointValue = (numerator / denominator) + 0.5;
            return pointValue;
        }
    }
}
