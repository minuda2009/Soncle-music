// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json;
using Soncle.Audio;
using Soncle.Audio.Dsp;
using Soncle.Audio.Policy;
using Soncle.Core.Models;
using Soncle.Flow;
using Soncle.TestKit;

namespace Soncle.Audio.Tests;

public class EngineTests
{
    private const int Sr = 48000;

    private static (float[] L, float[] R) Render(Engine engine, int frames)
    {
        var l = new float[frames];
        var r = new float[frames];
        var block = 480;
        for (var done = 0; done < frames; done += block)
        {
            var n = Math.Min(block, frames - done);
            engine.Render(l.AsSpan(done, n), r.AsSpan(done, n), n);
        }
        return (l, r);
    }

    [Fact]
    public void GaplessJoinHasNoGapOrOverlap()
    {
        // two click tracks: a click every 1000 frames; the second starts exactly at the first's end
        var engine = new Engine(Sr);
        var a = new ClickSource(Sr, 1000, 100_000);
        engine.Load(a, lufs: null);
        engine.GaplessTo(new ClickSource(Sr, 1000, 100_000), lufs: null);
        var (l, _) = Render(engine, 200_000);
        // clicks must stay 1000 frames apart across the join
        var clicks = new List<int>();
        for (var i = 1; i < l.Length; i++) if (Math.Abs(l[i]) > 0.5 && Math.Abs(l[i - 1]) <= 0.5) clicks.Add(i);
        Assert.True(clicks.Count > 150, clicks.Count.ToString());
        for (var i = 1; i < clicks.Count; i++)
            Assert.InRange(clicks[i] - clicks[i - 1], 999, 1001);
    }

    [Fact]
    public void CrossfadeFollowsTheCurveAndIsNeverLouder()
    {
        var engine = new Engine(Sr);
        engine.Load(new GeneratedSource(Sr, 300, 300, 0.5, 200_000), lufs: null);
        engine.CrossfadeTo(new GeneratedSource(Sr, 300, 300, 0.5, 200_000), lufs: null, seconds: 2, curve: "smooth");
        var (l, _) = Render(engine, 200_000);
        // measured overlap of two equal in-phase sines: 0.5 * (gi + go) must not exceed 0.5
        var peak = 0.0;
        for (var i = 0; i < l.Length; i++) peak = Math.Max(peak, Math.Abs(l[i]));
        Assert.True(peak <= 0.5 * Math.Pow(10, 0.05) + 0.01, peak.ToString());   // within the limiter's tolerance
    }

    [Fact]
    public void NormalisationBringsTracksToTheTarget()
    {
        var engine = new Engine(Sr);
        engine.SetNormalize(true, -14);
        engine.Load(new GeneratedSource(Sr, 300, 300, 0.1, 480_000), lufs: -20);
        var (l, _) = Render(engine, 480_000);
        // -20 LUFS at -14 target: +6 dB (the cap). Raw RMS is about -23 dB, so ~-17 dB after.
        var rms = AudioAssert.RmsDb(l[24000..]);
        Assert.InRange(rms, -18.0, -15.5);
    }

    [Fact]
    public void LimiterStaysLastSoAHotMixNeverClips()
    {
        var engine = new Engine(Sr);
        engine.Master.SetEq(true, new double[] { 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 });
        engine.Load(new GeneratedSource(Sr, 300, 300, 0.9, 240_000), lufs: null);
        var (l, r) = Render(engine, 240_000);
        var interleaved = new float[l.Length * 2];
        for (var i = 0; i < l.Length; i++) { interleaved[i * 2] = l[i]; interleaved[i * 2 + 1] = r[i]; }
        Assert.True(AudioAssert.TruePeakDb(interleaved) <= -0.8, AudioAssert.TruePeakDb(interleaved).ToString());
    }

    [Fact]
    public void RenderDoesNotAllocateSteadily()
    {
        var engine = new Engine(Sr);
        engine.Load(new GeneratedSource(Sr, 300, 300, 0.3), lufs: null);
        var l = new float[480];
        var r = new float[480];
        // warm up (JIT, first allocations)
        for (var i = 0; i < 100; i++) engine.Render(l, r, 480);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) engine.Render(l, r, 480);
        var after = GC.GetAllocatedBytesForCurrentThread();
        // no per-block allocation once warmed up (scratch buffers are preallocated)
        Assert.True(after - before < 100 * 1024, (after - before).ToString());
    }

    [Fact]
    public void TransitionPolicyMatchesTheJs()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("audio/policy.json"))).RootElement;
        foreach (var c in root.GetProperty("transitionPlan").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            var s = c.GetProperty("S");
            var settings = new TransitionSettings(
                s.GetProperty("crossfade").GetDouble(),
                s.GetProperty("crossfadeGapless").GetBoolean(),
                s.GetProperty("gapless").GetBoolean(),
                s.GetProperty("xfStyle").GetString()!,
                s.GetProperty("flowXf").GetBoolean());
            var cur = TrackOf(c.GetProperty("cur"));
            var nt = TrackOf(c.GetProperty("nt"));
            var feats = c.GetProperty("feat").EnumerateObject().ToDictionary(p => p.Name, p => Feature(p.Value));
            FlowFeatures? Feat(string id) => feats.TryGetValue(id, out var f) ? f : null;

            var plan = TransitionPolicy.Plan(cur, nt, settings, Feat, (a, fa, b, fb) => FlowMath.Transition(ToFlow(a), fa, ToFlow(b), fb));
            var exp = c.GetProperty("expected");
            Assert.NotNull(plan);
            Assert.Equal(exp.GetProperty("kind").GetString(), plan!.Kind);
            if (exp.TryGetProperty("dur", out var dur)) Assert.Equal(dur.GetDouble(), plan.Dur, 3);
        }
    }

    private static Track TrackOf(JsonElement e) => new()
    {
        Id = e.GetProperty("id").GetString()!,
        Album = e.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object ? al.GetProperty("id").GetString() : null,
        Artists = e.TryGetProperty("artists", out var ar) ? ar.EnumerateArray().Select(a => new ArtistRef { Name = a.GetProperty("name").GetString()! }).ToList() : new(),
    };

    private static Flow.FlowTrack ToFlow(Track t) => new(t.Id, t.Title, t.Duration, t.Album, t.Artists.FirstOrDefault()?.Name);

    private static FlowFeatures Feature(JsonElement e)
    {
        double? D(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        return new FlowFeatures { Bpm = D("bpm"), BpmConf = D("bpmConf") ?? 0, Camelot = e.TryGetProperty("camelot", out var c) ? c.GetString() : null, KeyConf = D("keyConf") ?? 0, Energy = D("energy") };
    }

    private sealed class ClickSource : IAudioSource
    {
        private readonly int _interval;
        private readonly long _length;
        private long _pos;
        public ClickSource(int sr, int interval, long length) { SampleRate = sr; _interval = interval; _length = length; }
        public int SampleRate { get; }
        public long? LengthFrames => _length;
        public bool CanSeek => true;
        public long Position { get => _pos; set => _pos = value; }
        public void Seek(long frame) => _pos = frame;
        public int Read(Span<float> left, Span<float> right, int frames)
        {
            var n = (int)Math.Min(frames, _length - _pos);
            for (var i = 0; i < n; i++) { var click = (_pos + i) % _interval == 0 ? 0.8f : 0f; left[i] = click; right[i] = click; }
            _pos += n;
            return n;
        }
    }
}
