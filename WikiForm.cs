using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;  // Додайте через NuGet: Newtonsoft.Json

namespace SCLOCUA
{
    public partial class WikiForm : Form
    {
        private Dictionary<string, string> wikiDictionary = new Dictionary<string, string>();
        private const string WikiUrl = "https://api.github.com/repos/Vova-Bob/SC_localization_UA/contents/wiki.ini";
        private HttpClient client;
        private int _autoCompleteIndex = 0; // Індекс поточного терміна
        private List<string> autoCompleteKeys = new List<string>(); // Список всіх термінів для автодоповнення

        public WikiForm()
        {
            InitializeComponent();
            InitializeHttpClient();
            button1.Click += button1_Click;
            this.Load += WikiForm_Load;
            textBox1.KeyDown += textBox1_KeyDown;
            textBox1.MouseWheel += textBox1_MouseWheel; // Додано обробник прокручування миші для textBox1
            richTextBox1.MouseWheel += richTextBox1_MouseWheel; // Додано обробник прокручування миші для richTextBox1
        }

        // Ініціалізація HTTP клієнта
        private void InitializeHttpClient()
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "SCLOCUA");
        }

        private async void WikiForm_Load(object sender, EventArgs e)
        {
            button1.Enabled = false;
            string wikiText = await DownloadWikiTextAsync(WikiUrl);

            if (!string.IsNullOrEmpty(wikiText))
            {
                wikiDictionary = ParseWikiText(wikiText);
                autoCompleteKeys = wikiDictionary.Keys.ToList(); // Створюємо список ключів для автодоповнення
                button1.Enabled = true;
                InitializeAutoComplete();
            }
            else
            {
                ShowError("Не вдалося завантажити словник.");
            }
        }

        // Завантаження тексту з GitHub API
        private async Task<string> DownloadWikiTextAsync(string url)
        {
            try
            {
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP статус код: {response.StatusCode}");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var jsonData = JsonConvert.DeserializeObject<GitHubFileContent>(jsonResponse);

                string encodedContent = jsonData?.content;
                if (encodedContent == null)
                {
                    throw new Exception("Не вдалося отримати закодований вміст.");
                }

                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedContent));
            }
            catch (Exception ex)
            {
                ShowError($"Помилка при завантаженні словника: {ex.Message}");
                return string.Empty;
            }
        }

        // Парсинг тексту словника
        private Dictionary<string, string> ParseWikiText(string wikiText)
        {
            var dictionary = new Dictionary<string, string>();
            var lines = wikiText.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("#") || string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                var parts = trimmedLine.Split(new[] { '=' }, 2);

                if (parts.Length == 2)
                {
                    string key = parts[0].Trim().ToUpperInvariant();
                    string value = parts[1].Trim();

                    if (!dictionary.ContainsKey(key))
                        dictionary[key] = value;
                }
            }

            return dictionary;
        }

        // Ініціалізація автодоповнення для текстового поля
        private void InitializeAutoComplete()
        {
            textBox1.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            textBox1.AutoCompleteSource = AutoCompleteSource.CustomSource;

            AutoCompleteStringCollection autoComplete = new AutoCompleteStringCollection();
            autoComplete.AddRange(autoCompleteKeys.ToArray());
            textBox1.AutoCompleteCustomSource = autoComplete;
        }

        // Обробка натискання кнопки пошуку
        private void button1_Click(object sender, EventArgs e)
        {
            string searchTerm = textBox1.Text.Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(searchTerm))
            {
                ShowWarning("Введіть термін для пошуку.");
                return;
            }

            if (wikiDictionary.ContainsKey(searchTerm))
            {
                richTextBox1.Text = $"{searchTerm} - {wikiDictionary[searchTerm]}";
            }
            else
            {
                richTextBox1.Text = "Термін не знайдено.";
            }
        }

        // Обробка натискання Enter у текстовому полі
        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                button1_Click(sender, e);
                e.SuppressKeyPress = true; // Відміна системного сигналу
            }
        }

        // Покращена обробка помилок
        private void ShowError(string message)
        {
            MessageBox.Show(message, "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // Попередження користувача
        private void ShowWarning(string message)
        {
            MessageBox.Show(message, "Попередження", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Обробка прокручування миші в textBox1
        private void textBox1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0) // Прокручування вгору
            {
                _autoCompleteIndex = Math.Max(0, _autoCompleteIndex - 1);
            }
            else if (e.Delta < 0) // Прокручування вниз
            {
                _autoCompleteIndex = Math.Min(autoCompleteKeys.Count - 1, _autoCompleteIndex + 1);
            }

            string term = autoCompleteKeys[_autoCompleteIndex];
            textBox1.Text = term;
            textBox1.SelectionStart = term.Length; // Переміщення курсору в кінець терміна

            // Оновлення тексту в richTextBox1 з відповіддю
            if (wikiDictionary.ContainsKey(term))
            {
                richTextBox1.Text = $"{term} - {wikiDictionary[term]}";
            }
        }

        // Обробка прокручування миші в richTextBox1
        private void richTextBox1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0) // Прокручування вгору
            {
                _autoCompleteIndex = Math.Max(0, _autoCompleteIndex - 1);
            }
            else if (e.Delta < 0) // Прокручування вниз
            {
                _autoCompleteIndex = Math.Min(autoCompleteKeys.Count - 1, _autoCompleteIndex + 1);
            }

            string term = autoCompleteKeys[_autoCompleteIndex];
            textBox1.Text = term;
            textBox1.SelectionStart = term.Length; // Переміщення курсору в кінець терміна

            // Оновлення тексту в richTextBox1 з відповіддю
            if (wikiDictionary.ContainsKey(term))
            {
                richTextBox1.Text = $"{term} - {wikiDictionary[term]}";
            }
        }
    }

    // Клас для десеріалізації відповіді з GitHub API
    public class GitHubFileContent
    {
        public string content { get; set; }
    }
}
