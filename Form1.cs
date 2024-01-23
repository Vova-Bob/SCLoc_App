using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCLOCUA
{
    public partial class Form1 : Form
    {
        private string selectedFolderPath = "";
        private bool folderDialogShown = false;
        private int autoUpdateInterval = 1;

        private Timer autoUpdateTimer = new Timer();

        public Form1()
        {
            InitializeComponent();
            button1.Click += button1_Click;
            LoadSavedFolderPath();
            this.FormClosing += Form1_FormClosing;
            checkBox2.CheckedChanged += checkBox2_CheckedChanged;
            comboBox1.SelectedIndexChanged += comboBox1_SelectedIndexChanged;
            this.MaximizeBox = false;
            this.Icon = Properties.Resources.Icon;
            autoUpdateTimer.Interval = autoUpdateInterval * 60 * 1000;
            autoUpdateTimer.Tick += autoUpdateTimer_Tick;
            autoUpdateTimer.Start();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!folderDialogShown)
            {
                folderDialogShown = true;
                using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.SelectedPath = selectedFolderPath;
                    DialogResult result = folderDialog.ShowDialog();

                    if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
                    {
                        selectedFolderPath = folderDialog.SelectedPath;
                        SaveSelectedFolderPath();
                        UpdateLabel();
                        toolStripStatusLabel1.Text = "Перейдіть до встановлення локалізації";
                        button2.Enabled = true;
                    }
                }
                folderDialogShown = false;
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
                        File.Copy("user.cfg", Path.Combine(selectedFolderPath, "user.cfg"), true);
                        toolStripProgressBar1.Value++;
                    }

                    await DownloadFileAsync("https://raw.githubusercontent.com/Vova-Bob/SC_localization_UA/main/Data/Localization/korean_(south_korea)/global.ini", Path.Combine(selectedFolderPath, "Data/Localization/korean_(south_korea)/global.ini"));
                    toolStripProgressBar1.Value++;

                    if (userCfgExists || globalIniExists)
                    {
                        toolStripStatusLabel1.Text = "Локалізацію оновлено";
                    }
                    else
                    {
                        toolStripStatusLabel1.Text = "Локалізацію встановлено";
                    }

                    autoUpdateTimer.Start();
                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Помилка при завантаженні/оновленні файлів: {ex.Message}", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    button2.Enabled = true;
                }
            }
        }

        private async Task DownloadFileAsync(string url, string filePath)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                byte[] fileData = await httpClient.GetByteArrayAsync(url);
                string directoryPath = Path.GetDirectoryName(filePath);

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                await Task.Run(() => File.WriteAllBytes(filePath, fileData));
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            folderDialogShown = false;
        }

        private void LoadSavedFolderPath()
        {
            if (File.Exists("config.ini"))
            {
                selectedFolderPath = File.ReadAllText("config.ini");
                UpdateLabel();
                button2.Enabled = !string.IsNullOrWhiteSpace(selectedFolderPath);
            }
        }

        private void SaveSelectedFolderPath()
        {
            File.WriteAllText("config.ini", selectedFolderPath);
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
                    MessageBox.Show($"Помилка при видаленні файлів: {ex.Message}", "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            autoUpdateTimer.Interval = checkBox2.Checked ? autoUpdateInterval * 60 * 1000 : Int32.MaxValue;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            autoUpdateInterval = Convert.ToInt32(comboBox1.SelectedItem);
            
            if (autoUpdateInterval > 0)
            {
                autoUpdateTimer.Interval = checkBox2.Checked ? autoUpdateInterval * 60 * 1000 : Int32.MaxValue;
            }
        }

        private void autoUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                button2_Click(sender, e); 
            }
        }

        private void linkLabel1_LinkClicked_1(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://crowdin.com/project/star-citizen-localization-ua/invite?h=5459dc1e3bc7eb2319809d529deea2a11952526");
        }

        private void linkLabel2_LinkClicked_1(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://discord.gg/QVV2G2aKzf");
        }

        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/Vova-Bob/SC_localization_UA");
        }
    }
}
