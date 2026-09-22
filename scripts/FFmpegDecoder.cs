using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

public class FFmpegDecoder
{
    public int Width { get; private set; }

    public int Height { get; private set; }

    public string InputPath { get; private set; }

    public string FFmpegExe { get; private set; }

    public string FFprobeExe { get; private set; }

    public string PxFormat { get; private set; }

    public ConcurrentQueue<byte[]> DecodedFrames { get; private set; } = new ConcurrentQueue<byte[]>(); // uses 10 GB of ram for 2 sec of footage

    public bool IsRunning;

    private Thread readThread;

    public FFmpegDecoder(string ffmpegPath, string ffprobePath, string inputPath)
    {
        InputPath = inputPath;
        FFmpegExe = ffmpegPath;
        FFprobeExe = ffprobePath;
    }

    public async Task UpdateMetadata()
    {
        JsonNode ffprobeJson = await GetVideoMetadata();

        // 0 stream video, 1 stream audio (unless there are more languages?)

        if (ffprobeJson["streams"][0]["codec_type"].GetValue<string>() == "video")
        {
            Width = ffprobeJson["streams"][0]["width"].GetValue<int>();
            Height = ffprobeJson["streams"][0]["height"].GetValue<int>();

            PxFormat = ffprobeJson["streams"][0]["pix_fmt"].GetValue<string>();
        }
        else { Logger.Error($"Stream 0 of {InputPath} is not a video"); } // should ideally throw/return/stop decoding
    }

    // frame size could be unresonably big
    public async Task<JsonNode> GetVideoMetadata()
    {
        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = FFprobeExe,
            Arguments = $"-v quiet -print_format json -show_streams \"{InputPath}\"", // returns whole video info, maybe shorten the info returned?
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process ffprobeProc = Process.Start(processStartInfo);
        string ffprobeStdout = await ffprobeProc.StandardOutput.ReadToEndAsync();
        await ffprobeProc.WaitForExitAsync();

        JsonNode ffprobeJsonOutput = JsonNode.Parse(ffprobeStdout);

        return ffprobeJsonOutput;
    }

    public void GetVideoChunk(double startTime, double chunkDuration, double fps)
    {
        int frameWeight = Width * Height * 4; // 4 because rgba

        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = FFmpegExe,
            Arguments = $"-ss {startTime.ToString(CultureInfo.InvariantCulture)} -i \"{InputPath}\" -t {chunkDuration.ToString(CultureInfo.InvariantCulture)} -f rawvideo -pix_fmt rgba -vf fps={fps.ToString(CultureInfo.InvariantCulture)} -an -", // add changeable bg framerate (in settings)
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process ffmpegProc = Process.Start(processStartInfo);
        var ffmpegStdoutStream = ffmpegProc.StandardOutput.BaseStream;

        readThread = new Thread(() =>
        {
            byte[] buffer = new byte[frameWeight];

            while (IsRunning)
            {
                int offset = 0;
                while (offset < frameWeight)
                {
                    int read = ffmpegStdoutStream.Read(buffer, offset, frameWeight - offset);
                    if (read <= 0)
                    {
                        offset = -1;
                        break;
                    }
                    offset += read;
                }
                if (offset == -1) { break; }
                DecodedFrames.Enqueue((byte[])buffer.Clone());
            }
        }){ IsBackground = true };

        readThread.Start();
    }
}