using Godot;
using System.IO;

namespace DigimonWorldEternity;

/// <summary>
/// Reads a PCM wav from disk. The music lives outside the Godot project folder.
/// </summary>
public static class WavMusic
{
    public static AudioStreamWav? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I')
            return null;

        int channels = 1;
        int rate = 44100;
        int bits = 16;
        byte[]? data = null;
        int cursor = 12;
        while (cursor + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, cursor, 4);
            int size = System.BitConverter.ToInt32(bytes, cursor + 4);
            int body = cursor + 8;
            if (size < 0 || body + size > bytes.Length)
                size = bytes.Length - body;
            if (id == "fmt " && size >= 16)
            {
                channels = System.BitConverter.ToInt16(bytes, body + 2);
                rate = System.BitConverter.ToInt32(bytes, body + 4);
                bits = System.BitConverter.ToInt16(bytes, body + 14);
            }
            else if (id == "data")
            {
                data = new byte[size];
                System.Buffer.BlockCopy(bytes, body, data, 0, size);
            }
            cursor = body + size + (size & 1);
        }
        if (data == null || bits != 16)
            return null;

        int samples = data.Length / (channels * 2);
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate,
            Stereo = channels >= 2,
            Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            LoopEnd = samples,
        };
    }
}
