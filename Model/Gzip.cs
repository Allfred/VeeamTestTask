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
        private readonly string _sourceFile;
        private readonly string _compressedFile;
        private readonly string _deCompressedFile;
        private bool _finish;
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="sourceFile">name of source file</param>
        /// <param name="deCompressedFile">name of deCompressed file</param>
        public Gzip(string sourceFile,string deCompressedFile)
        {
            var info = GetFileInfo(sourceFile);
            _sourceFile = info.FullName; 
            _compressedFile = _compressedFile = $"{_sourceFile}.gz";
            _deCompressedFile = Path.Combine(info.DirectoryName, deCompressedFile + info.Extension);
            _blocks = new Queue<Block>();
        }
        /// <summary>
        /// Get FileInfo of the file or if the file is not exist then exception
        /// </summary>
        /// <param name="sourceFile">name of file</param>
        /// <returns>File info of the  file</returns>
        private static FileInfo GetFileInfo(string sourceFile)
        {
            FileInfo info = new FileInfo(sourceFile);
            
            if (!info.Exists)
            {
                throw new Exception($"File \"{info.FullName}\" is not exist");
            }

            return info;
        }
        /// <summary>
        /// Get 45% of free RAM  or if free RAM < 1 bytes then exception
        /// </summary>
        /// <returns>45% of free RAM</returns>
        private int GetSizeFreeMemory()
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
            using (var fs = new FileStream(_sourceFile, FileMode.Open))
            {
                var id = 0;
                _finish = false;
                var threadW = new Thread(WritingOfBlockCompress);
                int sizeFreeMemory = GetSizeFreeMemory();
                var bytes = new byte[sizeFreeMemory];
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
                            newSourceStream.Write(block.Bytes, 0, block.Count);
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
        public void Decompress()
        {
           
            using (var compressFs = new FileStream(_compressedFile, FileMode.OpenOrCreate, FileAccess.Read))
            {
                using (var deCompressionStream = new GZipStream(compressFs, CompressionMode.Decompress))
                {
                    var id = 0;
                    _finish = false;
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
                }
            }
#if DEBUG
            Console.WriteLine("Decompressed successfully");
#endif
        }
    }
}