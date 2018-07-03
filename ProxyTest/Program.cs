using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;


namespace ProxyTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello world. Proxy method test o/");
            Console.WriteLine(Add(1336, 1) + Sub(123456, 1234) + cout("all"));

            Console.ReadKey();
        }

        static int Add(int a, int b)
        {
            return a + b;
        }

        static int Sub(int a, int b)
        {
            return a + b;
        }

        static string cout(string str)
        {
            return "world" + str + "hello"; 
        }
    }
}
