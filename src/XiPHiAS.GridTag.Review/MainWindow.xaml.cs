using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using XiPHiAS.GridTag.Vision;

namespace XiPHiAS.GridTag.Review;

public partial class MainWindow : Window
{
    private IReadOnlyList<ReviewPhoto> photos = [];
    private CancellationTokenSource? loadCancellation;
    private CancellationTokenSource? previewCancellation;
    private string? resultsPath;
    private bool closed;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            closed = true;
            loadCancellation?.Cancel();
            previewCancellation?.Cancel();
        };
    }

    private async void OpenResultsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "GridTag-resultaten (*.json)|*.json", Title = "Open results.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        var manifest = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, "manifest.json");
        await LoadAsync(dialog.FileName, File.Exists(manifest) ? manifest : null);
    }

    private async void OpenManifestClick(object sender, RoutedEventArgs e)
    {
        if (resultsPath is null)
            return;
        var dialog = new OpenFileDialog { Filter = "GridTag-manifest (*.json)|*.json", Title = "Kies het bijbehorende manifest.json" };
        if (dialog.ShowDialog(this) == true)
            await LoadAsync(resultsPath, dialog.FileName);
    }

    private async Task LoadAsync(string path, string? manifest)
    {
        loadCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        loadCancellation = cancellation;
        SummaryText.Text = "Resultaten laden…";
        try
        {
            var loaded = await Task.Run(() => ReviewSession.LoadAsync(path, manifest, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            photos = loaded;
            resultsPath = path;
            ManifestButton.IsEnabled = true;
            SourceText.Text = $"Resultaten: {path}\nManifest: {manifest ?? "niet geladen — kies een manifest voor fotopaden en previews"}";
            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested && !closed)
            {
                SummaryText.Text = "Laden mislukt; de vorige fotolijst blijft beschikbaar.";
                MessageBox.Show(this, $"Kies geldige GridTag-resultaten en het bijbehorende manifest.\n\n{exception.Message}", "GridTag Review", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(loadCancellation, cancellation))
                loadCancellation = null;
        }
    }

    private void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PhotoList is not null)
            ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selectedId = (PhotoList.SelectedItem as ReviewPhoto)?.Id;
        var filtered = photos.Where(photo => StatusFilter.SelectedIndex switch
        {
            1 => photo.Status == "review",
            2 => photo.Status == "noCar",
            _ => true
        }).ToArray();
        PhotoList.ItemsSource = filtered;
        PhotoList.SelectedItem = filtered.FirstOrDefault(photo => photo.Id == selectedId) ?? filtered.FirstOrDefault();
        SummaryText.Text = $"{filtered.Length} zichtbaar · {photos.Count(photo => photo.Status == "review")} review · {photos.Count(photo => photo.Status == "noCar")} geen auto";
    }

    private async void PhotoSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        previewCancellation?.Cancel();
        PreviewImage.Source = null;
        StatusText.Text = ReasonText.Text = PathText.Text = CandidateText.Text = string.Empty;
        PreviewText.Text = "Selecteer een foto. Handmatige correcties voer je uit in Lightroom.";
        if (PhotoList.SelectedItem is not ReviewPhoto photo)
            return;
        StatusText.Text = $"{(photo.Status == "noCar" ? "Geen auto" : "Review")} · #{photo.Id} · {photo.Session}";
        ReasonText.Text = $"Redenen: {photo.Reasons}";
        CandidateText.Text = $"Kandidaten: {(photo.Candidates.Length == 0 ? "geen" : photo.Candidates)}";
        PathText.Text = photo.Path;
        if (string.IsNullOrEmpty(photo.Path))
        {
            PreviewText.Text = "Geen fotopad beschikbaar. Kies het bijbehorende manifest.";
            return;
        }
        using var cancellation = new CancellationTokenSource();
        previewCancellation = cancellation;
        PreviewText.Text = "Preview laden…";
        try
        {
            var image = await Task.Run(() => LoadPreview(photo.Path, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            PreviewImage.Source = image;
            PreviewText.Text = image is null ? "Geen preview beschikbaar. Controleer het fotopad en de geïnstalleerde RAW-codec." : string.Empty;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (!cancellation.IsCancellationRequested && !closed)
                PreviewText.Text = $"Preview niet beschikbaar: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(previewCancellation, cancellation))
                previewCancellation = null;
        }
    }

    private static BitmapImage? LoadPreview(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
            return null;
        using var stream = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? (Stream)File.OpenRead(path)
            : new EmbeddedJpegRawPreviewProvider().GetPreview(path) is RawPreview preview
                ? new MemoryStream(preview.JpegBytes, writable: false) : null;
        cancellationToken.ThrowIfCancellationRequested();
        if (stream is null)
            return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 2000;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void PreviousClick(object sender, RoutedEventArgs e) => MoveSelection(-1);
    private void NextClick(object sender, RoutedEventArgs e) => MoveSelection(1);

    private void MoveSelection(int delta)
    {
        if (PhotoList.Items.Count == 0)
            return;
        PhotoList.SelectedIndex = Math.Clamp(PhotoList.SelectedIndex + delta, 0, PhotoList.Items.Count - 1);
        PhotoList.ScrollIntoView(PhotoList.SelectedItem);
    }
}
