using System;

namespace NGuava
{
    /// <summary>
    ///     A class for checkings of preconditions.
    /// </summary>
    public sealed class Preconditions
    {
        private Preconditions()
        {
        }

        public static void CheckNotNull(object reference, object errorMessage)
        {
            if (reference == null)
                throw new NullReferenceException(errorMessage.ToString());
            //change to "null" if refernce of message is null.
        }

        public static void CheckNotNullArgument(object reference, object errorMessage)
        {
            if (reference == null)
                throw new ArgumentNullException(errorMessage.ToString());
        }

        public static void CheckArgument(bool expression, object errorMessage)
        {
            if (!expression)
                throw new ArgumentException(errorMessage.ToString());
        }
    }
}