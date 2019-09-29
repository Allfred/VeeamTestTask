using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Threading;

namespace GZipTest.Model
{
    public class Gzip
    {
        private static readonly AutoResetEvent EndReadingWaitHandler = new AutoResetEvent(false);
        private static readonly AutoResetEvent CleanMemoryWaitHandler = new AutoResetEvent(false);
        private static readonly ManagementObjectSearcher RamMonitor = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
        private  readonly Queue<Block> _blocks;
        private  string _sourceFile;
        private  string _compressedFile;
        private  string _deCompressedFile;
        private bool _finish;
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="sourceFile">name of source file</param>
        /// <param name="deCompressedFile">name of deCompressed file</param>
        public Gzip()
        {
            _sourceFile = "";
            _compressedFile = "";
            _deCompressedFile = "";
            _blocks = new Queue<Block>();

            //var info = FileIsExist(sourceFile);
            //_sourceFile = info.FullName; 
            //_compressedFile = _compressedFile = $"{_sourceFile}.gz";
            //_deCompressedFile = Path.Combine(info.DirectoryName, deCompressedFile + info.Extension);

        }
        /// <summary>
        /// if the file is not exist return exception
        /// </summary>
        /// <param name="sourceFile">name of file</param>
        /// <returns>File info of the  file</returns>
        private static void FileIsExist(string sourceFile)
        {
            FileInfo info = new FileInfo(sourceFile);
            
            if (!info.Exists)
            {
                throw new Exception($"Файл \"{info.FullName}\" не существует");
            }
        }
        /// <summary>
        /// Get 45% of free RAM  or if free RAM < 1 bytes then exception
        /// </summary>
        /// <returns>45% of free RAM</returns>
        private long GetSizeFreeMemory()
        {
            var freeRam = GetFreeRam45();

            if (freeRam < 1)
            {
                GC.Collect();
                freeRam = GetFreeRam45();
                if (freeRam < 1)
                {
                    CleanMemoryWaitHandler.WaitOne();

                    GC.Collect();
                    freeRam = GetFreeRam45();

                    if (freeRam < 1)
                    {
                        throw new Exception("Не хватает оперативной памяти для работы приложения ");
                    }
                }

            }
#if DEBUG
            //return 1024 * 1024*100; //for testing 100Mb block
#endif
            return freeRam;
        }
        /// <summary>
        ///  Get 45% of free RAM
        /// </summary>
        /// <returns>45% of free RAM</returns>
        private static long GetFreeRam45()
        {
            ulong freeRam = 0;
            foreach (var objRam in RamMonitor.Get())
            {
                freeRam = Convert.ToUInt64(objRam["FreePhysicalMemory"]); //свободная RAM KB
            }
            freeRam = (ulong) (freeRam * 1024 * 0.45); //переводим в байты и берем 45% свободной оперативной памяти

            if (freeRam > Int64.MaxValue)
            {
                return Int64.MaxValue;
            }

            return (long) freeRam;
        }
        /// <summary>
        /// Function of compressing
        /// </summary>
        public void Compress(string sourceFile, string compressedFile)
        {
            FileIsExist(sourceFile);
            _sourceFile = sourceFile;
            _compressedFile = $"{compressedFile}.gz";

            using (var fs = new FileStream(_sourceFile, FileMode.Open))
            {
                var id = 0;
                _finish = false;
                var threadW = new Thread(WritingOfBlockCompress);
                long sizeFreeMemory = GetSizeFreeMemory();
                
                var bytes=new LargeByteArray(sizeFreeMemory);
                long countReadingByte = ReadingStreamInByte(bytes, fs);

                while (countReadingByte > 0)
                {
#if DEBUG
                    Console.WriteLine($"Read the block: id:{id} size:{countReadingByte / 1024} KB");
#endif
                    lock (_blocks)
                    {
                        _blocks.Enqueue(new Block(id++, bytes, countReadingByte));
                    }

                    if (threadW.IsAlive == false) threadW.Start();
                    

                    sizeFreeMemory = GetSizeFreeMemory();
                    bytes = new LargeByteArray(sizeFreeMemory);
                    countReadingByte = ReadingStreamInByte(bytes, fs);

                }

                _finish = true;
                EndReadingWaitHandler.WaitOne();
                threadW.Abort();
            }
#if DEBUG
            Console.WriteLine("Сompressed successfully");
#endif
        }

        private static long ReadingStreamInByte(LargeByteArray bytes, FileStream fs)
        {
            long countReadingByte = 0;

            for (int i = 0; i < bytes.CountOfArray; ++i)
            {
                int count= fs.Read(bytes[i], 0, bytes[i].Length);
                bytes.CountOfByte[i] = count;
                countReadingByte += count;
            }

            return countReadingByte;
        }

        /// <summary>
        /// Function for compressing of blocks and writing their in file
        /// </summary>
        private void WritingOfBlockCompress()
        {
            using (var compressFs = File.Create(_compressedFile))
            {
                using (var compressionStream =
                    new GZipStream(compressFs, CompressionMode.Compress))
                {
                    while (!_finish)
                    {
                        lock (_blocks)
                        {
                            while (_blocks != null && _blocks.Count > 0)
                            {
                                    var block = _blocks.Dequeue();

                                    for (int i = 0; i < block.Bytes.CountOfArray; i++)
                                    {
                                        compressionStream.Write(block.Bytes[i], 0, block.Bytes.CountOfByte[i]);
                                    }
                                    
#if DEBUG
                                    Console.WriteLine($"Block id:{block.Id} compressed {block.Count / 1024} KB");
#endif
                            }
                        }
                        CleanMemoryWaitHandler.Set();
                    }
                }
            }

            EndReadingWaitHandler.Set();
        }
        /// <summary>
        /// Function for writing the decompressed bloks in file
        /// </summary>
        private void WritingOfDecompressedBlock()
        {
            using (var newSourceStream = File.Create(_deCompressedFile))
            {
                while (!_finish)
                {
                    lock (_blocks)
                    {
                        while (_blocks != null && _blocks.Count > 0)
                        {
                            var block = _blocks.Dequeue();

                            for (int i = 0; i < block.Bytes.CountOfArray; i++)
                            {
                                newSourceStream.Write(block.Bytes[i], 0, block.Bytes.CountOfByte[i]);
                            }
                           
#if DEBUG
                            Console.WriteLine($"Block id:{block.Id} decompressed {block.Count / 1024} KB");
#endif
                        }

                    }

                    CleanMemoryWaitHandler.Set();
                }
            }

            EndReadingWaitHandler.Set();
        }
        /// <summary>
        /// Function of Decompressing 
        /// </summary>
        public void Decompress(string compressedFile, string deCompressedFile)
        {
            FileIsExist(compressedFile);
            _compressedFile = compressedFile;
            _deCompressedFile = deCompressedFile;
            using (var compressFs = new FileStream(_compressedFile, FileMode.OpenOrCreate, FileAccess.Read))
            {
                using (var deCompressionStream = new GZipStream(compressFs, CompressionMode.Decompress))
                {
                    var id = 0;
                    _finish = false;
                    var threadW = new Thread(WritingOfDecompressedBlock);
                    var sizeFreeMemory = GetSizeFreeMemory();
                    var bytes = new LargeByteArray(sizeFreeMemory);
                    long countReadingByte = ReadingDecompressedStreamInByte(bytes, deCompressionStream);

                    while (countReadingByte > 0)
                    {
#if DEBUG
                        Console.WriteLine($"Read the block id:{id} size:{countReadingByte / 1024} KB");
#endif
                        lock (_blocks)
                        {
                            _blocks.Enqueue(new Block(id++, bytes, countReadingByte));
                        }

                        if (threadW.IsAlive == false) threadW.Start();
                        sizeFreeMemory = GetSizeFreeMemory();
                        bytes = new LargeByteArray(sizeFreeMemory);
                        countReadingByte = ReadingDecompressedStreamInByte(bytes, deCompressionStream);

                    }

                    _finish = true;
                    EndReadingWaitHandler.WaitOne();
                    threadW.Abort();
                }
            }
#if DEBUG
            Console.WriteLine("Decompressed successfully");
#endif
        }

        private static long ReadingDecompressedStreamInByte(LargeByteArray bytes,GZipStream deCompressionStream)
        {
            long countReadingByte = 0;
            for (int i = 0; i < bytes.CountOfArray; i++)
            {
                int count= deCompressionStream.Read(bytes[i], 0, bytes[i].Length);
                bytes.CountOfByte[i] = count;
                countReadingByte += count;
            }

            return countReadingByte;
        }
    }
}