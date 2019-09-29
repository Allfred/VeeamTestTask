using System;
using System.Diagnostics;
using System.IO;
using GZipTest.Model;

namespace GZipTest
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    throw new Exception("Вы не ввели имя исходного и результирующего файла");
                }

                if (args.Length < 2)
                {
                    throw new Exception("Вы не ввели имя результирующего файла");
                }

                var source = args[0];
                var deCompressed = args[1];
                var gzip = new Gzip(source,deCompressed);
                gzip.Compress();
                gzip.Decompress();
                
                
            }

            catch (Exception e)
            {
                Console.WriteLine($"Error message: {e.Message}");
                Console.WriteLine("1");
                Console.ReadKey();
            }
        }
    }
}