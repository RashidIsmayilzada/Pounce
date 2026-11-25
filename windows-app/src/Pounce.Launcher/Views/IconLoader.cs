using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pounce.Core;
using Pounce.Launcher.Platform;

namespace Pounce.Launcher.Views;

/// <summary>
/// Shows a row's icon on an <see cref="Image"/>: <c>views:IconLoader.Item="{Binding Item}"</c>. Takes the cached icon
/// at once when there is one; otherwise clears the image (the row's neutral placeholder shows) and loads the icon off
/// the UI thread, setting it only if the image still shows the same item by then (rows are reused between
/// searches). A missing icon or a failing provider leaves the placeholder; nothing is thrown.
/// </summary>
public static class IconLoader
{
    /// <summary>The item whose icon the image shows.</summary>
    public static readonly DependencyProperty ItemProperty = DependencyProperty.RegisterAttached(
        "Item", typeof(SearchItem), typeof(IconLoader), new PropertyMetadata(null, OnItemChanged));

    /// <summary>Where icons come from. Set once, before the first row is shown.</summary>
    public static IIconProvider? Provider { get; set; }

    /// <summary>Gets the item whose icon <paramref name="image"/> shows.</summary>
    public static SearchItem? GetItem(Image image) => (SearchItem?)image.GetValue(ItemProperty);

    /// <summary>Sets the item whose icon <paramref name="image"/> shows.</summary>
    public static void SetItem(Image image, SearchItem? value) => image.SetValue(ItemProperty, value);

    private static void OnItemChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Image image)
        {
            return;
        }
        // Records compare by value: a new but equal item (the same file in the Final Results) keeps its icon.
        if (Equals(e.OldValue, e.NewValue) && image.Source is not null)
        {
            return;
        }
        var item = e.NewValue as SearchItem;
        IIconProvider? provider = Provider;
        ImageSource? cached = null;
        if (item is not null && provider is not null)
        {
            try
            {
                cached = provider.Cached(item);
            }
            catch (Exception ex)
            {
                Log.Warn($"Icon cache failed for {item.Path}", ex);
            }
        }
        image.Source = cached;
        if (cached is null && item is not null && provider is not null)
        {
            LoadInto(image, item, provider);
        }
    }

    /// <summary>Async void on purpose: an event-style fire-and-forget that catches everything itself.</summary>
    private static async void LoadInto(Image image, SearchItem item, IIconProvider provider)
    {
        try
        {
            ImageSource? icon = await provider.LoadAsync(item);
            // The continuation is back on the UI thread (the provider's contract). The row may show another item now.
            if (icon is not null && Equals(GetItem(image), item))
            {
                image.Source = icon;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Icon failed for {item.Path}", ex);
        }
    }
}
