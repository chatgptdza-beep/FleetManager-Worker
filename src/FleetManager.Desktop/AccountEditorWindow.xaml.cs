using System.Windows;

namespace FleetManager.Desktop;

public partial class AccountEditorWindow : Window
{
    public AccountEditorWindow(string title, string nodeLabel, string? email = null, string? username = null, string? status = null)
    {
        EditorTitle = title;
        NodeLabel = nodeLabel;
        Email = email ?? string.Empty;
        Username = username ?? string.Empty;
        StatusOptions = new[] { "Stable", "Running", "Manual", "Paused", "Error" };
        Status = string.IsNullOrWhiteSpace(status) ? "Running" : status;

        InitializeComponent();
        DataContext = this;
    }

    public string EditorTitle { get; }
    public string NodeLabel { get; }
    public IReadOnlyList<string> StatusOptions { get; }
    public string Email { get; set; }
    public string Username { get; set; }
    public string Status { get; set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Email = Email.Trim();
        Username = Username.Trim();

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Username))
        {
            MessageBox.Show(this, "Email and username are required.", "FleetManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
