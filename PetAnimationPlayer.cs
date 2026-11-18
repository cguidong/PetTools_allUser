using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OffWorkWidget.WinUI;

// Native premultiplied BGRA frames prepared from the alpha WebM at build time.
// Only active playback or a short transition subscribes to the render loop.
internal sealed class PetAnimationPlayer : IDisposable
{
    readonly Image target;
    readonly byte[] frames;
    readonly int width, height, count, fps, frameBytes;
    readonly WriteableBitmap bitmap;
    readonly Stream pixels;
    readonly Stopwatch clock = new();
    int frame;
    bool playing, hoverPlayback, armed = true, outside = true, disposed;
    bool triggeredLoop, returning, blended;
    long lastTriggerAt, transitionAt;
    byte[]? transitionFrom, returnFrame, blendBuffer, fadeFrom;
    int fadeMs;
    Action? loopCompleted;
    internal const int TriggerQuietMilliseconds = 1000;
    const int EnterMilliseconds = 220, ReturnMilliseconds = 240;

    PetAnimationPlayer(Image target, byte[] frames, int width, int height, int count, int fps)
    {
        this.target = target; this.frames = frames; this.width = width; this.height = height;
        this.count = count; this.fps = fps; frameBytes = checked(width * height * 4);
        bitmap = new WriteableBitmap(width, height);
        pixels = bitmap.PixelBuffer.AsStream();
    }

    internal static async Task<PetAnimationPlayer> LoadAsync(Image target, string path)
    {
        var dispatcher = target.DispatcherQueue;
        var data = await Task.Run(() =>
        {
            using var file = File.OpenRead(path);
            using Stream decompressor = path.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
                ? new BrotliStream(file, CompressionMode.Decompress)
                : new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(decompressor);
            if (new string(reader.ReadChars(8)) != "OWPET001") throw new InvalidDataException("Invalid pet animation header.");
            int w = reader.ReadInt32(), h = reader.ReadInt32(), n = reader.ReadInt32(), rate = reader.ReadInt32();
            if (w < 1 || w > 512 || h < 1 || h > 512 || n < 1 || n > 1000 || rate < 1 || rate > 60)
                throw new InvalidDataException("Invalid pet animation dimensions.");
            int length = checked(w * h * 4 * n);
            if (length > 100 * 1024 * 1024) throw new InvalidDataException("Pet animation is too large.");
            var bytes = new byte[length];
            decompressor.ReadExactly(bytes);
            if (decompressor.ReadByte() != -1) throw new InvalidDataException("Unexpected pet animation data.");
            return (bytes, w, h, n, rate);
        });
        var ready = new TaskCompletionSource<PetAnimationPlayer>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try { ready.SetResult(new PetAnimationPlayer(target, data.bytes, data.w, data.h, data.n, data.rate)); }
            catch (Exception ex) { ready.SetException(ex); }
        })) throw new InvalidOperationException("The pet window is closing.");
        return await ready.Task.ConfigureAwait(false);
    }

    internal void ShowPoster()
    {
        if (disposed) return;
        target.Source = bitmap; ShowFrame(0);
        GlassProfile.Event("pet-animation-ready");
    }

    internal bool HitTest(double x, double y)
    {
        double side = Math.Min(target.ActualWidth, target.ActualHeight);
        if (side <= 0) return false;
        x -= (target.ActualWidth - side) / 2;
        y -= (target.ActualHeight - side) / 2;
        if (x < 0 || y < 0 || x >= side || y >= side) return false;
        int px = Math.Min(width - 1, (int)(x / side * width));
        int py = Math.Min(height - 1, (int)(y / side * height));
        // Use the resting silhouette so moving fur cannot synthesize a mouse leave.
        return frames[(py * width + px) * 4 + 3] >= 32;
    }

    internal void SetHover(bool inside, bool overPetPixels)
    {
        if (disposed) return;
        outside = !overPetPixels;
        if (playing) return;
        if (outside) { armed = true; return; }
        if (!inside) return;
        if (!armed) return;
        armed = false; StartPlayback(true);
    }

    internal void PlayOnce()
    {
        if (disposed) return;
        fadeFrom = null; fadeMs = 0;
        if (playing) Stop();
        armed = false; StartPlayback(false);
    }

    internal void PlayOnceFrom(byte[]? fromFrame, int fadeMilliseconds)
    {
        if (disposed) return;
        if (fromFrame == null || fromFrame.Length != frameBytes || fadeMilliseconds <= 0)
        {
            PlayOnce();
            return;
        }
        if (playing) Stop();
        armed = false;
        fadeFrom = fromFrame; fadeMs = fadeMilliseconds;
        blendBuffer ??= new byte[frameBytes];
        Buffer.BlockCopy(fromFrame, 0, blendBuffer, 0, frameBytes);
        target.Source = bitmap;
        pixels.Position = 0; pixels.Write(blendBuffer, 0, frameBytes); bitmap.Invalidate();
        frame = 0; blended = true;
        clock.Restart();
        playing = true; hoverPlayback = false;
        CompositionTarget.Rendering += Render;
        GlassProfile.Event("pet-animation-fade-start");
    }

    internal bool RestorePosterAfterHover { get; set; }
    internal bool IsPlaying => playing;

    internal TimeSpan Duration => TimeSpan.FromSeconds((double)count / fps);

    internal byte[] CaptureFrame()
    {
        var copy = new byte[frameBytes];
        Buffer.BlockCopy(blended ? blendBuffer! : frames, blended ? 0 : frame * frameBytes, copy, 0, frameBytes);
        return copy;
    }

    internal byte[] PosterFrame()
    {
        var copy = new byte[frameBytes];
        Buffer.BlockCopy(frames, 0, copy, 0, frameBytes);
        return copy;
    }

    internal void StartTriggeredLoop(byte[] from, byte[] idle, Action completed)
    {
        if (disposed) return;
        if (from.Length != frameBytes || idle.Length != frameBytes) throw new ArgumentException("Pet transition dimensions differ.");
        Stop();
        triggeredLoop = playing = true;
        transitionFrom = from; returnFrame = idle; loopCompleted = completed;
        lastTriggerAt = transitionAt = 0;
        target.Source = bitmap; ShowBlend(from, 0, frames, 0, 0); clock.Restart();
        CompositionTarget.Rendering += Render;
        GlassProfile.Event("pet-coin-animation-start");
    }

    internal void PulseTriggeredLoop()
    {
        if (disposed || !playing || !triggeredLoop) return;
        lastTriggerAt = clock.ElapsedMilliseconds;
        if (returning)
        {
            transitionFrom = CaptureFrame(); transitionAt = lastTriggerAt; returning = false;
        }
        GlassProfile.Event("pet-coin-animation-pulse");
    }

    internal void ResetIdle()
    {
        if (disposed) return;
        Stop();
        armed = true; outside = true;
        target.Source = bitmap; ShowFrame(0);
    }

    internal void ShowLastFrame()
    {
        if (disposed) return;
        Stop();
        armed = false;
        target.Source = bitmap; ShowFrame(count - 1);
    }

    internal byte[] LastFrame()
    {
        var copy = new byte[frameBytes];
        Buffer.BlockCopy(frames, (count - 1) * frameBytes, copy, 0, frameBytes);
        return copy;
    }

    void StartPlayback(bool hover)
    {
        hoverPlayback = hover;
        playing = true; target.Source = bitmap; ShowFrame(0); clock.Restart();
        CompositionTarget.Rendering += Render;
        GlassProfile.Event(hover ? "pet-animation-start" : "pet-reminder-animation-start");
    }

    void Render(object? sender, object args)
    {
        if (disposed || !playing) return;
        if (triggeredLoop) { RenderTriggeredLoop(); return; }
        if (fadeFrom != null)
        {
            long ms = clock.ElapsedMilliseconds;
            if (ms < fadeMs)
            {
                int blendFrame = Math.Min(count - 1, (int)(clock.Elapsed.TotalSeconds * fps));
                ShowBlend(fadeFrom, 0, frames, blendFrame * frameBytes, ms / (double)fadeMs);
                frame = blendFrame;
                return;
            }
            fadeFrom = null;
            GlassProfile.Event("pet-animation-fade-end");
        }
        int next = (int)(clock.Elapsed.TotalSeconds * fps);
        if (next >= count)
        {
            if (frame != count - 1) ShowFrame(count - 1);
            bool hover = hoverPlayback;
            Stop();
            if (hover && RestorePosterAfterHover) ShowFrame(0);
            if (hover) armed = outside;
            GlassProfile.Event(hover ? "pet-animation-end" : "pet-reminder-animation-end");
        }
        else if (next != frame) ShowFrame(next);
    }

    void RenderTriggeredLoop()
    {
        long now = clock.ElapsedMilliseconds;
        if (!returning && now - lastTriggerAt >= TriggerQuietMilliseconds)
        {
            transitionFrom = CaptureFrame(); transitionAt = now; returning = true;
            GlassProfile.Event("pet-coin-animation-return");
        }
        if (returning)
        {
            double progress = Math.Clamp((now - transitionAt) / (double)ReturnMilliseconds, 0, 1);
            ShowBlend(transitionFrom!, 0, returnFrame!, 0, progress);
            if (progress >= 1)
            {
                var completed = loopCompleted;
                Stop();
                GlassProfile.Event("pet-coin-animation-end");
                completed?.Invoke();
            }
            return;
        }
        int next = (int)(now * fps / 1000 % count);
        if (transitionFrom != null && now - transitionAt < EnterMilliseconds)
        {
            ShowBlend(transitionFrom, 0, frames, next * frameBytes, (now - transitionAt) / (double)EnterMilliseconds);
            frame = next;
        }
        else
        {
            transitionFrom = null;
            if (next != frame || blended) ShowFrame(next);
        }
    }

    void ShowBlend(byte[] from, int fromOffset, byte[] to, int toOffset, double progress)
    {
        blendBuffer ??= new byte[frameBytes];
        progress = Math.Clamp(progress, 0, 1);
        int weight = (int)Math.Round(progress * progress * (3 - 2 * progress) * 255);
        for (int i = 0; i < frameBytes; i++)
            blendBuffer[i] = (byte)((from[fromOffset + i] * (255 - weight) + to[toOffset + i] * weight + 127) / 255);
        blended = true;
        pixels.Position = 0; pixels.Write(blendBuffer, 0, frameBytes); bitmap.Invalidate();
    }

    void ShowFrame(int index)
    {
        frame = index; blended = false;
        pixels.Position = 0; pixels.Write(frames, index * frameBytes, frameBytes); bitmap.Invalidate();
        if (GlassProfile.Enabled) GlassProfile.Event("pet-animation-frame " + index);
    }

    internal void Stop()
    {
        if (playing) CompositionTarget.Rendering -= Render;
        playing = false; hoverPlayback = false; armed = false; clock.Stop();
        triggeredLoop = returning = false;
        transitionFrom = returnFrame = null; loopCompleted = null;
        fadeFrom = null; fadeMs = 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop(); disposed = true; pixels.Dispose();
    }
}
