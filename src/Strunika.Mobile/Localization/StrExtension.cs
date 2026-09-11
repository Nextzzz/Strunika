namespace Strunika.Mobile.Localization;

/// <summary>
/// <c>Text="{loc:Str Tuner_Title}"</c> — a binding to <see cref="Loc"/>'s
/// indexer, so the text follows the selected language at runtime.
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class StrExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = "";

    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]", BindingMode.OneWay, source: Loc.Instance);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}
