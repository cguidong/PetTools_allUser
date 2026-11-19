using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OffWorkWidget.WinUI;

public sealed partial class MainWindow
{
    InputActivityService? activityInput;
    bool activitySessionLocked, activityShopComplete;
    int pendingActivityCoins;
    int activityBatchCount;
    long nextActivityFlush, lastActivityBreath = -1000, lastLocalCoin = -1000;
    Task<int>? activityBatch;

    bool EarnsFromGlobalActivity => !activitySessionLocked && !activityShopComplete && activityInput?.Listening == true;
    static bool SupportsTypingBreath(int id) => id is 2 or 8 or 12 or 13 or 15 or 19;

    void InitializeActivityInput()
    {
        activityInput = new InputActivityService(hwnd);
        nextActivityFlush = elapsed.ElapsedMilliseconds + 30_000;
        RefreshActivityShopState();
        privacy.EffectUnlockedChanged += RefreshActivityShopState;
    }

    void RefreshActivityShopState()
    {
        activityShopComplete = privacy.AllShopEffectsUnlocked();
        UpdateActivityRegistration();
    }

    void UpdateActivityRegistration()
    {
        if (activityInput == null || disposed || exiting) return;
        bool needed = !activitySessionLocked &&
            (!activityShopComplete || (uiSettings.AnimationsEnabled && SupportsTypingBreath(shopEffectPreview ?? privacy.SelectedEffect)));
        if (activityInput.Listening == needed) return;
        if (activityInput.SetListening(needed))
            GlassProfile.Event(needed ? "activity-listening" : "activity-stopped");
        else DebugActivityRegistrationFailure();
    }

    static void DebugActivityRegistrationFailure() =>
        App.RecordEvent("Raw Input registration failed: " + Marshal.GetLastWin32Error());

    void OnActivityMessage(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
    {
        if (disposed || exiting || activitySessionLocked || activityInput?.Listening != true) return;
        var message = args.Message;
        if (message.MessageId == InputActivityService.DeviceChangeMessage)
        {
            if (message.WParam == 2) activityInput.ForgetDevice(message.LParam);
            return;
        }
        if (message.MessageId != InputActivityService.InputMessage) return;
        var (count, keyboard) = activityInput.ReadActivity(message.LParam);
        if (count == 0) return;
        if (!activityShopComplete) pendingActivityCoins = (int)Math.Min(int.MaxValue, (long)pendingActivityCoins + count);
        long now = elapsed.ElapsedMilliseconds;
        if (keyboard && uiSettings.AnimationsEnabled && now - lastActivityBreath >= 75 && SupportsTypingBreath(ambientEffectKind))
        {
            lastActivityBreath = now;
            if (now + 200 < nextAmbientEffectAt) nextAmbientEffectAt = now + 200;
        }
        // Leave WM_INPUT unhandled so the original window procedure can clean it up.
    }

    void FlushActivityCoins(bool closing = false)
    {
        if (activityBatch != null && (closing || activityBatch.IsCompleted))
        {
            try
            {
                emittedCoins = Math.Max(emittedCoins, activityBatch.GetAwaiter().GetResult());
                if (!closing) privacy.NotifyBalanceChanged();
            }
            catch (Exception error)
            {
                pendingActivityCoins = (int)Math.Min(int.MaxValue, (long)pendingActivityCoins + activityBatchCount);
                App.RecordEvent("Activity coins batch failed: " + error.GetType().Name);
            }
            finally
            {
                activityBatch = null;
                activityBatchCount = 0;
            }
        }
        long now = elapsed.ElapsedMilliseconds;
        if (!closing && now < nextActivityFlush) return;
        nextActivityFlush = now + 30_000;
        if (pendingActivityCoins == 0 || activityBatch != null) return;
        if (closing)
        {
            try { privacy.AddCoins(pendingActivityCoins); }
            catch (IOException error) { App.RecordEvent("Activity coins exit save failed: " + error.GetType().Name); }
            return;
        }

        int count = activityBatchCount = pendingActivityCoins;
        pendingActivityCoins = 0;
        activityBatch = Task.Run(() =>
        {
            using var store = new PrivacyStore(Path.Combine(dataFolder, "privacy.db"), 0);
            return store.AddCoins(count);
        });
    }

    void EarnFromWidgetClick(Windows.Foundation.Point point)
    {
        long now = elapsed.ElapsedMilliseconds;
        if (EarnsFromGlobalActivity || activitySessionLocked || now - lastLocalCoin < 140) return;
        lastLocalCoin = now;
        int roll = Random.Shared.Next(100);
        EmitCoin(point, roll < 2 ? 3 : roll < 10 ? 2 : 1);
    }
}
