using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeeamTask
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string path = "File.djvu";
                var gzip = new Gzip(path);
                // создание сжатого файла
                gzip.Compress();
                // чтение из сжатого файла
                gzip.Decompress();

                Console.WriteLine("0");
                Console.ReadKey();
            }
            catch (Exception)
            {
                Console.WriteLine("1");
            }
        }
    }
}
