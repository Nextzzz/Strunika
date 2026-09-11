using System.Collections.Specialized;
using System.ComponentModel;
using Strunika.Mobile.Controls;
using Strunika.Mobile.Theme;
using Strunika.Mobile.Pages;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Views;

public partial class TunerView : ContentView
{
    /// <summary>The share of the free height the headstock takes, between the
    /// least its pegs need and the most they can use.</summary>
    private const double NeckShare = 0.4;

    public TunerView()
    {
        InitializeComponent();
        Root.SizeChanged += (_, _) => QueueArrange();
        Fan.SizeChanged += (_, _) => QueueArrange();
        PegRow.ChildAdded += (_, e) =>
        {
            if (e.Element is View peg) peg.SizeChanged += OnPegSized;
            QueueArrange();
        };
        PegRow.ChildRemoved += (_, e) =>
        {
            if (e.Element is View peg) peg.SizeChanged -= OnPegSized;
            QueueArrange();
        };
        BindingContextChanged += (_, _) =>
        {
            if (BindingContext is TunerViewModel vm)
            {
                vm.ProRequired += (_, feature) => _ = PaywallSheet.ShowAsync(feature);
                vm.StringTuned += (_, index) => _ = BouncePegAsync(index);
                vm.AllTunedReached += (_, _) => _ = CelebrateAsync(vm);
                vm.PropertyChanged += OnVmPropertyChanged;
                vm.Pegs.CollectionChanged += OnPegsChanged;
                foreach (var peg in vm.Pegs) peg.PropertyChanged += OnPegChanged;
                ApplyHero(animated: false);
                SyncStrings();
            }
        };
    }

    private TunerViewModel? Vm => BindingContext as TunerViewModel;

    // ---- the hero ---------------------------------------------------------

    private bool? _reading;
    private int _heroTurn;

    /// <summary>The note while a string sounds, the line in silence — a short
    /// cross-fade between them, and "all set" springs in when it arrives.</summary>
    private async void ApplyHero(bool animated)
    {
        if (Vm is not { } vm) return;
        bool reading = vm.HasSignal;
        if (_reading == reading) return;
        _reading = reading;
        int turn = ++_heroTurn;
        View show = reading ? ReadingBlock : SilenceBlock;
        View hide = reading ? SilenceBlock : ReadingBlock;
        IdleLabel.Scale = 1;
        if (!animated || Motion.Reduced)
        {
            hide.IsVisible = false;
            hide.Opacity = 1;
            show.Opacity = 1;
            show.IsVisible = true;
            return;
        }
        show.Opacity = 0;
        show.IsVisible = true;
        var pop = !reading && vm.AllTuned ? PopIdleAsync() : Task.CompletedTask;
        await Task.WhenAll(hide.FadeToAsync(0, 120, Easing.CubicIn), show.FadeToAsync(1, 180, Easing.CubicOut), pop);
        if (turn != _heroTurn) return;                           // the other one is already on its way back
        hide.IsVisible = false;
        hide.Opacity = 1;
    }

    private async Task PopIdleAsync()
    {
        if (Motion.Reduced) return;
        IdleLabel.Scale = 0.85;
        await IdleLabel.ScaleToAsync(1, 420, Easing.SpringOut);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TunerViewModel.HasSignal):
                ApplyHero(animated: true);
                SyncSound();
                break;
            case nameof(TunerViewModel.Level):
            case nameof(TunerViewModel.Cents):
            case nameof(TunerViewModel.InTune):
                SyncSound();
                break;
            case nameof(TunerViewModel.AllTuned):
                // The last string counted in the quiet after its note: the line changes in place.
                if (Vm is { AllTuned: true, HasSignal: false }) _ = PopIdleAsync();
                break;
        }
    }

    // ---- the headstock ------------------------------------------------------

    private void OnPegsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (PegItem peg in e.OldItems) peg.PropertyChanged -= OnPegChanged;
        if (e.NewItems != null) foreach (PegItem peg in e.NewItems) peg.PropertyChanged += OnPegChanged;
        SyncStrings();
    }

    private void OnPegChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(PegItem.IsActive) or nameof(PegItem.IsTuned))) return;
        SyncStrings();
        SyncSound();
    }

    /// <summary>Each string takes its peg's colours.</summary>
    private void SyncStrings()
    {
        if (Vm is not { } vm) return;
        Fan.SetStates(vm.Pegs.Select(p => (p.IsActive, p.IsTuned)).ToList());
    }

    /// <summary>The string being read shakes while it sounds — as hard as it is
    /// played, steady once in tune.</summary>
    private void SyncSound()
    {
        if (Vm is not { } vm) return;
        int sounding = -1;
        if (vm.HasSignal)
            for (int i = 0; i < vm.Pegs.Count; i++)
                if (vm.Pegs[i].IsActive) { sounding = i; break; }
        Fan.Sound(sounding, vm.Level, vm.InTune ? 0 : vm.Cents);
    }

    private bool _arrangeQueued;

    private void OnPegSized(object? sender, EventArgs e) => QueueArrange();

    /// <summary>Once, after the layout pass: the rows above have their heights by then.</summary>
    private void QueueArrange()
    {
        if (_arrangeQueued) return;
        _arrangeQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _arrangeQueued = false;
            ArrangeNeck();
        });
    }

    /// <summary>
    /// The headstock's height comes from the page, not from whatever is above
    /// it: the "start again" chip appearing must not move a peg while a string
    /// is being tuned. It takes a share of the room the fixed rows leave — no
    /// less than its pegs need, no more than they can use — and reaches down by
    /// the padding kept for the tab bar, so the strings run on under the bar.
    /// </summary>
    private void ArrangeNeck()
    {
        var pegs = PegRow.Children.OfType<View>().ToList();
        if (pegs.Count == 0 || Root.Height <= 0) return;
        double peg = pegs[0].Width > 0 ? pegs[0].Width : pegs[0].WidthRequest;
        if (peg <= 0) return;
        double below = Root.Padding.Bottom;
        double free = Root.Height - Root.Padding.VerticalThickness - TitleRow.Height
                      - ChipsRow.Height - ChipsRow.Margin.VerticalThickness - StringIndicator.Height;
        double least = Headstock.HeightFor(peg, pegs.Count, roomy: false);
        double most = Math.Max(least, Headstock.HeightFor(peg, pegs.Count, roomy: true));
        double height = Math.Clamp(free * NeckShare, least, most);
        var margin = new Thickness(0, Metrics.Instance.Size(12), 0, -below);
        if (Neck.Margin != margin) Neck.Margin = margin;
        if (Math.Abs(Neck.HeightRequest - (height + below)) > 0.5) Neck.HeightRequest = height + below;
        Fan.Arrange(pegs, below);
    }

    // ---- moments ------------------------------------------------------------

    /// <summary>A string just got tuned: it twangs, and its peg swells and springs back.</summary>
    private async Task BouncePegAsync(int index)
    {
        Fan.Pluck(index);
        if (Motion.Reduced) return;
        var pegs = PegRow.Children.OfType<View>().ToList();
        if (index < 0 || index >= pegs.Count) return;
        var peg = pegs[index];
        await peg.ScaleToAsync(1.35, 160, Easing.CubicOut);
        await peg.ScaleToAsync(1.0, 420, Easing.SpringOut);
    }

    /// <summary>Last string tuned: the pegs bounce and the strings twang in a
    /// wave, and the string flashes; the tuner goes on listening.</summary>
    private async Task CelebrateAsync(TunerViewModel vm)
    {
        Haptics.Default.Success();
        StringIndicator.Celebrate();
        if (Motion.Reduced) return;
        var pegs = PegRow.Children.OfType<View>().ToList();
        var tasks = new List<Task>();
        for (int i = 0; i < pegs.Count; i++)
        {
            var peg = pegs[i];
            int index = i, delay = i * 70;
            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(delay);
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    Fan.Pluck(index);
                    await peg.ScaleToAsync(1.28, 130, Easing.CubicOut);
                    await peg.ScaleToAsync(1.0, 320, Easing.SpringOut);
                });
            }));
        }
        await Task.WhenAll(tasks);
    }

    private void OnTuningTapped(object? sender, TappedEventArgs e)
    {
        if (Vm == null) return;
        _ = TuningSheet.ShowAsync(Vm.Tuning.Id, Vm.AltTuningsLocked, picked => Vm.TrySelectTuning(picked));
    }

    private void OnA4Tapped(object? sender, TappedEventArgs e)
    {
        if (Vm == null) return;
        if (Vm.A4Locked)
            _ = PaywallSheet.ShowAsync(Feature.A4Reference);
        else
            _ = A4Sheet.ShowAsync();
    }
}
