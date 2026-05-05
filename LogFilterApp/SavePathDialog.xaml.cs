using System.Windows;

namespace LogFilterApp;

public partial class SavePathDialog : Window
{
    public string DirectoryPath { get; }
    public string Alias => TxtAlias.Text.Trim();

    public SavePathDialog(string directoryPath)
    {
        InitializeComponent();
        DirectoryPath = directoryPath;
        TxtPath.Text = directoryPath;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
