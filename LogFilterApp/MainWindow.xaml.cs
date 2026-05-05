using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LogFilterApp;

public partial class MainWindow : Window
{
    private static readonly TimeSpan AutoDetectDebounce = TimeSpan.FromMilliseconds(250);

    private readonly PathHistoryManager _pathHistory = new();
    private readonly DispatcherTimer _autoDetectTimer;
    private readonly List<string> _loadedFiles = new();
    private readonly LogFilterCoordinator _coordinator = new();

    private TimestampPreset? _detectedPreset;
    private bool _userOverrodePreset;

    private CancellationTokenSource? _filterCts;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = version != null
            ? $"Log Filter  v{version.Major}.{version.Minor}.{version.Build}"
            : "Log Filter";

        _autoDetectTimer = new DispatcherTimer { Interval = AutoDetectDebounce };
        _autoDetectTimer.Tick += AutoDetectTimer_Tick;

        CmbTimestampPresets.ItemsSource = LogParser.KnownPresets;
        RefreshPathHistoryCombo();
        UpdateFilterButtonState();
        UpdateReloadButtonState();
    }

    private void TxtInputLog_TextChanged(object sender, TextChangedEventArgs e)
    {
        InvalidateParsedCache();
        _autoDetectTimer.Stop();
        _autoDetectTimer.Start();
    }

    private void AutoDetectTimer_Tick(object? sender, EventArgs e)
    {
        _autoDetectTimer.Stop();
        AutoDetectTimestamp();
    }

    private void AutoDetectTimestamp()
    {
        var input = TxtInputLog.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            _detectedPreset = null;
            TxtDetectedStatus.Text = "";
            UpdateFilterButtonState();
            return;
        }

        var detected = LogParser.DetectPreset(input);
        _detectedPreset = detected;

        if (detected != null && !_userOverrodePreset)
        {
            CmbTimestampPresets.SelectionChanged -= CmbTimestampPresets_SelectionChanged;
            CmbTimestampPresets.SelectedItem = detected;
            CmbTimestampPresets.SelectionChanged += CmbTimestampPresets_SelectionChanged;

            TxtDetectedStatus.Text = "(auto-detectado)";
            TxtDetectedStatus.Foreground = System.Windows.Media.Brushes.Green;
        }
        else if (detected == null)
        {
            TxtDetectedStatus.Text = "(padrao nao reconhecido)";
            TxtDetectedStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }

        UpdateFilterButtonState();
    }

    private void UpdateFilterButtonState()
    {
        if (BtnFilter == null) return;

        var input = TxtInputLog?.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            BtnFilter.IsEnabled = false;
            return;
        }

        var selectedPreset = CmbTimestampPresets.SelectedItem as TimestampPreset;
        if (selectedPreset == null)
        {
            BtnFilter.IsEnabled = false;
            return;
        }

        bool isValid = LogParser.ValidatePresetAgainstInput(input, selectedPreset);
        BtnFilter.IsEnabled = isValid;

        if (_userOverrodePreset && !isValid)
        {
            TxtDetectedStatus.Text = "(padrao nao coincide com o input)";
            TxtDetectedStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
        }
        else if (_userOverrodePreset && isValid)
        {
            TxtDetectedStatus.Text = "(manual)";
            TxtDetectedStatus.Foreground = System.Windows.Media.Brushes.Gray;
        }
    }

    private void UpdateReloadButtonState()
    {
        if (BtnReloadFiles == null) return;
        BtnReloadFiles.IsEnabled = _loadedFiles.Count > 0;
    }

    private void InvalidateParsedCache() => _coordinator.Invalidate();

    private void CmbTimestampPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _userOverrodePreset = true;
        InvalidateParsedCache();
        UpdateFilterButtonState();
    }

    private async void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        var preset = CmbTimestampPresets.SelectedItem as TimestampPreset;
        if (preset == null)
        {
            MessageBox.Show("Selecione um padrao de timestamp.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string filterPattern = TxtPattern.Text.Trim();
        string input = TxtInputLog.Text;
        DateTime? startDate = ParseDateTimeFromPickers(DpStartDate, TxtStartTime);
        DateTime? endDate = ParseDateTimeFromPickers(DpEndDate, TxtEndTime);

        // Cancela filtragem anterior, se ainda em curso
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var ct = _filterCts.Token;

        BeginBusyUi();

        try
        {
            var result = await _coordinator.FilterAsync(input, preset, filterPattern, startDate, endDate, ct);

            ct.ThrowIfCancellationRequested();

            RenderFilterResult(result);

            if (result.TotalFiltered == 0)
            {
                MessageBox.Show("Nenhum registro encontrado.", "Log Parser", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            // filtragem cancelada — silencioso
        }
        catch (FormatException ex)
        {
            MessageBox.Show($"Erro ao processar log: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndBusyUi();
        }
    }

    private void RenderFilterResult(FilterResult result)
    {
        LstOutput.ItemsSource = result.Entries;
        GrpOutput.Header = $"Output  ({result.TotalFiltered} de {result.TotalParsed} registros)";
    }

    private void BeginBusyUi()
    {
        _isBusy = true;
        PbFiltering.Visibility = Visibility.Visible;
        Mouse.OverrideCursor = Cursors.Wait;
        BtnFilter.IsEnabled = false;
        BtnLoadFile.IsEnabled = false;
        BtnReloadFiles.IsEnabled = false;
        BtnRemovePath.IsEnabled = false;
        BtnClearDates.IsEnabled = false;
    }

    private void EndBusyUi()
    {
        _isBusy = false;
        PbFiltering.Visibility = Visibility.Collapsed;
        Mouse.OverrideCursor = null;
        BtnLoadFile.IsEnabled = true;
        BtnRemovePath.IsEnabled = true;
        BtnClearDates.IsEnabled = true;
        UpdateFilterButtonState();
        UpdateReloadButtonState();
    }

    private static DateTime? ParseDateTimeFromPickers(DatePicker datePicker, TextBox timeBox)
    {
        if (datePicker.SelectedDate is not DateTime date)
            return null;

        if (TryParseTime(timeBox.Text, out var time))
        {
            return date.Date + time;
        }

        return date.Date;
    }

    private static bool TryParseTime(string raw, out TimeSpan time) =>
        TimeSpan.TryParseExact(raw.Trim(), ["hh\\:mm\\:ss", "h\\:mm\\:ss"],
            CultureInfo.InvariantCulture, out time);

    private void TxtTime_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;

        var text = box.Text.Trim();
        // Vazio é tolerado (fica 00:00:00 / 23:59:59 implícito ao parsear data-só).
        bool valid = string.IsNullOrEmpty(text) || TryParseTime(text, out _);

        box.BorderBrush = valid
            ? System.Windows.SystemColors.ControlDarkBrush
            : System.Windows.Media.Brushes.OrangeRed;
        box.ToolTip = valid ? "HH:mm:ss" : "Formato invalido. Use HH:mm:ss (ex: 14:30:00).";
    }

    // ───── Pesquisa incremental (input + output) ─────

    private void TxtFindInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInInput(forward: !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TxtInputLog.Focus();
            e.Handled = true;
        }
    }

    private void BtnFindNextInput_Click(object sender, RoutedEventArgs e) => FindInInput(forward: true);
    private void BtnFindPrevInput_Click(object sender, RoutedEventArgs e) => FindInInput(forward: false);

    private void FindInInput(bool forward)
    {
        var term = TxtFindInput.Text;
        if (string.IsNullOrEmpty(term)) return;

        var text = TxtInputLog.Text;
        if (text.Length == 0) return;

        int idx;
        if (forward)
        {
            int from = TxtInputLog.SelectionStart + TxtInputLog.SelectionLength;
            idx = text.IndexOf(term, Math.Min(from, text.Length), StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                idx = text.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase); // wrap
        }
        else
        {
            int from = Math.Max(0, TxtInputLog.SelectionStart - 1);
            idx = LastIndexOfUpTo(text, term, from);
            if (idx < 0)
                idx = LastIndexOfUpTo(text, term, text.Length - 1); // wrap
        }

        if (idx >= 0)
        {
            TxtInputLog.Focus();
            TxtInputLog.Select(idx, term.Length);
            var line = TxtInputLog.GetLineIndexFromCharacterIndex(idx);
            if (line >= 0) TxtInputLog.ScrollToLine(line);
        }
        else
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    private static int LastIndexOfUpTo(string text, string term, int upToInclusive)
    {
        if (upToInclusive < 0 || text.Length == 0) return -1;
        return text.LastIndexOf(term, upToInclusive, upToInclusive + 1, StringComparison.OrdinalIgnoreCase);
    }

    private void TxtFindOutput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInOutput(forward: !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            LstOutput.Focus();
            e.Handled = true;
        }
    }

    private void BtnFindNextOutput_Click(object sender, RoutedEventArgs e) => FindInOutput(forward: true);
    private void BtnFindPrevOutput_Click(object sender, RoutedEventArgs e) => FindInOutput(forward: false);

    private void FindInOutput(bool forward)
    {
        var term = TxtFindOutput.Text;
        if (string.IsNullOrEmpty(term)) return;

        int n = LstOutput.Items.Count;
        if (n == 0) return;

        int start = LstOutput.SelectedIndex; // -1 quando nada selecionado
        int step = forward ? 1 : -1;

        for (int i = 1; i <= n; i++)
        {
            int idx = ((start + step * i) % n + n) % n;
            if (LstOutput.Items[idx] is LogEntry entry &&
                entry.FullText.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                LstOutput.SelectedIndex = idx;
                LstOutput.ScrollIntoView(LstOutput.Items[idx]);
                return;
            }
        }

        System.Media.SystemSounds.Beep.Play();
    }

    private void CopyOutputSelected_Executed(object sender, ExecutedRoutedEventArgs e) => CopyOutputSelectedToClipboard();
    private void MenuCopyOutputSelected_Click(object sender, RoutedEventArgs e) => CopyOutputSelectedToClipboard();
    private void MenuCopyOutputAll_Click(object sender, RoutedEventArgs e) => CopyOutputAllToClipboard();
    private void MenuSelectAllOutput_Click(object sender, RoutedEventArgs e) => LstOutput.SelectAll();

    private void CopyOutputSelectedToClipboard()
    {
        var lines = LstOutput.SelectedItems.OfType<LogEntry>().Select(x => x.FullText);
        var text = string.Join(Environment.NewLine, lines);
        if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
    }

    private void CopyOutputAllToClipboard()
    {
        var lines = LstOutput.Items.OfType<LogEntry>().Select(x => x.FullText);
        var text = string.Join(Environment.NewLine, lines);
        if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
    }

    private void BtnOutputToInput_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var text = string.Join(
            Environment.NewLine,
            LstOutput.Items.OfType<LogEntry>().Select(x => x.FullText));

        TxtInputLog.Text = text;
        LstOutput.ItemsSource = null;
        GrpOutput.Header = "Output";
    }

    private void BtnClearDates_Click(object sender, RoutedEventArgs e)
    {
        DpStartDate.SelectedDate = null;
        DpEndDate.SelectedDate = null;
        TxtStartTime.Text = "00:00:00";
        TxtEndTime.Text = "23:59:59";
    }

    private async void BtnLoadFile_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Arquivos de Log (*.log;*.txt)|*.log;*.txt|Todos os arquivos (*.*)|*.*",
            Multiselect = true
        };

        if (CmbPathHistory.SelectedItem is PathHistoryEntry selectedEntry &&
            Directory.Exists(selectedEntry.Path))
        {
            dialog.InitialDirectory = selectedEntry.Path;
        }

        if (dialog.ShowDialog() != true)
            return;

        BeginBusyUi();
        try
        {
            await AppendFilesToInputAsync(dialog.FileNames);
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Erro ao ler arquivo: {ex.Message}", "Erro de leitura", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show($"Sem permissao para ler o arquivo: {ex.Message}", "Acesso negado", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            EndBusyUi();
        }

        var selectedDir = Path.GetDirectoryName(dialog.FileNames[0]);
        if (string.IsNullOrEmpty(selectedDir))
            return;

        if (_pathHistory.Contains(selectedDir))
        {
            if (!_pathHistory.UpdateLastUsed(selectedDir))
                ShowHistorySaveFailure();
            return;
        }

        var saveDialog = new SavePathDialog(selectedDir) { Owner = this };
        if (saveDialog.ShowDialog() == true)
        {
            if (!_pathHistory.AddOrUpdate(selectedDir, saveDialog.Alias))
            {
                ShowHistorySaveFailure();
                return;
            }
            RefreshPathHistoryCombo();

            var newEntry = _pathHistory.Entries.FirstOrDefault(e2 =>
                string.Equals(e2.Path, selectedDir, StringComparison.OrdinalIgnoreCase));
            if (newEntry != null)
                CmbPathHistory.SelectedItem = newEntry;
        }
    }

    private void ShowHistorySaveFailure()
    {
        var detail = _pathHistory.LastSaveError;
        var msg = "Nao foi possivel salvar o historico de diretorios."
                  + (string.IsNullOrEmpty(detail) ? "" : $"\n\nDetalhes: {detail}");
        MessageBox.Show(this, msg, "Historico", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void BtnReloadFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        if (_loadedFiles.Count == 0) return;

        var files = _loadedFiles.ToList();
        var missing = files.Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0)
        {
            var msg = "Os seguintes arquivos nao foram encontrados e serao removidos da lista:\n\n"
                      + string.Join("\n", missing);
            MessageBox.Show(msg, "Atualizar arquivos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        TxtInputLog.Clear();
        _loadedFiles.Clear();

        BeginBusyUi();
        try
        {
            await AppendFilesToInputAsync(files);
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Erro ao ler arquivo: {ex.Message}", "Erro de leitura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show($"Sem permissao para ler o arquivo: {ex.Message}", "Acesso negado", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndBusyUi();
        }
    }

    private void BtnRemovePath_Click(object sender, RoutedEventArgs e)
    {
        if (CmbPathHistory.SelectedItem is not PathHistoryEntry entry)
        {
            MessageBox.Show("Selecione um diretorio no combo para remover.",
                "Historico", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Remover \"{entry.DisplayText}\" do historico?",
            "Confirmar remocao",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            if (!_pathHistory.Remove(entry))
                ShowHistorySaveFailure();
            RefreshPathHistoryCombo();
        }
    }

    private void RefreshPathHistoryCombo()
    {
        var previousSelection = CmbPathHistory.SelectedItem as PathHistoryEntry;
        CmbPathHistory.ItemsSource = null;
        CmbPathHistory.ItemsSource = _pathHistory.Entries.OrderByDescending(e => e.LastUsed).ToList();
        CmbPathHistory.DisplayMemberPath = "DisplayText";

        if (previousSelection != null)
        {
            CmbPathHistory.SelectedItem = _pathHistory.Entries.FirstOrDefault(e =>
                string.Equals(e.Path, previousSelection.Path, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void TxtInputLog_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = (!_isBusy && e.Data.GetDataPresent(DataFormats.FileDrop))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void TxtInputLog_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (_isBusy) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] droppedFiles || droppedFiles.Length == 0)
            return;

        BeginBusyUi();
        try
        {
            await AppendFilesToInputAsync(droppedFiles);
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Erro ao ler arquivo: {ex.Message}", "Erro de leitura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show($"Sem permissao para ler o arquivo: {ex.Message}", "Acesso negado", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndBusyUi();
        }
    }

    private async Task AppendFilesToInputAsync(IEnumerable<string> fileNames)
    {
        var files = fileNames.Where(File.Exists).ToList();
        if (files.Count == 0) return;

        if (!await ConfirmFormatCompatibilityAsync(files))
            return;

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(TxtInputLog.Text))
        {
            sb.AppendLine(TxtInputLog.Text);
        }

        foreach (var fileName in files)
        {
            var content = await File.ReadAllTextAsync(fileName);
            sb.AppendLine(content);
            sb.AppendLine();

            if (!_loadedFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                _loadedFiles.Add(fileName);
        }

        TxtInputLog.Text = sb.ToString();
        UpdateReloadButtonState();
    }

    private async Task<bool> ConfirmFormatCompatibilityAsync(IReadOnlyList<string> files)
    {
        // Coleta o preset de cada fonte (input atual + cada arquivo).
        // Se houver mais de um preset reconhecido distinto, avisa o usuário antes de misturar.
        var presets = new HashSet<TimestampPreset>();

        if (!string.IsNullOrWhiteSpace(TxtInputLog.Text))
        {
            var current = LogParser.DetectPreset(TxtInputLog.Text);
            if (current != null) presets.Add(current);
        }

        foreach (var file in files)
        {
            var detected = await Task.Run(() =>
            {
                try
                {
                    using var reader = new StreamReader(file);
                    return LogParser.DetectPreset(reader);
                }
                catch
                {
                    return null;
                }
            });

            if (detected != null) presets.Add(detected);
        }

        if (presets.Count <= 1)
            return true;

        var msg = "Os arquivos/texto selecionados parecem ter formatos de timestamp diferentes:\n\n  - "
                  + string.Join("\n  - ", presets.Select(p => p.Name))
                  + "\n\nMisturar formatos faz com que apenas um preset seja usado e os demais virem linhas \"sem timestamp\". Continuar mesmo assim?";

        var result = MessageBox.Show(this, msg, "Formatos de timestamp diferentes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
}
