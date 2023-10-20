using Salesforce.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Security.Cryptography;
using System.Reflection;
using System.Net;

namespace Queryator
{
    /// <summary>
    /// Interaction logic for Login.xaml
    /// </summary>
    public partial class Login : Window
    {
        public string user { get; set; }
        public string pass { get; set; }

        public Login()
        {
            InitializeComponent();
        }

        public static string Protect(string str)
        {
            byte[] entropy = Encoding.ASCII.GetBytes(Assembly.GetExecutingAssembly().FullName);
            byte[] data = Encoding.ASCII.GetBytes(str);
            string protectedData = Convert.ToBase64String(ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser));
            return protectedData;
        }

        private async void buttonAceptar_Click(object sender, RoutedEventArgs e)
        {
            //Leemos los 2 valores y hacemos login en salesforce
            user = this.textBoxUsuario.Text;
            pass = this.textBoxContrasena.Password;

            //user = "Sistemas.vol@iberostar.com";
            //pass = "Wacawaca316";

            SalesforceSesion.Instancia.sandBox = this.checkBoxSandBox.IsChecked ?? false;

            var auth = new AuthenticationClient();

            try
            {
                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                await auth.UsernamePasswordAsync(Constantes.ConsumerKey, Constantes.ConsumerSecret, this.user, this.pass, SalesforceSesion.Instancia.urlLogin);
                SalesforceSesion.Instancia.token = auth.AccessToken;
                SalesforceSesion.Instancia.urlServer = auth.InstanceUrl;
                SalesforceSesion.Instancia.api = auth.ApiVersion;
                SalesforceSesion.Instancia.user = this.user;
                SalesforceSesion.Instancia.password = this.pass;

                //si se ha picado la opcion de guardar usuario persistimos a fichero
                if (this.checkBoxSaveUser.IsChecked == true)
                {
                    var path = System.IO.Directory.GetCurrentDirectory();
                    var userFile = path + @"\user.data";
                    if (File.Exists(userFile))
                        File.Delete(userFile);

                    using (var sw = new StreamWriter(userFile))
                    {
                        sw.WriteLine(this.checkBoxSandBox.IsChecked + ";" + this.user + ";" + Protect(this.pass));
                    }

                }

                this.DialogResult = true;
                this.Close();
            }
            catch (SystemException ioex)
            {
                this.labelError.Content = "Error accediendo al disco.";
                this.labelError2.Content= "Asegurese de ejecutar la app como administrador.";
            }
            catch (Exception ex)
            {
                this.labelError.Content = "No se ha podido conectar con salesforce.";
                //this.DialogResult = DialogResult.No;
                //this.DialogResult = false;
            }
        }
    }
}
