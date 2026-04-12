using Microsoft.Win32;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FleetManager.Desktop;

public partial class InjectProxiesWindow : Window, INotifyPropertyChanged
{
    private readonly int _currentProxyCount;
    private string _selectedFileLabel = "No file selected";
    private bool _replaceMode;
    private int _validLineCount;
    private int _invalidLineCount;
    private int _duplicateLineCount;

    public InjectProxiesWindow(string accountEmail, int currentProxyCount, string currentProxyLabel)
    {
        _currentProxyCount = currentProxyCount;
        AccountLabel = $"Target account: {accountEmail}";
        CurrentProxyLabel = $"Current proxy pool: {currentProxyLabel}";

        InitializeComponent();
        DataContext = this;
        RecalculatePreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AccountLabel { get; }
    public string CurrentProxyLabel { get; }
    public string RawProxies { get; private set; } = string.Empty;

    public string SelectedFileLabel
    {
        get => _selectedFileLabel;
        private set => SetProperty(ref _selectedFileLabel, value);
    }

    public bool ReplaceMode
    {
        get => _replaceMode;
        set
        {
            if (SetProperty(ref _replaceMode, value))
            {
                OnPropertyChanged(nameof(AppendMode));
                OnPropertyChanged(nameof(ModeSummary));
            }
        }
    }

    public bool AppendMode
    {
        get => !ReplaceMode;
        set
        {
            if (value)
            {
                ReplaceMode = false;
            }
        }
    }

    public int ValidLineCount
    {
        get => _validLineCount;
        private set => SetProperty(ref _validLineCount, value);
    }

    public int InvalidLineCount
    {
        get => _invalidLineCount;
        private set => SetProperty(ref _invalidLineCount, value);
    }

    public int DuplicateLineCount
    {
        get => _duplicateLineCount;
        private set => SetProperty(ref _duplicateLineCount, value);
    }

    public string PreviewSummary => $"{ValidLineCount} valid | {InvalidLineCount} invalid | {DuplicateLineCount} duplicates";

    public string ModeSummary => ReplaceMode
        ? $"Replace mode will clear {_currentProxyCount} existing proxies before injecting the valid lines below."
        : $"Append mode will keep the current pool and add the valid lines below.";

    private void LoadTxt_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Select proxy list"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadProxyFile(dialog.FileName);
    }

    private void InjectMode_Checked(object sender, RoutedEventArgs e)
    {
        OnPropertyChanged(nameof(ModeSummary));
    }

    private void RawProxiesTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SelectedFileLabel = string.IsNullOrWhiteSpace(SelectedFileLabel) ? "Manual paste" : SelectedFileLabel;
        RecalculatePreview();
    }

    private void RawProxiesTextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void RawProxiesTextBox_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        LoadProxyFile(files[0]);
    }

    private void Inject_Click(object sender, RoutedEventArgs e)
    {
        RawProxies = RawProxiesTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(RawProxies))
        {
            MessageBox.Show(this, "Load a TXT file or paste at least one proxy line before injecting.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ValidLineCount == 0)
        {
            MessageBox.Show(this, "No valid proxy lines were found. Use only ip:port or ip:port:user:password.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void LoadProxyFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            MessageBox.Show(this, "Selected proxy file no longer exists.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RawProxiesTextBox.Text = File.ReadAllText(filePath);
        SelectedFileLabel = Path.GetFileName(filePath);
        RecalculatePreview();
    }

    private void RecalculatePreview()
    {
        var rawText = RawProxiesTextBox.Text;
        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var validCount = 0;
        var invalidCount = 0;
        var duplicateCount = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (!TryNormalizeProxyLine(line, out var normalized))
            {
                invalidCount++;
                continue;
            }

            if (!seen.Add(normalized))
            {
                duplicateCount++;
                continue;
            }

            validCount++;
        }

        ValidLineCount = validCount;
        InvalidLineCount = invalidCount;
        DuplicateLineCount = duplicateCount;
        OnPropertyChanged(nameof(PreviewSummary));
    }

    private static bool TryNormalizeProxyLine(string line, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if ((parts.Length != 2 && parts.Length != 4) || string.IsNullOrWhiteSpace(parts[0]))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var port) || port <= 0)
        {
            return false;
        }

        var username = parts.Length == 4 ? parts[2] : string.Empty;
        var password = parts.Length == 4 ? parts[3] : string.Empty;
        normalized = $"{parts[0]}:{port}:{username}:{password}";
        return true;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
