using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GridTag.Review;

public partial class MainWindow : Window
{
    private readonly List<ReviewPhoto> photos = [];

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenResultsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "GridTag results|results.json|JSON files|*.json" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            LoadResults(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "GridTag Review", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadResults(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported results schemaVersion.");

        var manifestPaths = LoadManifestPaths(Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "manifest.json"));

        photos.Clear();
        foreach (var item in root.GetProperty("photos").EnumerateArray())
        {
            var status = item.GetProperty("status").GetString() ?? string.Empty;
            if (status is not ("review" or "noCar"))
                continue;
            var reasons = item.TryGetProperty("reasons", out var reasonElement)
                ? string.Join(", ", reasonElement.EnumerateArray().Select(reason => reason.GetString()))
                : string.Empty;
            var id = item.GetProperty("id").GetInt32();
            photos.Add(new ReviewPhoto(id, status, reasons, manifestPaths.GetValueOrDefault(id, string.Empty)));
        }

        PhotoList.ItemsSource = photos;
        if (photos.Count > 0)
            PhotoList.SelectedIndex = 0;
    }

    private static IReadOnlyDictionary<int, string> LoadManifestPaths(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<int, string>();
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("photos").EnumerateArray().ToDictionary(
            photo => photo.GetProperty("id").GetInt32(),
            photo => photo.GetProperty("path").GetString() ?? string.Empty);
    }

    private void PhotoSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PhotoList.SelectedItem is not ReviewPhoto photo)
            return;
        StatusText.Text = $"{photo.Status}  #{photo.Id}";
        ReasonText.Text = photo.Reasons;
        PathText.Text = photo.Path;
        PreviewImage.Source = TryLoadImage(photo.Path);
    }

    private static BitmapImage? TryLoadImage(string path)
    {
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private sealed record ReviewPhoto(int Id, string Status, string Reasons, string Path)
    {
        public string Display => $"{Status}  #{Id}";
    }
}
