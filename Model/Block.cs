namespace GZipTest.Model
{
    public class Block
    {
        public Block(int id, LargeByteArray bytes, long count)
        {
            Id = id;
            Bytes = bytes;
            Count = count;
        }

        public int Id { get; }
        public LargeByteArray Bytes { get; set; }
        public long Count { get; }
    }
}