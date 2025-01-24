using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Empoli.Executing;

namespace Empoli
{
    public partial class Login : System.Windows.Forms.Form
    {
        public Login()
        {
            InitializeComponent();
        }

        private string userId { get; set; }

        private void button1_Click(object sender, EventArgs e)
        {
            var handler = new System.Net.Http.HttpClientHandler();


            using (var client = new System.Net.Http.HttpClient(handler))
            {

                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                var email = emailField.Text;
                var password = passwordField.Text;

                var json = $"{{ \"email\": \"{email}\", \"password\": \"{password}\" }}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.empoli.ai/login")
                {
                    Content = content
                };

                var response = client.SendAsync(request).GetAwaiter().GetResult();

                var responseBody = response.Content.ReadAsStringAsync().Result;


                if (responseBody == null)
                {
                    return;
                } else
                {

                    ResponseLoginAPI searchResponse = JsonConvert.DeserializeObject<ResponseLoginAPI>(responseBody);

                    userId = searchResponse.Id;

                    this.DialogResult = DialogResult.OK;

                }

            }

        }

        public string getUserId()
        {
            return userId;
        }
        private void Login_Load(object sender, EventArgs e)
        {

        }

        private void emailField_TextChanged(object sender, EventArgs e)
        {

        }

        private void passwordField_TextChanged(object sender, EventArgs e)
        {

        }

        private void cancel_Click(object sender, EventArgs e)
        {

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string url = "https://app.empoli.ai/register";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir o link: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }
    }
}
