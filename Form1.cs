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
        private const string UserCfgFileName = "user.cfg";
        private const string GlobalIniFileName = "global.ini";
        private const string GithubGistUrlPattern = @"https://gist.github.com/\w+/\w+";

        private readonly HttpClient httpClient = new HttpClient();
        private readonly Timer autoUpdateTimer = new Timer();
        private string selectedFolderPath = "";

        public Form1()
        {
            InitializeComponent();
            this.MaximizeBox = false;
            this.Icon = Properties.Resources.Icon;
            selectedFolderPath = Properties.Settings.Default.LastSelectedFolderPath;

            InitializeUI();
            InitializeEvents();
            InitializeTimer();
        }

        private void InitializeUI()
        {
            label1.Text = "Виберіть шлях до папки StarCitizen/LIVE";
            button2.Text = "Встановити локалізацію";
            button2.Enabled = false; 

            if (!string.IsNullOrWhiteSpace(selectedFolderPath) && Directory.Exists(selectedFolderPath))
            {
                UpdateLabel();
                button2.Enabled = true;
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                bool globalIniExists = File.Exists(Path.Combine(selectedFolderPath, $"Data/Localization/korean_(south_korea)/{GlobalIniFileName}"));
                button2.Text = userCfgExists || globalIniExists ? "Оновити локалізацію" : "Встановити локалізацію";
            }
        }

        private void InitializeEvents()
        {
            button1.Click += SelectFolderButtonClick;
            checkBox2.CheckedChanged += AutoUpdateCheckBoxChanged;
            comboBox1.SelectedIndexChanged += AutoUpdateIntervalChanged;
            button2.Click += UpdateLocalizationButtonClick;
            button3.Click += DeleteFilesButtonClick;
            linkLabel1.LinkClicked += OpenLinkLabel1;
            linkLabel2.LinkClicked += OpenLinkLabel2;
            linkLabel3.LinkClicked += OpenLinkLabel3;
            pictureBox1.Click += OpenPictureBoxLink;
        }

        private void InitializeTimer()
        {
            autoUpdateTimer.Interval = autoUpdateInterval * 60 * 1000;
            autoUpdateTimer.Tick += AutoUpdateTimerTick;
            autoUpdateTimer.Start();
        }

        private void SelectFolderButtonClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.SelectedPath = selectedFolderPath;
                DialogResult result = folderDialog.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
                {
                    selectedFolderPath = folderDialog.SelectedPath;
                    UpdateLabel();
                    bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                    checkBox1.Checked = !userCfgExists;

                    toolStripStatusLabel1.Text = "Перейдіть до встановлення локалізації";
                    button2.Enabled = true;

                    Properties.Settings.Default.LastSelectedFolderPath = selectedFolderPath;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private async void UpdateLocalizationButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
                return;

            toolStripProgressBar1.Maximum = checkBox1.Checked ? 2 : 1;
            toolStripProgressBar1.Value = 0;
            button2.Enabled = false;

            try
            {
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                bool globalIniExists = File.Exists(Path.Combine(selectedFolderPath, $"Data/Localization/korean_(south_korea)/{GlobalIniFileName}"));

                if (!userCfgExists && checkBox1.Checked)
                {
                    CopyFile(UserCfgFileName, Path.Combine(selectedFolderPath, UserCfgFileName));
                    toolStripProgressBar1.Value++;
                    checkBox1.Checked = false;
                }

                string githubGistUrl = "https://raw.githubusercontent.com/Vova-Bob/SC_localization_UA/main/Data/Localization/korean_(south_korea)/global.ini";
                string localFilePath = Path.Combine(selectedFolderPath, $"Data/Localization/korean_(south_korea)/{GlobalIniFileName}");

                await DownloadFileAsync(githubGistUrl, localFilePath);
                toolStripProgressBar1.Value++;

                string gistContent = File.ReadAllText(localFilePath);

                if (DetectGithubGistUrl(gistContent))
                {
                    toolStripStatusLabel1.Text = "Знайдено URL до Github gist";
                }
                else
                {
                    button2.Text = "Оновити локалізацію";

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

        private bool DetectGithubGistUrl(string content)
        {
            string regexPattern = GithubGistUrlPattern;
            Match match = Regex.Match(content, regexPattern);
            return match.Success;
        }

        private void CopyFile(string sourceFileName, string destinationPath)
        {
            File.Copy(sourceFileName, destinationPath, true);
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

                await Task.Run(() =>
                {
                    File.WriteAllBytes(filePath, fileData);
                });

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

        private void DeleteFilesButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
                return;

            try
            {
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                bool globalIniExists = File.Exists(Path.Combine(selectedFolderPath, $"Data/Localization/korean_(south_korea)/{GlobalIniFileName}"));

                if (userCfgExists || globalIniExists)
                {
                    if (userCfgExists)
                    {
                        File.Delete(Path.Combine(selectedFolderPath, UserCfgFileName));
                    }
                    if (globalIniExists)
                    {
                        File.Delete(Path.Combine(selectedFolderPath, $"Data/Localization/korean_(south_korea)/{GlobalIniFileName}"));
                    }

                    int initialValue = toolStripProgressBar1.Value;

                    for (int i = initialValue; i >= 0; i--)
                    {
                        toolStripProgressBar1.Value = i;
                    }

                    toolStripStatusLabel1.Text = "Файли видалено";                 
                    button2.Text = "Встановити локалізацію";
                    checkBox1.Checked = true;
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

        private void AutoUpdateCheckBoxChanged(object sender, EventArgs e)
        {
            autoUpdateTimer.Interval = checkBox2.Checked ? autoUpdateInterval * 60 * 1000 : int.MaxValue;
        }

        private void AutoUpdateIntervalChanged(object sender, EventArgs e)
        {
            autoUpdateInterval = Convert.ToInt32(comboBox1.SelectedItem);

            if (autoUpdateInterval > 0)
            {
                autoUpdateTimer.Interval = checkBox2.Checked ? autoUpdateInterval * 60 * 1000 : int.MaxValue;
            }
        }

        private void AutoUpdateTimerTick(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                UpdateLocalizationButtonClick(sender, e);
            }
        }

        private void UpdateLabel()
        {
            label1.Text = selectedFolderPath;
        }

        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void OpenLinkLabel1(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenLink("https://crowdin.com/project/star-citizen-localization-ua/invite?h=5459dc1e3bc7eb2319809d529deea2a11952526");
        }

        private void OpenLinkLabel2(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenLink("https://discord.gg/QVV2G2aKzf");
        }

        private void OpenLinkLabel3(object sender, LinkLabelLinkClickedEventArgs e)
        {
            OpenLink("https://github.com/Vova-Bob/SC_localization_UA");
        }

        private void OpenPictureBoxLink(object sender, EventArgs e)
        {
            OpenLink("https://usf.42web.io");
        }

        private void OpenLink(string url)
        {
            System.Diagnostics.Process.Start(url);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                bool userCfgExists = File.Exists(Path.Combine(selectedFolderPath, UserCfgFileName));
                checkBox1.Checked = !userCfgExists;
            }
        }
    }
}
