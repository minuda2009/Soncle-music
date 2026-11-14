// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
using System.Text.Json.Nodes;
using Soncle.App.Commands;
using Soncle.App.Notifications;
using Soncle.App.Queue;
using Soncle.App.Settings;

namespace Soncle.App.Tests;

/// <summary>The <c>docs/DESIGN.md</c> §5.1/§5.2/§5.5 rules as tests (each named after the rule).</summary>
public class DesignFoundationTests
{
    // ---- §5.1 layered settings ----

    private static LayeredSettings Settings() => new(new JsonObject
    {
        ["crossfade"] = 3, ["autoplay"] = false, ["eq"] = new JsonObject { ["enabled"] = false },
        ["lyricsSize"] = 30,
    });

    [Fact]
    public void EffectiveSettingsAreBaseThenDeviceThenFocusThenTransientTopWins()
    {
        var s = Settings();
        Assert.Equal(3, s.Get("crossfade")!.GetValue<int>());
        Assert.Equal(SettingSource.Base, s.Resolve("crossfade").Source);

        s.SetDeviceProfile("HD 600", new JsonObject { ["crossfade"] = 5 });
        s.SelectDevice("HD 600");
        Assert.Equal(5, s.Get("crossfade")!.GetValue<int>());
        Assert.Equal(SettingSource.Device, s.Resolve("crossfade").Source);

        s.BeginFocus();
        Assert.Equal(8, s.Get("crossfade")!.GetValue<int>());   // focus wins over the device
        Assert.Equal(SettingSource.Focus, s.Resolve("crossfade").Source);

        s.SetTransient("crossfade", 1);
        Assert.Equal(1, s.Get("crossfade")!.GetValue<int>());   // transient wins over everything
        Assert.Equal(SettingSource.Transient, s.Resolve("crossfade").Source);
    }

    [Fact]
    public void EndingFocusRemovesTheOverlayAndNothingNeedsRestoring()
    {
        var s = Settings();
        s.SetDeviceProfile("HD 600", new JsonObject { ["crossfade"] = 5 });
        s.SelectDevice("HD 600");
        var before = s.Effective().ToJsonString();

        s.BeginFocus();
        Assert.True(s.FocusActive);
        Assert.Equal(8, s.Get("crossfade")!.GetValue<int>());

        s.EndFocus();
        Assert.False(s.FocusActive);
        Assert.Equal(before, s.Effective().ToJsonString());     // exactly as before focus started
    }

    [Fact]
    public void FocusOnlyOverridesItsKeysAndLeavesOthersAlone()
    {
        var s = Settings();
        s.BeginFocus();
        Assert.Equal(30, s.Get("lyricsSize")!.GetValue<int>());  // not a focus key
        Assert.Equal(SettingSource.Base, s.Resolve("lyricsSize").Source);
        Assert.True(s.Get("autoplay")!.GetValue<bool>());        // a focus key
        Assert.False(s.Get("discord")!.GetValue<bool>());        // focus turns discord off
    }

    // ---- §5.2 queue with origins ----

    [Fact]
    public void SectionsSplitUpNextFromThenFromFlow()
    {
        var q = new QueueModel();
        q.SetSource(new[] { "a", "b" });
        q.Add(new[] { "user1" }, QueueOrigin.User, playNext: true);
        q.AppendFlow(new[] { new QueueItem("f1", "flow1", QueueOrigin.Flow), new QueueItem("f2", "flow2", QueueOrigin.Flow) });
        q.SetCurrentIndex(0);

        var (upNext, thenFlow) = q.Sections();
        Assert.Equal(new[] { "user1", "b" }, upNext.Select(x => x.TrackId));   // the user song first, then the source
        Assert.Equal(new[] { "flow1", "flow2" }, thenFlow.Select(x => x.TrackId));
    }

    [Fact]
    public void ClearQueueOffersUndoAndUndoRestoresTheOrder()
    {
        var q = new QueueModel();
        q.SetSource(new[] { "a", "b", "c" });
        q.Clear();
        Assert.Empty(q.Items);
        Assert.True(q.Undo());
        Assert.Equal(new[] { "a", "b", "c" }, q.Items.Select(x => x.TrackId));
    }

    [Fact]
    public void AManualDragLocksTheFlowPlanAndResumeClearsIt()
    {
        var q = new QueueModel();
        q.SetSource(new[] { "a", "b", "c" });
        q.AppendFlow(new[] { new QueueItem("f1", "flow1", QueueOrigin.Flow) });   // index 3
        Assert.False(q.FlowLocked);

        q.Move(1, 2);                                            // a source item moved
        Assert.False(q.FlowLocked);                              // moving a non-Flow item does not lock

        q.Move(3, 0);                                            // now the Flow item is dragged
        Assert.True(q.FlowLocked);
        q.ResumeFlow();
        Assert.False(q.FlowLocked);
    }

    [Fact]
    public void AddingAUserSongLocksThePlan()
    {
        var q = new QueueModel();
        q.SetSource(new[] { "a" });
        q.Add(new[] { "b" }, QueueOrigin.User);
        Assert.True(q.FlowLocked);
    }

    [Fact]
    public void ShuffleIsAnOrderingViewAndTurningItOffRestoresTheOrder()
    {
        var q = new QueueModel();
        var ids = Enumerable.Range(0, 30).Select(i => "t" + i).ToArray();
        q.SetSource(ids);
        q.SetCurrentIndex(0);
        var current = q.Current!.TrackId;

        q.SetShuffle(true);
        Assert.Equal(current, q.Current!.TrackId);               // the playing song stays put
        Assert.Equal(ids.OrderBy(x => x).ToArray(), q.Items.Select(x => x.TrackId).OrderBy(x => x).ToArray());
        Assert.Equal(ids.Length, q.Items.Count);                 // no items lost or duplicated

        q.SetShuffle(false);
        Assert.Equal(ids, q.Items.Select(x => x.TrackId));
    }

    // ---- §5.4 command registry ----

    [Fact]
    public void ThePaletteFiltersByWhenAndSearchesTitleShortcutAndWords()
    {
        var reg = new CommandRegistry();
        var cur = false;
        reg.Add(new Command("play", "Play", "play", () => { }, "Space"));
        reg.Add(new Command("next", "Next song", "next", () => { }, "Ctrl+Right", () => cur));
        reg.Add(new Command("radio", "Start Flow radio from this song", "radio", () => { }, CanExecute: () => cur, Words: "mix similar"));

        Assert.Equal(new[] { "play" }, reg.Available().Select(c => c.Id));          // "next"/"radio" are hidden with no song

        cur = true;
        Assert.Equal(new[] { "play", "next", "radio" }, reg.Available().Select(c => c.Id));
        Assert.Equal(new[] { "radio" }, reg.Search("mix similar").Select(c => c.Id));   // extra words
        Assert.Equal(new[] { "next" }, reg.Search("Ctrl+Right").Select(c => c.Id));     // shortcut
        Assert.Equal(3, reg.Search("").Count());                                        // empty query lists all available
    }

    [Fact]
    public void RunningAnUnavailableCommandDoesNothing()
    {
        var reg = new CommandRegistry();
        var ran = 0;
        reg.Add(new Command("next", "Next song", "next", () => ran++, CanExecute: () => false));
        Assert.False(reg.Run("next"));
        Assert.False(reg.Run("does-not-exist"));
        Assert.Equal(0, ran);
    }

    [Fact]
    public void TheShortcutListComesFromTheSameRegistry()
    {
        var reg = new CommandRegistry();
        reg.Add(new Command("play", "Play", "play", () => { }, "Space"));
        reg.Add(new Command("queue", "Queue", "queue", () => { }));          // no shortcut
        reg.Add(new Command("mini", "Mini player", "mini", () => { }, "N"));
        Assert.Equal(new[] { ("Space", "Play"), ("N", "Mini player") }, reg.Shortcuts());
    }

    // ---- §5.5 notification levels ----

    [Fact]
    public void SilentNoticesAreNeverShown()
    {
        var n = new NotificationService(() => 0);
        Assert.False(n.Push("Shuffle on", NoticeLevel.Silent));
        Assert.Null(n.Current);
    }

    [Fact]
    public void AnActionMessageIsNotReplacedByAnInfoOne()
    {
        var t = 0L;
        var n = new NotificationService(() => t);
        n.Push("Removed from queue", NoticeLevel.Action, "Undo", () => { });
        t = 100;
        n.Push("Saved", NoticeLevel.Info);
        Assert.Equal("Removed from queue", n.Current!.Message);
        Assert.Equal(NoticeLevel.Action, n.Current.Level);
    }

    [Fact]
    public void AMoreImportantMessageTakesTheSlotAndTheOldOneComesBack()
    {
        var t = 0L;
        var n = new NotificationService(() => t);
        n.Push("Saved", NoticeLevel.Info);
        t = 100;
        n.Push("No connection", NoticeLevel.Important);
        Assert.Equal("No connection", n.Current!.Message);
        n.Dismiss();
        Assert.Equal("Saved", n.Current!.Message);               // the info one returns
    }

    [Fact]
    public void DuplicatesWithinAFewSecondsMerge()
    {
        var t = 0L;
        var n = new NotificationService(() => t);
        var shown = 0;
        n.Changed += x => { if (x is not null) shown++; };
        n.Push("Saved", NoticeLevel.Info);
        t = 500;
        n.Push("Saved", NoticeLevel.Info);
        Assert.Equal(1, shown);                                  // the second one merged, no re-show
        Assert.Equal("Saved", n.Current!.Message);
    }
}
