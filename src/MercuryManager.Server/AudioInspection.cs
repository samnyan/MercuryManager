using System.Buffers.Binary;
using CUE4Parse.UE4.Criware.Decoders.HCA;
using NVorbis;
using VGAudio.Containers.Wave;
namespace MercuryManager.Server;
public sealed record AudioFileInfo(string Format,int Channels,int SampleRate,long Samples,bool LoopEnabled,long LoopStart,long LoopEnd,bool Encrypted=false){public object? Details{get;init;}};
public static class AudioInspection
{
    public static AudioFileInfo Read(byte[] bytes)
    {
        if(bytes.Length>=12&&bytes.AsSpan(0,4).SequenceEqual("RIFF"u8))
        {
            var format=new WaveReader().ReadFormat(HcaEncoding.NormalizeWav(bytes));long start=0,end=format.SampleCount;bool loop=false;
            for(int p=12;p+8<=bytes.Length;){int n=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+4)));if(n<0||p+8L+n>bytes.Length)throw new InvalidDataException("Invalid WAV chunk.");
                if(bytes.AsSpan(p,4).SequenceEqual("smpl"u8)&&n>=60&&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+36))>0)
                {
                    if(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+48))!=0)throw new InvalidDataException("Only forward WAV loops are supported.");
                    start=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+52));end=(long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+56))+1;loop=true;
                }p=checked(p+8+n+(n&1));
            }
            if(loop&&(start<0||end<=start||end>format.SampleCount))throw new InvalidDataException("WAV loop outside audio.");
            return new("wav",format.ChannelCount,format.SampleRate,format.SampleCount,loop,start,end);
        }
        if(bytes.Length>=4&&bytes.AsSpan(0,4).SequenceEqual("OggS"u8))
        {
            return ReadOgg(bytes);
        }
        using var stream=new MemoryStream(bytes);var h=new HcaDecoder(stream,0,0).HcaInfo;
        return new("hca",h.ChannelCount,h.SamplingRate,h.SampleCount,h.LoopEnabled,h.LoopEnabled?h.LoopStartSample:0,h.LoopEnabled?h.LoopEndSample:h.SampleCount,h.EncryptionEnabled);
    }

    public static AudioFileInfo ReadOgg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new VorbisReader(stream, false);
        int channels = reader.Channels;
        int sampleRate = reader.SampleRate;
        long totalSamples = reader.TotalSamples;
        long loopStart = 0, loopEnd = totalSamples;
        bool loopEnabled = false;

        long? startTag = null, endTag = null, lengthTag = null;
        if (reader.Tags?.All is not null)
        {
            foreach (var (keyRaw, values) in reader.Tags.All)
            {
                string key = keyRaw.Trim().ToUpperInvariant();
                foreach (var val in values)
                {
                    if (long.TryParse(val.Trim(), out var num))
                    {
                        if (key is "LOOPSTART" or "LOOP_START" or "LOOP_START_SAMPLE") startTag = num;
                        else if (key is "LOOPEND" or "LOOP_END" or "LOOP_END_SAMPLE") endTag = num;
                        else if (key is "LOOPLENGTH" or "LOOP_LENGTH") lengthTag = num;
                    }
                }
            }
        }

        if (startTag.HasValue)
        {
            loopStart = startTag.Value;
            if (endTag.HasValue) loopEnd = endTag.Value;
            else if (lengthTag.HasValue) loopEnd = loopStart + lengthTag.Value;
            loopEnabled = loopStart >= 0 && loopEnd > loopStart && loopEnd <= totalSamples;
        }

        return new("ogg", channels, sampleRate, totalSamples, loopEnabled, loopStart, loopEnd);
    }

    public static byte[] DecodeOggToWav(byte[] oggBytes, long? loopStart = null, long? loopEnd = null, bool loopEnabled = false)
    {
        using var inStream = new MemoryStream(oggBytes);
        using var reader = new VorbisReader(inStream, false);
        int channels = reader.Channels;
        int sampleRate = reader.SampleRate;
        if (channels is < 1 or > 2 || sampleRate is < 8000 or > 96000)
            throw new InvalidDataException("OGG must be mono/stereo and 8–96 kHz.");

        using var outStream = new MemoryStream();
        using var writer = new BinaryWriter(outStream);
        writer.Write("RIFF"u8);
        writer.Write(0u);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((ushort)(channels * 2));
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(0u);

        float[] buffer = new float[4096 * channels];
        byte[] pcmChunk = new byte[buffer.Length * 2];
        long totalFrames = 0;
        int samplesRead;
        while ((samplesRead = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            int byteCount = samplesRead * 2;
            for (int i = 0; i < samplesRead; i++)
            {
                short sample = (short)Math.Clamp(Math.Round(Math.Clamp(buffer[i], -1f, 1f) * 32768f), short.MinValue, short.MaxValue);
                pcmChunk[i * 2] = (byte)(sample & 0xFF);
                pcmChunk[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            outStream.Write(pcmChunk, 0, byteCount);
            totalFrames += samplesRead / channels;
        }

        if (totalFrames <= 0 || totalFrames > (long)sampleRate * 600)
            throw new InvalidDataException("Invalid OGG audio duration.");

        uint pcmBytes = checked((uint)(outStream.Position - 44));
        if (loopEnabled && loopStart.HasValue && loopEnd.HasValue && loopStart.Value >= 0 && loopEnd.Value > loopStart.Value && loopEnd.Value <= totalFrames)
        {
            writer.Write("smpl"u8);
            writer.Write(60u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((uint)(1_000_000_000.0 / sampleRate));
            writer.Write(60u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((uint)loopStart.Value);
            writer.Write((uint)(loopEnd.Value - 1));
            writer.Write(0u);
            writer.Write(0u);
        }

        uint totalFileSize = checked((uint)(outStream.Position - 8));
        writer.Seek(4, SeekOrigin.Begin);
        writer.Write(totalFileSize);
        writer.Seek(40, SeekOrigin.Begin);
        writer.Write(pcmBytes);

        return outStream.ToArray();
    }
}
