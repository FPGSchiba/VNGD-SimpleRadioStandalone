using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class ConversionHelpers
    {
        public static short[] ByteArrayToShortArray(byte[] data)
        {
            var shortArry = new short[data.Length/sizeof(short)];
            Buffer.BlockCopy(data, 0, shortArry, 0, data.Length);
            return shortArry;
        }

        public static byte[] ShortArrayToByteArray(short[] shortArray)
        {
            var byteArray = new byte[shortArray.Length*sizeof(short)];
            Buffer.BlockCopy(shortArray, 0, byteArray, 0, byteArray.Length);
            return byteArray;
        }
    }
}