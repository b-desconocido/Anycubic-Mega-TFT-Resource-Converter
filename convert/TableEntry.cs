using System.IO;

namespace convert
{
    class TableEntry
    {
        public ushort Id { get; private set; }
        public ushort Width { get; private set; }
        public ushort Height { get; private set; }
        public uint Offset { get; private set; }
        public const int Size = 16, UsedSize = 2 + 2 + 2 + 3;
        public static TableEntry[] LoadFromFile(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                var result = new TableEntry[fs.Length / Size];
                for (int i = 0; i < result.Length; i++)
                {
                    fs.Position = i * Size;
                    var entry = new TableEntry();
                    entry.Id = br.ReadUInt16();
                    entry.Width = br.ReadUInt16();
                    entry.Height = br.ReadUInt16();
                    entry.Offset = br.ReadUInt32();
                    result[i] = entry;
                }
                return result;
            }
        }

        public override string ToString()
        {
            return $"[{Id}] {Width}x{Height} at {Offset}";
        }

        private TableEntry() { }
        public TableEntry(ushort identifier, ushort width, ushort height, uint offset)
        {
            Id = identifier;
            Width = width;
            Height = height;
            Offset = offset;
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Id);       // 2
            bw.Write(Width);    // 2
            bw.Write(Height);   // 2
            bw.Write(Offset);   // 4
            // pad with dummy zeroes to make it 16 bytes long
            bw.Write(0);        // 4
            bw.Write((short)0); // 2
        }
    }
}
