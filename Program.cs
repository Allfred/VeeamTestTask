using System;
using VeeamTask.Model;

namespace VeeamTask
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                //var path = "File.djvu";
                var path1 = @"C:\Users\Antibakter\Documents\Тест\File.pdf";
                var gzip = new Gzip(path1);
                
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