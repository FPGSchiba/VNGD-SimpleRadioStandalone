using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class ConversionHelpers
    {
        public static short[] ByteArrayToShortArray(byte[] data)
        {
            short[] shortArry = new short[(data.Length / sizeof(short))];
            Buffer.BlockCopy(data, 0, shortArry, 0, data.Length); 
            return shortArry;
        }

        public static byte[] ShortArrayToByteArray(short[] shortArray)
        {
            byte[] byteArray = new byte[shortArray.Length * sizeof(short)];
            Buffer.BlockCopy(shortArray, 0, byteArray, 0, byteArray.Length);
            return byteArray;
        }
    }
}
