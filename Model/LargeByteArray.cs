using System;

namespace GZipTest.Model
{
    public class LargeByteArray
    {
        private byte[][] _bytes;
        public int CountOfArray { get; }
        public LargeByteArray(long count)
        {
            CountOfArray = (int)(count / Int32.MaxValue);
            var demOflastArray = (int)(count - CountOfArray * Int32.MaxValue);

            if (demOflastArray > 0)
            {
                CountOfArray++;
            }

            _bytes = new byte[CountOfArray][];

            for (int i = 0; i < CountOfArray - 1; ++i)
            {
                _bytes[i] = new byte[Int32.MaxValue];
            }

            if (demOflastArray > 0)
            {
                _bytes[CountOfArray - 1] = new byte[demOflastArray];
            }
            else
            {
                _bytes[CountOfArray - 1] = new byte[Int32.MaxValue];
            }
        }

        public byte this[long index]
        {
            get
            {
                int firstIndex = (int)(index / Int32.MaxValue);
                int secondIndex = (int) (index - firstIndex * Int32.MaxValue);
                return _bytes[firstIndex][secondIndex];
            }

            set
            {
                int firstIndex = (int)(index / Int32.MaxValue);
                int secondIndex = (int)(index - firstIndex * Int32.MaxValue);
                _bytes[firstIndex][secondIndex]=value;
            }
        }

    }
}
