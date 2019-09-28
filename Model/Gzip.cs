using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Threading;

namespace VeeamTask.Model
{
    public class Gzip
    {
        private static readonly AutoResetEvent EndReadingWaitHandler = new AutoResetEvent(false);
        private static readonly AutoResetEvent NoMemoryReadingWaitHandler = new AutoResetEvent(false);
        private static readonly ManagementObjectSearcher RamMonitor = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
        private readonly Queue<Block> _blocks;
        private readonly string _compressedFile;
        private readonly string _sourceFile;
        private readonly string _targetFile;
        private bool _finish;
        public Gzip(string sourceFile)
        {
            _sourceFile = sourceFile; // source file
            _compressedFile = sourceFile + ".gz"; // compressed file

            _targetFile = sourceFile+ "New"; // decompressed file
            _blocks = new Queue<Block>();
        }
        /// <summary>
        /// Get 45% of free RAM  or if free RAM <1 bytes then exception
        /// </summary>
        /// <returns>45% of free RAM</returns>
        private int GetSizeFreeMemory()
        {
            var freeRam = GetFreeRam45();
            //freeRam = 0;

            //TODO:хуйня полная надо улучшить
            if (freeRam < 1)
            {
                GC.Collect();

                freeRam = GetFreeRam45();

                if (freeRam < 1)
                {
                    throw new Exception("Не хватает оперативной памяти для работы ");
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
        private static int GetFreeRam45()
        {
            ulong freeRam = 0;
            foreach (var objRam in RamMonitor.Get())
            {
                freeRam = Convert.ToUInt64(objRam["FreePhysicalMemory"]); //свободная RAM KB
            }
            freeRam = (ulong) (freeRam * 1024 * 0.45); //переводим в байты и берем 45% свободной оперативной памяти

            if (freeRam > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int) freeRam;
        }
        /// <summary>
        /// Function of compressing
        /// </summary>
        public void Compress()
        {
            var id = 0;
            using (var fs = new FileStream(_sourceFile, FileMode.Open))
            {
                int sizeFreeMemory = GetSizeFreeMemory();
                var bytes = new byte[sizeFreeMemory];
                var threadW = new Thread(WritingOfBlockCompress);
                var countReadingByte = fs.Read(bytes, 0, sizeFreeMemory);

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


                    bytes = new byte[sizeFreeMemory];
                    countReadingByte = fs.Read(bytes, 0, sizeFreeMemory);

                }

                _finish = true;
                EndReadingWaitHandler.WaitOne();
                threadW.Abort();
            }
#if DEBUG
            Console.WriteLine("Сompressed successfully");
#endif
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
                                    compressionStream.Write(block.Bytes, 0, block.Count);
#if DEBUG
                                    Console.WriteLine($"Block id:{block.Id} compressed {block.Count / 1024} KB");
#endif
                            }
                        }
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
            using (var targetStream = File.Create(_targetFile))
            {
                while (!_finish)
                {
                    lock (_blocks)
                    {
                        while (_blocks != null && _blocks.Count > 0)
                        {
                            var block = _blocks.Dequeue();
                            targetStream.Write(block.Bytes, 0, block.Count);
#if DEBUG
                            Console.WriteLine($"Block id:{block.Id} decompressed {block.Count / 1024} KB");
#endif
                        }

                    }
                }
            }

            EndReadingWaitHandler.Set();
        }
        /// <summary>
        /// Function of Decompressing 
        /// </summary>
        public void Decompress()
        {
            _finish = false;
            using (var compressFs = new FileStream(_compressedFile, FileMode.OpenOrCreate, FileAccess.Read))
            {
                using (var deCompressionStream = new GZipStream(compressFs, CompressionMode.Decompress))
                {
                    var id = 0;
                    var threadW = new Thread(WritingOfDecompressedBlock);
                    var sizeFreeMemory = GetSizeFreeMemory();
                    var bytes = new byte[sizeFreeMemory];
                    var countReadingByte = deCompressionStream.Read(bytes, 0, sizeFreeMemory);
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
                        bytes = new byte[sizeFreeMemory];
                        countReadingByte = deCompressionStream.Read(bytes, 0, sizeFreeMemory);

                    }

                    _finish = true;
                    EndReadingWaitHandler.WaitOne();
                    threadW.Abort();
                    deCompressionStream.Close();
                }

                compressFs.Close();
            }
#if DEBUG
            Console.WriteLine("Decompressed successfully");
#endif
        }
    }
}