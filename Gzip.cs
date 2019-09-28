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
            _sourceFile = sourceFile; // исходный файл
            _compressedFile = sourceFile + ".gz"; // сжатый файл
            _targetFile = "New" + sourceFile; // восстановленный файл
            _blocks = new Queue<Block>();
            _sizeFreeMemory = 10;
        }

        private int GetSizeFreeMemory()
        {
            ulong freeRam = 0;
            var ramMonitor = //запрос к WMI для получения памяти ПК
                new ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");

            foreach (ManagementObject objram in ramMonitor.Get())
                freeRam = Convert.ToUInt64(objram["FreePhysicalMemory"]); //свободная KB

            freeRam = (ulong) (freeRam * 1024 * 0.45);

            if (freeRam > int.MaxValue) return int.MaxValue;
            return 1024 * 1024;
            return (int) freeRam;
        }

        public void Compress()
        {
            try
            {
                var id = 0;

                //var sizeFile = new FileInfo(_sourceFile).Length;
                //const int mbyte = 1024 * 1024;
                //var countBlock = (int)sizeFile / mbyte;
                //if (countBlock % sizeFile != 0) countBlock++;
                //var threads = new Thread[Environment.ProcessorCount];
                // var  threadR = new Thread(WritingOfBlockDecompress);

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


                        //хуйня полная надо улучшить
                        if (_sizeFreeMemory < 1)
                        {
                            GC.Collect();
                            _sizeFreeMemory = GetSizeFreeMemory();
                            if (_sizeFreeMemory < 1) NoMemoryReadingWaitHandler.WaitOne();
                        }

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
            catch (Exception e)
            {
                Console.WriteLine($"Error message: {e.Message}");
                
            }
        }

        private void WritingOfBlockCompress()
        {
            using (_compressFs = File.Create(_compressedFile))
            {
                // поток архивации
                using (var compressionStream =
                    new GZipStream(_compressFs, CompressionMode.Compress))
                {
                    while (!_finish)
                    {
                        while (_blocks != null && _blocks.Count > 0)
                            lock (_blocks)
                            {
                                var block = _blocks.Dequeue();
                                compressionStream.Write(block.Bytes, 0, block.Count);
                                Console.WriteLine($"Block id:{block.Id} compressed");
                            }

                        
                    }
                }
            }

            EndReadingWaitHandler.Set();
        }

        private void WritingOfBlockDecompress()
        {
            // поток для записи восстановленного файла
            using (var targetStream = File.Create(_targetFile))
            {
                // поток разархивации

                while (!_finish)
                {
                    while (_blocks != null && _blocks.Count > 0)
                        lock (_blocks)
                        {
                            var block = _blocks.Dequeue();
                            targetStream.Write(block.Bytes, 0, block.Count);
                            Console.WriteLine($"Block id:{block.Id} decompressed");
                        }
                }
            }

            EndReadingWaitHandler.Set();
        }

        public void Decompress1()
        {
            _finish = false;
            // поток для чтения из сжатого файла
            try
            {
                using (_compressFs = new FileStream(_compressedFile, FileMode.OpenOrCreate, FileAccess.Read))
                {
                    using (var deCompressionStream = new GZipStream(_compressFs, CompressionMode.Decompress))
                    {
                        var id = 0;
                        var threadW = new Thread(WritingOfBlockDecompress);
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


                            //хуйня полная надо улучшить
                            if (_sizeFreeMemory < 1)
                            {
                                GC.Collect();
                                _sizeFreeMemory = GetSizeFreeMemory();
                                if (_sizeFreeMemory < 1) NoMemoryReadingWaitHandler.WaitOne();
                            }

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
            catch (Exception e)
            {
                Console.WriteLine($"Error message: {e.Message}");
               
            }
        }

        public void Decompress()
        {
            // поток для чтения из сжатого файла
            using (_compressFs = new FileStream(_compressedFile, FileMode.OpenOrCreate, FileAccess.Read))
            {
                // поток для записи восстановленного файла
                using (var targetStream = File.Create(_targetFile))
                {
                    // поток разархивации
                    using (var decompressionStream = new GZipStream(_compressFs, CompressionMode.Decompress))
                    {
                        decompressionStream.CopyTo(targetStream);
                        Console.WriteLine("Восстановлен файл: {0}", _targetFile);
                    }
                }
            }
        }
    }
}