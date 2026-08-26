using Strunika.Mobile.Pages;
using Strunika.Mobile.Pro;
using Strunika.Mobile.Services;
using Strunika.Mobile.ViewModels;

namespace Strunika.Mobile.Views;

public partial class TunerView : ContentView
{
    public TunerView()
    {
        InitializeComponent();
        BindingContextChanged += (_, _) =>
        {
            if (BindingContext is TunerViewModel vm)
            {
                vm.ProRequired += (_, feature) => _ = PaywallSheet.ShowAsync(feature);
                vm.StringTuned += (_, index) => _ = BouncePegAsync(index);
                vm.AllTunedReached += (_, _) => _ = CelebrateAsync(vm);
                vm.PropertyChanged += OnVmPropertyChanged;
            }
        };
    }

    private TunerViewModel? Vm => BindingContext as TunerViewModel;

    /// <summary>A string just got tuned: its peg swells and springs back.</summary>
    private async Task BouncePegAsync(int index)
    {
        if (Motion.Reduced) return;
        var pegs = PegRow.Children.OfType<View>().ToList();
        if (index < 0 || index >= pegs.Count) return;
        var peg = pegs[index];
        await peg.ScaleTo(1.35, 160, Easing.CubicOut);
        await peg.ScaleTo(1.0, 420, Easing.SpringOut);
    }

    /// <summary>Last string tuned: pegs bounce in a wave and the string flashes;
    /// listening continues until the user stops.</summary>
    private async Task CelebrateAsync(TunerViewModel vm)
    {
        Haptics.Default.Success();
        StringIndicator.Celebrate();
        if (!Motion.Reduced)
        {
            var pegs = PegRow.Children.OfType<View>().ToList();
            var tasks = new List<Task>();
            for (int i = 0; i < pegs.Count; i++)
            {
                var peg = pegs[i];
                int delay = i * 70;
                tasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(delay);
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await peg.ScaleTo(1.28, 130, Easing.CubicOut);
                        await peg.ScaleTo(1.0, 320, Easing.SpringOut);
                    });
                }));
            }
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>When the user stops with everything tuned, the "all set" line pops in.</summary>
    private async void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TunerViewModel.Listening) || Motion.Reduced) return;
        if (sender is TunerViewModel { Listening: false, AllTuned: true })
        {
            IdleLabel.Scale = 0.85;
            IdleLabel.Opacity = 0;
            await Task.WhenAll(IdleLabel.FadeTo(1, 220), IdleLabel.ScaleTo(1, 420, Easing.SpringOut));
        }
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
