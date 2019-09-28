using System;
using GZipTest.Model;

namespace GZipTest
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                var source = args[0];
                var deCompressed = args[1];
                var gzip = new Gzip(source,deCompressed);

                // var gzip = new Gzip("C\\File", "NewFile");
                gzip.Compress();
                gzip.Decompress();

                Console.WriteLine("0");
                Console.ReadKey();
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