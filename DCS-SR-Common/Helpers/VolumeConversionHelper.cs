using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers
{
    public static class VolumeConversionHelper
    {

        public static double ConvertFloatToDB(float linear)
        {
            if (linear == 0)
            {
                //basically nothing but not 0 or we'll get -nan
                linear = 0.0000158f;
            }

            return ((Math.Log(linear) * 20f));
        }


        public static string ConvertLinearDiffToDB(float delta)
        {
            float diff = (float) VolumeConversionHelper.ConvertFloatToDB((float) (delta)) -
                         (float) VolumeConversionHelper.ConvertFloatToDB((float) (1.0));

            return Math.Round(diff) + " dB";
            //convert diff into db
        }

        public static float ConvertVolumeSliderToScale(float volume)
        {
            var db = (-30) + (28 - (-30)) * volume;
            return (float) Math.Exp(db / 20 * Math.Log(10));
        }
    }
}