using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NGuava
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var dic = new ConcurrentDictionary<int, int>();

            Console.ReadKey();
        }

        public static IEnumerable<MethodInfo> GetMarkedMethods(object clazz)
        {
            var typeOfClass = clazz.GetType();
            return typeOfClass.GetMethods().Where(method =>
            {
                var attribute = method.GetCustomAttribute(typeof(Subscribe));
                return attribute == null ? false : true;
            });
        }
    }
}