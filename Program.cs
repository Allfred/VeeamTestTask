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
                bool isCompress = true;

                if (args.Length < 3)
                {
                    throw new Exception("Вы не ввели команду программы,имя исходного и результирующего файла");
                }

                if (args[0] != "compress")
                {
                    isCompress = false;
                    if (args[0] != "decompress")
                    {
                        throw new Exception($"Команда \"{args[0]}\" не существует");
                    }
                }
                
                if (args.Length < 2)
                {
                    throw new Exception("Вы не ввели имя исходного и результирующего файла");
                }

                if (args.Length < 3)
                {
                    throw new Exception("Вы не ввели имя результирующего файла");
                }

                var readingFile = args[1];
                var writingFile = args[2];

                var gzip = new Gzip();
                if (isCompress)
                {
                    gzip.Compress(readingFile, writingFile);
                }
                else
                {
                    gzip.Decompress(readingFile, writingFile);
                }

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