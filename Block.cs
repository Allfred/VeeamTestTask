namespace VeeamTask
{
    public class Block
    {
        public Block(int id, byte[] bytes, int count)
        {
            Id = id;
            Bytes = bytes;
            Count = count;
        }

        public int Id { get; }
        public byte[] Bytes { get; set; }
        public int Count { get; }
    }
}