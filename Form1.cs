using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace SCLOCUA
{
    public partial class Form1 : Form
    {
        private int autoUpdateInterval = 1;
        private static readonly HttpClient httpClient = new HttpClient();
        private Timer autoUpdateTimer = new Timer();
        private string selectedFolderPath = "";

        public Form1()
        {
            InitializeComponent();
            InitializeEvents();
            InitializeTimer();
            InitializeUI();

            selectedFolderPath = Properties.Settings.Default.LastSelectedFolderPath;
            UpdateLabel();
        }

        private void InitializeEvents()
        {
            button1.Click += button1_Click;
            checkBox2.CheckedChanged += checkBox2_CheckedChanged;
            comboBox1.SelectedIndexChanged += comboBox1_SelectedIndexChanged;
        }

        private void InitializeTimer()
        {
            autoUpdateTimer.Interval = autoUpdateInterval * 60 * 1000;
            autoUpdateTimer.Tick += autoUpdateTimer_Tick;
            autoUpdateTimer.Start();
        }

        private void InitializeUI()
        {
            this.MaximizeBox = false;
            this.Icon = Properties.Resources.Icon;
            UpdateLabel();

            if (!string.IsNullOrWhiteSpace(selectedFolderPath) && Directory.Exists(selectedFolderPath))
            {
                toolStripStatusLabel1.Text = "Перейдіть до встановлення локалізації";
                button2.Enabled = true;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.SelectedPath = selectedFolderPath;
                DialogResult result = folderDialog.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
                {
                    selectedFolderPath = folderDialog.SelectedPath;
                    UpdateLabel();
                    toolStripStatusLabel1.Text = "Перейдіть до встановлення локалізації";
                    button2.Enabled = true;

                    Properties.Settings.Default.LastSelectedFolderPath = selectedFolderPath;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                toolStripProgressBar1.Maximum = checkBox1.Checked ? 2 : 1;
                toolStripProgressBar1.Value = 0;
                button2.Enabled = false;

                try
                {
                    bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, "user.cfg"));
                    bool globalIniExists = File.Exists(Path.Combine(selectedFolderPath, "Data/Localization/korean_(south_korea)/global.ini"));

                    if (checkBox1.Checked)
                    {
                        CopyFile("user.cfg", Path.Combine(selectedFolderPath, "user.cfg"));
                        toolStripProgressBar1.Value++;
                    }

                    string githubGistUrl = "https://raw.githubusercontent.com/Vova-Bob/SC_localization_UA/main/Data/Localization/korean_(south_korea)/global.ini";
                    string localFilePath = Path.Combine(selectedFolderPath, "Data/Localization/korean_(south_korea)/global.ini");

                    await DownloadFileAsync(githubGistUrl, localFilePath);
                    toolStripProgressBar1.Value++;

                    string gistContent = File.ReadAllText(localFilePath);

                    if (DetectGithubGistUrl(gistContent))
                    {
                        toolStripStatusLabel1.Text = "Знайдено URL до Github gist";
                    }
                    else
                    {
                        toolStripStatusLabel1.Text = userCfgExists || globalIniExists ? "Локалізацію оновлено" : "Локалізацію встановлено";
                    }

                    autoUpdateTimer.Start();
                }
                catch (Exception ex)
                {
                    ShowErrorMessage($"Помилка при завантаженні/оновленні файлів: {ex.Message}");
                }
                finally
                {
                    button2.Enabled = true;
                }
            }
        }

        private bool DetectGithubGistUrl(string content)
        {
            string regexPattern = @"https://gist.github.com/\w+/\w+";
            Match match = Regex.Match(content, regexPattern);
            return match.Success;
        }

        private void CopyFile(string sourcePath, string destinationPath)
        {
            File.Copy(sourcePath, destinationPath, true);
        }

        private async Task DownloadFileAsync(string url, string filePath)
        {
            try
            {
                byte[] fileData = await httpClient.GetByteArrayAsync(url);
                string directoryPath = Path.GetDirectoryName(filePath);

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                await Task.Run(() => File.WriteAllBytes(filePath, fileData));
            }
            catch (HttpRequestException ex)
            {
                ShowErrorMessage($"Помилка мережевого запиту: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"Помилка при завантаженні файлу: {ex.Message}");
            }
        }

        private void UpdateLabel()
        {
            label1.Text = selectedFolderPath;
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                try
                {
                    bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, "user.cfg"));
                    bool globalIniExists = File.Exists(Path.Combine(selectedFolderPath, "Data/Localization/korean_(south_korea)/global.ini"));

                    if (userCfgExists || globalIniExists)
                    {
                        if (userCfgExists)
                        {
                            File.Delete(Path.Combine(selectedFolderPath, "user.cfg"));
                        }
                        if (globalIniExists)
                        {
                            File.Delete(Path.Combine(selectedFolderPath, "Data/Localization/korean_(south_korea)/global.ini"));
                        }

                        int initialValue = toolStripProgressBar1.Value;

                        for (int i = initialValue; i >= 0; i--)
                        {
                            toolStripProgressBar1.Value = i;
                            await Task.Delay(50);
                        }

                        toolStripStatusLabel1.Text = "Файли видалено";
                        button2.Enabled = true;
                    }
                    else
                    {
                        toolStripStatusLabel1.Text = "Файли вже видалені або відсутні";
                    }
                }
                catch (Exception ex)
                {
                    ShowErrorMessage($"Помилка при видаленні файлів: {ex.Message}");
                }
            }
        }

        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            autoUpdateTimer.Interval = checkBox2.Checked ? autoUpdateInterval * 60 * 1000 : int.MaxValue;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            autoUpdateInterval = Convert.ToInt32(comboBox1.SelectedItem);

            if (autoUpdateInterval > 0)
            {
                autoUpdateTimer.Interval = checkBox2.Checked ? autoUpdateInterval * 60 * 1000 : int.MaxValue;
            }
        }

        private void autoUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                button2_Click(sender, e);
            }
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenLink("https://crowdin.com/project/star-citizen-localization-ua/invite?h=5459dc1e3bc7eb2319809d529deea2a11952526");
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenLink("https://discord.gg/QVV2G2aKzf");
        }

        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenLink("https://github.com/Vova-Bob/SC_localization_UA");
        }

        private void OpenLink(string url)
        {
            System.Diagnostics.Process.Start(url);
        }
    }
}
