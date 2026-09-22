using System;
using Godot;

public partial class VideoTextureRenderer : Node
{
    // reformat these, they are all placed randomly
    public bool IsPlaying
    {
        get;
        set
        {
            if (value) { Play(); }
            else { Stop(); }

            field = value;
        }
    }

    public VideoTextureRenderer Instance;

    public FFmpegDecoder Decoder;

    public Action<Texture2D> OnTextureUpdate;

    public string InputPath;

    public double Fps = 60;

    public double PlaybackSpeed = 1.0;

    private ImageTexture texture;

    private double playbackPosition = 0;

    private int lastFrame = -1;

    private double timeSinceLastChunk = 2; // the same as decode interval

    private double chunkDecodeInterval = 2;

    private bool isReady = false;

    public VideoTextureRenderer(FFmpegDecoder decoder, double fps, Action<Texture2D> onTextureUpdate)
    {
        Decoder = decoder;
        Fps = fps;
        OnTextureUpdate = onTextureUpdate;
    }

    public override async void _Ready()
    {
        Instance = this;

        SetProcess(false);
        await Decoder.UpdateMetadata();

        Decoder.IsRunning = false;
        IsPlaying = false;
        isReady = true;
    }

    public override void _Process(double delta)
    {
        if (!isReady) { return; }

        timeSinceLastChunk += delta;
        playbackPosition += delta * PlaybackSpeed;

        if (timeSinceLastChunk >= chunkDecodeInterval)
        {
            Decoder.GetVideoChunk(playbackPosition, chunkDecodeInterval, Fps);
            timeSinceLastChunk = 0;
        }

        int targetFrame = (int)(playbackPosition * Fps);

        if (targetFrame > lastFrame && Decoder.DecodedFrames.TryDequeue(out byte[] frameBytes))
        {
            var image = Image.CreateFromData(Decoder.Width, Decoder.Height, false, Image.Format.Rgba8, frameBytes);
            if (texture == null) { texture = ImageTexture.CreateFromImage(image); }
            else { texture.Update(image); }

            lastFrame = targetFrame;
            OnTextureUpdate(texture);
        }
    }

    public void Play()
    {
        SetProcess(true);
        Decoder.IsRunning = true;
    }

    public void Stop()
    {
        SetProcess(false);
        Decoder.IsRunning = false;
    }
}