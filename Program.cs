using System;

namespace VeeamTask
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                var path = "File.djvu";
                var gzip = new Gzip(path);
                // создание сжатого файла
                gzip.Compress();
                // чтение из сжатого файла
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