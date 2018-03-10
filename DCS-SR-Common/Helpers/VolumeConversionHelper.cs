using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers
{
    public static class VolumeConversionHelper
    {
        public static double ConvertLinearToDB(float linear)
        {
            if (linear == 0)
            {
                //basically nothing but not 0 or we'll get -nan
                linear = 0.00000001f;
            }

            return ((Math.Log(Math.Abs(linear) / 2.302585092994046)) * 20f);
        }

        public static string ConvertLinearDiffToDB(float delta)
        {
           
            float diff = (float)VolumeConversionHelper.ConvertLinearToDB((float)(delta)) - (float)VolumeConversionHelper.ConvertLinearToDB((float)(1.0));

            return Math.Round(diff) + " dB";
            //convert diff into db
        }

        public static float ConvertVolumeSliderToScale(float volume)
        {
            var db = (-30) + (25 - (-30)) * volume;
            return (float) Math.Exp(db / 20 * Math.Log(10));
        }

    }
}
