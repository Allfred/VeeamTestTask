using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Threading;

namespace VeeamTask
{
    public class Gzip
    {
        private static readonly AutoResetEvent EndReadingWaitHandler = new AutoResetEvent(false);
        private static readonly AutoResetEvent NoMemoryReadingWaitHandler = new AutoResetEvent(false);
        private readonly Queue<Block> _blocks;
        private readonly string _compressedFile;
        private readonly string _sourceFile;
        private readonly string _targetFile;
        private FileStream _compressFs;
        private bool _finish;
        private int _sizeFreeMemory;

        public Gzip(string sourceFile)
        {
            _sourceFile = sourceFile; // source file
            _compressedFile = sourceFile + ".gz"; // compressed file
            _targetFile = "New" + sourceFile; // decompressed file
            _blocks = new Queue<Block>();
        }
        /// <summary>
        /// Get 45% of free RAM  or if free RAM < 1 byte then exception
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

                //freeRam = 0;

                if (freeRam < 1)
                    throw new Exception("Не хватает оперативной памяти для работы ");
                //NoMemoryReadingWaitHandler.WaitOne();
            }

            //return 1024 * 1024; for testing 1Mb block
            return freeRam;
        }
        /// <summary>
        ///  Get 45% of free RAM
        /// </summary>
        /// <returns>45% of free RAM</returns>
        private static int GetFreeRam45()
        {
            ulong freeRam = 0;
            var ramMonitor = //запрос к WMI для получения памяти ПК
                new ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");

            foreach (ManagementObject objram in ramMonitor.Get())
                freeRam = Convert.ToUInt64(objram["FreePhysicalMemory"]); //свободная KB

            freeRam = (ulong) (freeRam * 1024 * 0.45); //переводим в байты и берем 45% свободной оперативной памяти

            if (freeRam > int.MaxValue) return int.MaxValue;

            return (int) freeRam;
        }
        /// <summary>
        /// Function of compressing
        /// </summary>
        public void Compress()
        {
            var id = 0;

            //var sizeFile = new FileInfo(_sourceFile).Length;
            //const int mbyte = 1024 * 1024;
            //var countBlock = (int)sizeFile / mbyte;
            //if (countBlock % sizeFile != 0) countBlock++;
            //var threads = new Thread[Environment.ProcessorCount];
            // var  threadR = new Thread(WritingOfDecompressedBlock);

            using (var fs = new FileStream(_sourceFile, FileMode.Open))
            {
                _sizeFreeMemory = GetSizeFreeMemory();
                var bytes = new byte[_sizeFreeMemory];
                var threadW = new Thread(WritingOfBlockCompress);
                var countReadingByte = fs.Read(bytes, 0, _sizeFreeMemory);
                while (countReadingByte > 0)
                {
                    lock (_blocks)
                    {
                        _blocks.Enqueue(new Block(id++, bytes, countReadingByte));
                    }

                    if (threadW.IsAlive == false) threadW.Start();
                    _sizeFreeMemory = GetSizeFreeMemory();


                    bytes = new byte[_sizeFreeMemory];
                    countReadingByte = fs.Read(bytes, 0, _sizeFreeMemory);
                }

                _finish = true;
                EndReadingWaitHandler.WaitOne();
                threadW.Abort();
                fs.Close();
            }

            Console.WriteLine("Сompressed successfully");
        }
        /// <summary>
        /// Function for compressing of blocks and writing their in file
        /// </summary>
        private void WritingOfBlockCompress()
        {
            using (_compressFs = File.Create(_compressedFile))
            {
                using (var compressionStream =
                    new GZipStream(_compressFs, CompressionMode.Compress))
                {
                    while (!_finish)
                    while (_blocks != null && _blocks.Count > 0)
                        lock (_blocks)
                        {
                            var block = _blocks.Dequeue();
                            compressionStream.Write(block.Bytes, 0, block.Count);
                            Console.WriteLine($"Block id:{block.Id} compressed");
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
                while (_blocks != null && _blocks.Count > 0)
                    lock (_blocks)
                    {
                        var block = _blocks.Dequeue();
                        targetStream.Write(block.Bytes, 0, block.Count);
                        Console.WriteLine($"Block id:{block.Id} decompressed");
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
            using (_compressFs = new FileStream(_compressedFile, FileMode.OpenOrCreate, FileAccess.Read))
            {
                using (var deCompressionStream = new GZipStream(_compressFs, CompressionMode.Decompress))
                {
                    var id = 0;
                    var threadW = new Thread(WritingOfDecompressedBlock);
                    _sizeFreeMemory = GetSizeFreeMemory();
                    var bytes = new byte[_sizeFreeMemory];

                    var countReadingByte = deCompressionStream.Read(bytes, 0, _sizeFreeMemory);
                    while (countReadingByte > 0)
                    {
                        lock (_blocks)
                        {
                            _blocks.Enqueue(new Block(id++, bytes, countReadingByte));
                        }

                        if (threadW.IsAlive == false) threadW.Start();
                        _sizeFreeMemory = GetSizeFreeMemory();
                        bytes = new byte[_sizeFreeMemory];
                        countReadingByte = deCompressionStream.Read(bytes, 0, _sizeFreeMemory);
                    }

                    _finish = true;
                    EndReadingWaitHandler.WaitOne();
                    threadW.Abort();
                    deCompressionStream.Close();
                }

                _compressFs.Close();
            }

            Console.WriteLine("Decompressed successfully");
        }
    }
}