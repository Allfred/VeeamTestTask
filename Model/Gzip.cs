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
        private Thread _threadWDec;
        private  readonly Queue<Block> _blocks;
        private  string _sourceFile;
        private  string _compressedFile;
        private  string _deCompressedFile;
        private bool _finish;
        
        /// <summary>
        /// Constructor
        /// </summary>
        public Gzip()
        {
            _sourceFile = "";
            _compressedFile = "";
            _deCompressedFile = "";
            _blocks = new Queue<Block>();
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
           // return 1024 * 1024*1; //for testing 1Mb block
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
        /// <param name="sourceFile">source file</param>
        /// <param name="compressedFile">compressed file </param>
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
        /// <summary>
        /// Reading from stream to bytes array of datas
        /// </summary>
        /// <param name="bytes">bytes array for reading</param>
        /// <param name="fs">stream</param>
        /// <returns> count read bytes </returns>
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
                    while (!_finish || _blocks.Count > 0)
                    {
                        lock (_blocks)
                        {
                            
                            var block = _blocks.Dequeue();
                            compressionStream.Write( BitConverter.GetBytes(block.Id), 0, 4);
                            compressionStream.Write( BitConverter.GetBytes(block.Count), 0, 8);

                            for (int i = 0; i < block.Bytes.CountOfArray; i++)
                            {
                                        compressionStream.Write(block.Bytes[i], 0, block.Bytes.CountOfByte[i]);
                            }
                                    
#if DEBUG
                                    Console.WriteLine($"Block id:{block.Id} compressed {block.Count / 1024} KB");
#endif
                            
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
                while ( !_finish || _blocks.Count > 0)
                {
                    lock (_blocks)
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

                    CleanMemoryWaitHandler.Set();
                }
            }

            EndReadingWaitHandler.Set();
        }
        /// <summary>
        /// Function of Decompressing 
        /// </summary>
        /// <param name="compressedFile">compressed file</param>
        /// <param name="deCompressedFile">decompressed file</param>
        public void Decompress(string compressedFile, string deCompressedFile)
        {
            FileIsExist(compressedFile);
            _compressedFile = compressedFile;
            _deCompressedFile = deCompressedFile;
            using (var compressFs = new FileStream(_compressedFile, FileMode.OpenOrCreate, FileAccess.Read))
            {
                using (var deCompressionStream = new GZipStream(compressFs, CompressionMode.Decompress))
                {
                    _finish = false;
                    _threadWDec = new Thread(WritingOfDecompressedBlock);
                    var (id, blockSize) = GetIdAndSizeOfBlock(deCompressionStream);

                    while (blockSize > 0)
                    {
                        var sizeFreeMemory = GetSizeFreeMemory();
                        if (blockSize > sizeFreeMemory)
                        {
                            DecompressFileInBlocksOnMaxRamMemory(id, blockSize, sizeFreeMemory, deCompressionStream);
                        }
                        else
                        {
                            var bytes=new LargeByteArray(blockSize);
                            ReadingDecompressedStreamInByte(bytes, deCompressionStream);
                            lock (_blocks)
                            {
                                _blocks.Enqueue(new Block(id, bytes, blockSize));
                            }

#if DEBUG
                            Console.WriteLine($"Read the block id:{id} size:{blockSize / 1024} KB");
#endif

                            if (_threadWDec.IsAlive == false) _threadWDec.Start();
                        }

                        (id, blockSize) = GetIdAndSizeOfBlock(deCompressionStream);
                       
                    }

                    _finish = true;
                    EndReadingWaitHandler.WaitOne();
                    _threadWDec.Abort();
                }
            }
#if DEBUG
            Console.WriteLine("Decompressed successfully");
#endif
        }
        /// <summary>
        /// Get id and size of the block from the stream
        /// </summary>
        /// <param name="deCompressionStream"></param>
        /// <returns>(id, block size)</returns>
        private static (int id, long blockSize) GetIdAndSizeOfBlock(GZipStream deCompressionStream)
        {
           
            byte[] byteId = new byte[4];
            deCompressionStream.Read(byteId, 0, byteId.Length);
            var id = BitConverter.ToInt32(byteId, 0);

            byte[] byteCountReading = new byte[8];
            deCompressionStream.Read(byteCountReading, 0, byteCountReading.Length);
            var countOfBytesInBlock = BitConverter.ToInt32(byteCountReading, 0);
            return (id, countOfBytesInBlock);
        }
        /// <summary>
        /// Decopmpress of the block if PC has RAM less than block size
        /// </summary>
        /// <param name="id">block id</param>
        /// <param name="blockSize"> block size in bytes</param>
        /// <param name="sizeFreeMemory"> size of free RAM</param>
        /// <param name="deCompressionStream">stream</param>
        private  void DecompressFileInBlocksOnMaxRamMemory(int id,long blockSize, long sizeFreeMemory, GZipStream deCompressionStream)
        {
            while (blockSize > 0)
            {
                if (blockSize > sizeFreeMemory)
                {
                    blockSize = blockSize - sizeFreeMemory;
                }
                else
                {
                    sizeFreeMemory = blockSize;
                    blockSize = 0;
                }

                var bytes = new LargeByteArray(sizeFreeMemory);
                ReadingDecompressedStreamInByte(bytes, deCompressionStream);
#if DEBUG
                Console.WriteLine($"Read the block id:{id} size:{sizeFreeMemory / 1024} KB");
#endif

                lock (_blocks)
                {
                    _blocks.Enqueue(new Block(id, bytes, sizeFreeMemory));
                }

                if (_threadWDec.IsAlive == false) _threadWDec.Start();

                sizeFreeMemory = GetSizeFreeMemory();
            }
        }
        /// <summary>
        /// Reading from stream to bytes array of datas
        /// </summary>
        /// <param name="bytes">big bytes array</param>
        /// <param name="deCompressionStream">stream</param>
        private void ReadingDecompressedStreamInByte(LargeByteArray bytes,GZipStream deCompressionStream)
        {
         
            for (int i = 0; i < bytes.CountOfArray; i++)
            {
                int count= deCompressionStream.Read(bytes[i], 0, bytes[i].Length);
                bytes.CountOfByte[i] = count;
               
            }
        }
    }
}