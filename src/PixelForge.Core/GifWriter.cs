namespace PixelForge.Core;

/// <summary>Looping animated GIF89a with one global palette. Alpha &lt; 128 = transparent.</summary>
public static class GifWriter
{
    public static void Save(string path, IReadOnlyList<uint[]> frames, IReadOnlyList<int> delaysMs, int w, int h)
    {
        using var fs = File.Create(path);
        Write(fs, frames, delaysMs, w, h);
    }

    public static void Write(Stream s, IReadOnlyList<uint[]> frames, IReadOnlyList<int> delaysMs, int w, int h)
    {
        // palette: index 0 = transparent
        var index = new Dictionary<uint, byte>();
        var colors = new List<uint> { 0 };
        var indexed = frames.Select(f => f.Select(p =>
        {
            if (p >> 24 < 128) return (byte)0;
            uint rgb = p & 0xFFFFFF;
            if (!index.TryGetValue(rgb, out var i))
            {
                if (colors.Count == 256)
                    throw new InvalidOperationException("GIF supports at most 255 colors; this animation uses more.");
                index[rgb] = i = (byte)colors.Count;
                colors.Add(rgb);
            }
            return i;
        }).ToArray()).ToList();

        int bits = 1;
        while (1 << bits < colors.Count) bits++;

        var bw = new BinaryWriter(s);
        bw.Write("GIF89a"u8);
        bw.Write((ushort)w); bw.Write((ushort)h);
        bw.Write((byte)(0x80 | 0x70 | (bits - 1))); bw.Write((byte)0); bw.Write((byte)0);
        for (int i = 0; i < 1 << bits; i++)
        {
            uint c = i < colors.Count ? colors[i] : 0;
            bw.Write((byte)(c >> 16)); bw.Write((byte)(c >> 8)); bw.Write((byte)c);
        }
        bw.Write([0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8, 0x03, 0x01, 0x00, 0x00, 0x00]);   // loop forever

        for (int f = 0; f < indexed.Count; f++)
        {
            // graphic control: dispose = restore to background, transparent index 0
            bw.Write([0x21, 0xF9, 0x04, (2 << 2) | 1]);
            bw.Write((ushort)Math.Max(1, (delaysMs[f] + 5) / 10));
            bw.Write([0x00, 0x00]);
            bw.Write((byte)0x2C);
            bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)w); bw.Write((ushort)h);
            bw.Write((byte)0);
            bw.Flush();
            Lzw(s, indexed[f], Math.Max(2, bits));
        }
        s.WriteByte(0x3B);
    }

    static void Lzw(Stream s, byte[] data, int minCode)
    {
        s.WriteByte((byte)minCode);
        int clear = 1 << minCode, end = clear + 1;
        int next = end + 1, size = minCode + 1;
        var dict = new Dictionary<int, int>();
        var bits = new SubBlockBits(s);

        bits.Write(clear, size);
        int prefix = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            int key = prefix << 8 | data[i];
            if (dict.TryGetValue(key, out int code)) { prefix = code; continue; }
            bits.Write(prefix, size);
            if (next < 4096)
            {
                dict[key] = next++;
                if (next > 1 << size && size < 12) size++;
            }
            else
            {
                bits.Write(clear, size);
                dict.Clear();
                next = end + 1;
                size = minCode + 1;
            }
            prefix = data[i];
        }
        bits.Write(prefix, size);
        bits.Write(end, size);
        bits.Flush();
    }

    /// <summary>LSB-first bit packer emitting GIF data sub-blocks (≤255 bytes) and the 0 terminator.</summary>
    sealed class SubBlockBits(Stream s)
    {
        readonly byte[] _block = new byte[255];
        int _len, _acc, _nbits;

        public void Write(int code, int size)
        {
            _acc |= code << _nbits;
            _nbits += size;
            while (_nbits >= 8) { Push((byte)_acc); _acc >>= 8; _nbits -= 8; }
        }

        public void Flush()
        {
            if (_nbits > 0) Push((byte)_acc);
            if (_len > 0) { s.WriteByte((byte)_len); s.Write(_block, 0, _len); }
            s.WriteByte(0);
        }

        void Push(byte b)
        {
            _block[_len++] = b;
            if (_len < 255) return;
            s.WriteByte(255); s.Write(_block); _len = 0;
        }
    }
}
