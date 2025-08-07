using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Media;
using System.Reflection;

namespace SCLOCUA.Forms
{
    public partial class KillFeedForm : Form
    {
        private CancellationTokenSource _logCts;
        private Task _logTask;

        private bool showOldEntries = false;
        private bool showNPCs = true;
        private bool fullNPCNames = false;
        private string logFilePath = "";
        private int soundMode = 0;
        private string wavFilePath = null;

        public KillFeedForm()
        {
            InitializeComponent();
        }

        private void KillFeedForm_Load(object sender, EventArgs e)
        {
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Game.log");

            // Перевірка наявності файлу перед початком моніторингу
            if (!File.Exists(logFilePath))
            {
                MessageBox.Show("Лог-файл не знайдено.", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Помилка: Лог-файл не знайдено", Color.Red);
                return;
            }

            StartLogMonitoring();
        }

        private void KillFeedForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopLogMonitoring();
        }

        private void StartLogMonitoring()
        {
            _logCts = new CancellationTokenSource();
            _logTask = Task.Run(() => MonitorLogFile(logFilePath, _logCts.Token));
            UpdateStatus("Моніторинг логу активний", Color.LimeGreen);
        }

        private void StopLogMonitoring()
        {
            _logCts?.Cancel();
            _logTask = null;
            UpdateStatus("Моніторинг зупинено", Color.Orange);
        }

        private async Task MonitorLogFile(string path, CancellationToken token)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs))
                {
                    // Пропускаємо всі старі записи, якщо не потрібно показувати старі логи
                    if (!showOldEntries)
                        fs.Seek(0, SeekOrigin.End); // Позиціонуємо на кінець файлу

                    while (!token.IsCancellationRequested)
                    {
                        string line = await reader.ReadLineAsync();
                        if (line != null)
                        {
                            string result = ParseLogLine(line);
                            if (result != null)
                            {
                                AppendToFeed(result);
                                if (soundMode > 0) PlaySound();
                            }
                        }
                        else
                        {
                            await Task.Delay(100, token); // Якщо нових рядків немає, чекаємо
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    MessageBox.Show($"Помилка читання логів: {ex.Message}", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string ParseLogLine(string line)
        {
            var tsMatch = Regex.Match(line, @"<(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})");
            if (!tsMatch.Success) return null;

            string timestamp = DateTime.Parse(tsMatch.Groups[1].Value).ToString("HH:mm");

            var match = Regex.Match(line, @"'(.*?)'.*?killed by '(.*?)'");
            if (!match.Success) return null;

            string victim = match.Groups[1].Value;
            string killer = match.Groups[2].Value;

            bool isVictimNPC = Regex.IsMatch(victim, @"\d{13}$");
            bool isKillerNPC = Regex.IsMatch(killer, @"\d{13}$");

            // Виключаємо самогубства
            if (killer == victim) return null;

            // Виключаємо кілфіди, де вбивця невідомий
            if (killer == "unknown") return null;

            // Якщо показуємо лише гравців (не NPC)
            if (!showNPCs && (isVictimNPC || isKillerNPC)) return null;

            // Форматування імен NPC
            if (!fullNPCNames)
            {
                victim = Regex.Replace(victim, @"_\d{13}$", "");
                killer = Regex.Replace(killer, @"_\d{13}$", "");
            }

            // Формування рядка для виведення
            return $"[{timestamp}] {killer} вбив {victim}";
        }

        private void AppendToFeed(string message)
        {
            if (richTextBox1.InvokeRequired)
            {
                richTextBox1.Invoke(new Action(() =>
                {
                    richTextBox1.Text = message + Environment.NewLine + richTextBox1.Text;
                    richTextBox1.SelectionStart = 0;
                    richTextBox1.ScrollToCaret();
                }));
            }
            else
            {
                richTextBox1.Text = message + Environment.NewLine + richTextBox1.Text;
                richTextBox1.SelectionStart = 0;
                richTextBox1.ScrollToCaret();
            }
        }

        private void PlaySound()
        {
            if (!string.IsNullOrEmpty(wavFilePath) && File.Exists(wavFilePath))
            {
                using (var player = new SoundPlayer(wavFilePath))
                {
                    player.Play();
                }
            }
            else
            {
                var soundStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SCLOCUA.Forms.default.wav");
                if (soundStream != null)
                {
                    using (var player = new SoundPlayer(soundStream))
                    {
                        player.Play();
                    }
                }
            }
        }

        public void DisplayLog(string logContent)
        {
            if (richTextBox1 != null)
            {
                richTextBox1.Text = logContent;
            }
        }

        private void UpdateStatus(string text, Color color)
        {
            if (label_status.InvokeRequired)
            {
                label_status.Invoke(new Action(() =>
                {
                    label_status.Text = $"Status: {text}";
                    label_status.ForeColor = color;
                }));
            }
            else
            {
                label_status.Text = $"Status: {text}";
                label_status.ForeColor = color;
            }
        }
    }
}
