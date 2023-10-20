using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Salesforce.Common;
using Salesforce.Force;
using Salesforce.Common.Models;
using System.Dynamic;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using System.Threading;

using System.Reflection;
using System.Security.Cryptography;
using System.Media;

namespace Queryator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private object threadLock = new object();
        public volatile int numquerys;
        public String objectChunk { get; set; }
        public Dictionary<int, List<string>> mapaChunks { get; set; }
       
        public MainWindow()
        {
            InitializeComponent();
            this.textBoxOutputFile.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);            
        }

        public static string Unprotect(string str)
        {
            byte[] protectedData = Convert.FromBase64String(str);
            byte[] entropy = Encoding.ASCII.GetBytes(Assembly.GetExecutingAssembly().FullName);
            string data = Encoding.ASCII.GetString(ProtectedData.Unprotect(protectedData, entropy, DataProtectionScope.CurrentUser));
            return data;
        }

        //selecciona la ruta de salida
        private void button1_Click(object sender, RoutedEventArgs e)
        {
            var FD = new System.Windows.Forms.FolderBrowserDialog();
            if (FD.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                this.textBoxOutputFile.Text = FD.SelectedPath;
            }

        }

        private void textBlockUserSalir_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            //Cerramos el formulario
            //Borramos el fichero de login
            //Abrimos login
            //this.Close();
            var actualpath = System.IO.Directory.GetCurrentDirectory();
            var userFile = actualpath + @"\user.data";
            File.Delete(userFile);
            var login = new Login();
            if (login.ShowDialog() == false)
                this.Close();
            else
            {
                this.Activate();
                this.textBlockUser.Text = SalesforceSesion.Instancia.user;
            }
        }

       
        private void Window_Initialized_1(object sender, EventArgs e)
        {
            //Comprobamos si existe un fichero de usuarios
            var actualpath = System.IO.Directory.GetCurrentDirectory();
            var userFile = actualpath + @"\user.data";

            try
            {
                if (File.Exists(userFile))
                {
                    //recuperamos usuario y contraseña
                    using (var sr = new StreamReader(userFile))
                    {
                        var line = sr.ReadLine();
                        var data = line.Split(';');

                        if (data.Length > 0)
                        {
                            var pass = data[2];
                            if (pass != null)
                            {
                                SalesforceSesion.Instancia.password = Unprotect(pass);
                                SalesforceSesion.Instancia.user = data[1];
                                this.textBlockUser.Text = data[1];
                                SalesforceSesion.Instancia.sandBox = Convert.ToBoolean(data[0]);
                            }
                        }
                    }
                }
                else
                {
                    //Lanzamos login
                    var login = new Login();
                    if (login.ShowDialog() == false)
                        this.Close();
                    this.textBlockUser.Text = SalesforceSesion.Instancia.user;
                }
            }
            catch (Exception ex)
            {
                //Borramos el fichero
                try {
                    File.Delete(userFile);
                }
                catch { }

                SalesforceSesion.Instancia.user = null;
                SalesforceSesion.Instancia.password = null;

                //Lanzamos login
                var login = new Login();
                if (login.ShowDialog() == false)
                    this.Close();
            }
          
        }
        
        private async Task<bool> doLogin()
        {
            var auth = new AuthenticationClient();            

            try
            {
                //if (SalesforceSesion.Instancia.token == null)
                //{
                    await auth.UsernamePasswordAsync(Constantes.ConsumerKey,
                                                        Constantes.ConsumerSecret,
                                                        SalesforceSesion.Instancia.user,
                                                        SalesforceSesion.Instancia.password,
                                                        SalesforceSesion.Instancia.urlLogin);

                    SalesforceSesion.Instancia.token = auth.AccessToken;
                    SalesforceSesion.Instancia.urlServer = auth.InstanceUrl;
                    SalesforceSesion.Instancia.api = auth.ApiVersion;
                //}
                return true;
                
            }
            catch (Exception ex)
            {
                SalesforceSesion.Instancia.user = null;
                SalesforceSesion.Instancia.password = null;
                SalesforceSesion.Instancia.token = null;
                return false;
            }
        }
       
        private async void button_Click(object sender, RoutedEventArgs e)
        {
            this.listBoxResultados.Items.Clear();

            //Comprobaciones previas al lanzamiento
            if (String.IsNullOrEmpty(this.textBoxQuery.Text))
            {
                this.listBoxResultados.Items.Insert(0, "Escribe una query para comenzar");
                return;
            }

            var resultadoLogin = await doLogin();
            
            if (resultadoLogin)
            {
                try
                {
                    await getPKChunks();
                    await realizarQuerysPorChunks(this.textBoxQuery.Text, mapaChunks);
                    SystemSounds.Beep.Play();
                }
                catch (Exception ex)
                {
                    this.listBoxResultados.Items.Insert(0, "Error inesperado " + ex.Message);
                    this.mapaChunks = null;
                }
            }
            else
            {
                var login = new Login();
                if (login.ShowDialog() == false)
                    this.Close();
            }
            
        }

        public async Task getPKChunks()
        {
            var client = new ForceClient(SalesforceSesion.Instancia.urlServer, SalesforceSesion.Instancia.token, SalesforceSesion.Instancia.api);

            var chunkSize = int.Parse(this.textBoxNumTrozos.Text);
    
            var procesados = 0;
            var actualChunk = 0;
            string cursorId = "";
            QueryResult<dynamic> result;

            var query = this.textBoxQuery.Text;
            var queryInicio = query.ToLower().IndexOf(" from ");

            var objeto = query.Substring(queryInicio, query.Length - queryInicio).Split(' ')[2];

            if (mapaChunks == null || (mapaChunks != null && objectChunk != objeto))
            {
                objectChunk = objeto;

                mapaChunks = new Dictionary<int, List<string>>();
                this.listBoxResultados.Items.Insert(0, "Recuperando lista de Ids de toda la organizacion...");
                result = await client.QueryAsync<dynamic>("Select Id from "+ objeto + " order by Id");

                cursorId = result.nextRecordsUrl.Split('-')[0];
                mapaChunks[actualChunk] = new List<string>() { ((dynamic)result.records[0]).Id.Value };
                procesados = procesados + 2000;

                var loopSize = Math.Floor((decimal)(result.totalSize / chunkSize));
                var remainder = result.totalSize % chunkSize;

                var runningTotal = (chunkSize * loopSize);
                if ((result.totalSize - runningTotal) < chunkSize)
                {
                    remainder = 0;
                }
                List<Query> listaQuerys = new List<Query>();
                for (var i = 1; i <= loopSize; i++)
                {
                    var offset = chunkSize * i;

                    var q = new Query()
                    {
                        query = cursorId + "-" + offset,
                        numQuery = i,
                        UI = this,
                    };
                    listaQuerys.Add(q);
                }

                if (listaQuerys.Count > 0)
                {
                    var numParalelos = int.Parse(this.textBoxParalelQuerys.Text);
                    var bloqueActual = 0;
                    while (numParalelos * bloqueActual <= listaQuerys.Count)
                    {
                        var querys = listaQuerys.Skip(bloqueActual * numParalelos).Take(numParalelos);
                        await Task.Run(() =>
                        {
                            Parallel.ForEach(querys, x => x.runQueryLocator().Wait());
                        });
                        bloqueActual++;

                    }
                }

                this.listBoxResultados.Items.Insert(0, "Lanzamos QUERYS!!!");
            }
            else
            {
                this.listBoxResultados.Items.Insert(0, "Reutilizamos Ids");
            }

            /* Para debug
            for (var i = 0; i < mapaChunks.Keys.Count; i++)
            {
                var firstId = mapaChunks[i][0];
                var lastId = "";
                if (mapaChunks[i].Count > 1)
                    lastId = mapaChunks[i][1];
                Console.WriteLine(firstId + " - " + lastId);
            }
            */

        }

        public async Task realizarQuerysPorChunks(string query, Dictionary<int, List<string>> mapaChunks)
        {
            List<Query> listaQuerys = new List<Query>();

            //Quitamos todos los campos de la query. Solo queremos obtener Ids para que sea muy rapido todo
            //var queryAux = "SELECT Id " + query.Substring(query.ToLower().IndexOf(" from "));
            var queryAux = query;

            //creamos la carpeta "queryator" dentro de la ruta elegida
            if (!Directory.Exists(this.textBoxOutputFile + @"\queryator"))
                Directory.CreateDirectory(this.textBoxOutputFile.Text + @"\queryator");
            else
            {
                DirectoryInfo directory = new DirectoryInfo(this.textBoxOutputFile + @"\queryator");
                //Borramos contenido
                foreach (FileInfo file in directory.GetFiles())
                    file.Delete();
            }

            for (var i = 0; i < mapaChunks.Keys.Count; i++)
            {
                var firstId = mapaChunks[i][0];
                var lastId = "";
                var queryFinal = "";
                var queryCompleta = "";

                if (mapaChunks[i].Count > 1)
                    lastId = mapaChunks[i][1];

                if (!query.ToLower().Contains(" where "))
                    queryFinal = " WHERE (";
                else
                    queryFinal = " AND (";

                if (lastId != "")
                    queryFinal = queryFinal + "Id >='" + firstId + "' AND Id < '" + lastId + "')";
                else
                    queryFinal = queryFinal + "Id >='" + firstId + "')";

                queryCompleta = queryAux + " " + queryFinal;

                var q = new Query()
                {
                    file = String.Format(this.textBoxOutputFile.Text + @"\queryator\queryator{0}.txt", i),
                    numQuery = i,
                    query = queryCompleta,
                    UI = this,
                    timeout = int.Parse(this.textBoxTimeout.Text),
                    numMaxErrores = Convert.ToInt32(this.textBoxReintentos.Text)
                };
                listaQuerys.Add(q);
            }
          
            var bloqueActual = 0;
            var numParalelos = int.Parse(this.textBoxParalelQuerys.Text);
            try
            {
                if (numParalelos == 0)
                {
                    //todos de golpe
                    await Task.Run(() =>
                    {
                        Parallel.ForEach(listaQuerys, x => x.runQuery().Wait());
                    });
                }
                else if (numParalelos > 0)
                {
                    while (numParalelos * bloqueActual <= listaQuerys.Count)
                    {
                        var querys = listaQuerys.Skip(bloqueActual * numParalelos).Take(numParalelos);
                        await Task.Run(() =>
                        {
                            Parallel.ForEach(querys, x => x.runQuery().Wait());
                        });
                        bloqueActual++;
                    }
                }

                //juntamos los ficheros y terminamos
                using (var output = File.Create(String.Format(@"{0}\queryatorFinal-{1}.txt", this.textBoxOutputFile.Text,DateTime.Now.Ticks)))
                {
                    foreach (var file in Directory.GetFiles(this.textBoxOutputFile.Text + @"\queryator"))
                    {
                        using (var input = File.OpenRead(file))
                        {
                            input.CopyTo(output);
                        }
                        File.Delete(file);
                    }
                    Directory.Delete(this.textBoxOutputFile.Text + @"\queryator");
                    this.listBoxResultados.Items.Insert(0, "Fichero Generado! queryator al rescate una vez mas!!");
                }
            }
            catch(Exception ex)
            {
                this.listBoxResultados.Items.Insert(0, "Cancelamos proceso. Revise los parametros de entrada. " + ex.Message);
            }                                    
        }
        
    }

    public class Query
    {
        private object threadLock = new object();
        public MainWindow UI { get; set; }
        public string query { get; set; }
        public string file { get; set; }
        public int numQuery { get; set; }
        public int timeout { get; set; }
        public bool isCustom { get; set; }
        public int numMaxErrores { get; set; }

        public async Task runQuery()
        {
            await Task.Run(async () =>
            {
                var threadId = Thread.CurrentThread.ManagedThreadId;

                await UI.Dispatcher.BeginInvoke(
                 DispatcherPriority.Background,
                 new Action(() => UI.listBoxResultados.Items.Insert(0, "Ejecucion query " + numQuery + " thread: " + threadId + " at " + DateTime.Now.ToString("hh:mm:ss.fff"))));

                Console.WriteLine("Query {0} started on thread {1} at {2}.",
                     numQuery, Thread.CurrentThread.ManagedThreadId, DateTime.Now.ToString("hh:mm:ss.fff"));

                var client = new ForceClient(SalesforceSesion.Instancia.urlServer, SalesforceSesion.Instancia.token, SalesforceSesion.Instancia.api);

                var terminado = false;
                QueryResult<dynamic> result = null;
                List<dynamic> customResult = null;
                var numErrores = 0;

                while (!terminado)
                {
                    try
                    {
                        if (isCustom)
                        {
                            customResult = await client.CallCustomQuery<dynamic>(query, timeout);
                            if (customResult != null && customResult.Count > 0)
                            {
                                using (var sw = new StreamWriter(file, true))
                                {
                                    foreach (dynamic registro in customResult)
                                    {
                                        sw.WriteLine(((dynamic)registro).Id.Value);
                                    }
                                    sw.Flush();
                                }
                            }
                            terminado = true;
                        }
                        else
                        {
                            result = await client.QueryAsync<dynamic>(query, 2000, timeout);
                            if (result != null && result.records.Count > 0)
                            {
                                using (var sw = new StreamWriter(file, true))
                                {
                                    foreach (dynamic registro in result.records)
                                    {
                                        var sb = new StringBuilder();
                                        foreach (Newtonsoft.Json.Linq.JProperty propertie in registro)
                                        {   
                                            if (propertie.Name != "attributes")                                       
                                                 sb.Append(propertie.Value + ",");
                                        }
                                        sw.WriteLine(sb.ToString());                                                    
                                    }
                                    sw.Flush();
                                }
                            }
                            while (result.done == "False")
                            {
                                result = await client.QueryContinuationAsync<dynamic>(result.nextRecordsUrl);
                                if (result != null && result.records.Count > 0)
                                {
                                    using (var sw = new StreamWriter(file, true))
                                    {
                                        foreach (dynamic registro in result.records)
                                        {
                                            var sb = new StringBuilder();
                                            foreach (Newtonsoft.Json.Linq.JProperty propertie in registro)
                                            {
                                                if (propertie.Name != "attributes")
                                                    sb.Append(propertie.Value + ",");
                                            }
                                            sw.WriteLine(sb.ToString());
                                        }
                                        sw.Flush();
                                    }
                                }
                            }
                            terminado = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        numErrores++;
                        File.Delete(file);

                        //if (!ex.ToString().Contains("timeout") &&
                        //    !ex.ToString().ToLower().Contains("locator") &&
                        //    !ex.ToString().ToLower().Contains("se canceló una tarea.") &&
                        //    !ex.ToString().ToLower().Contains("your query request was running for too long") &&
                        //    !ex.ToString().ToLower().Contains("invalid batch size salesforce") &&
                        //    !ex.ToString().ToLower().Contains("error al enviar la solicitud")                           
                        //   )
                        if (numErrores >= numMaxErrores)
                        {
                            terminado = true;

                            await UI.Dispatcher.BeginInvoke(
                             DispatcherPriority.Background,
                             new Action(() => UI.listBoxResultados.Items.Insert(0, "FIN::EXCEPCION " + numQuery + " thread: " + threadId + " at " + DateTime.Now.ToString("hh:mm:ss.fff") + ex.Message)));

                            throw ex;
                        }
                        else
                        {
                            client = new ForceClient(SalesforceSesion.Instancia.urlServer, SalesforceSesion.Instancia.token, SalesforceSesion.Instancia.api);

                            await UI.Dispatcher.BeginInvoke(
                             DispatcherPriority.Background,
                             new Action(() => UI.listBoxResultados.Items.Insert(0, "REINTENTAMOS EXCEPCION " + numQuery + " thread: " + threadId + " at " + DateTime.Now.ToString("hh:mm:ss.fff") + ex.Message)));
                        }

                    }
                }

                Console.WriteLine("Query {0} stopped at {1}.",
                    numQuery, DateTime.Now.ToString("hh:mm:ss.fff"));

                await UI.Dispatcher.BeginInvoke(
                 DispatcherPriority.Background,
                 new Action(() => UI.listBoxResultados.Items.Insert(0, "Fin query " + numQuery + " thread: " + threadId + " at " + DateTime.Now.ToString("hh:mm:ss.fff"))));

            });

        }

        public async Task runQueryLocator()
        {
            await Task.Run(async () =>
            {
                var threadId = Thread.CurrentThread.ManagedThreadId;

                var client = new ForceClient(SalesforceSesion.Instancia.urlServer, SalesforceSesion.Instancia.token, SalesforceSesion.Instancia.api);

                //repetimmos query
                var result = await client.QueryContinuationAsync<dynamic>(query);

                await UI.Dispatcher.BeginInvoke(
               DispatcherPriority.Background,
               new Action(() => UI.listBoxResultados.Items.Insert(0, "Recuperado PK Chunk " + numQuery + " thread: " + threadId + " at " + DateTime.Now.ToString("hh:mm:ss.fff"))));

                var terminado = false;

                while (!terminado)
                {
                    try
                    {
                        if (result.records.Count > 0)
                        {
                            lock (threadLock)
                            {
                                if (UI.mapaChunks.ContainsKey(numQuery))
                                {
                                    //var aux = UI.mapaChunks[numQuery][0];
                                    UI.mapaChunks[numQuery].Insert(0, ((dynamic)result.records[0]).Id.Value);
                                    //UI.mapaChunks[numQuery].Add(aux);
                                }
                                else
                                {
                                    UI.mapaChunks[numQuery] = new List<string>();
                                    UI.mapaChunks[numQuery].Add(((dynamic)result.records[0]).Id.Value);
                                }

                                //cambiamos de chunk
                                if (UI.mapaChunks.ContainsKey(numQuery - 1))
                                {
                                    UI.mapaChunks[numQuery - 1].Add(((dynamic)result.records[0]).Id.Value);
                                }
                                else
                                {
                                    UI.mapaChunks[numQuery - 1] = new List<string>() { ((dynamic)result.records[0]).Id.Value };
                                }
                                terminado = true;
                            }                            
                        }
                    }
                    catch (Exception ex)
                    {
                        ex = ex;
                        terminado = false;
                    }

                }

            });
        }

    }
      
    public static class DynamicExtensions
    {
        public static dynamic ToDynamic(this object value)
        {
            IDictionary<string, object> expando = new ExpandoObject();

            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(value.GetType()))
                expando.Add(property.Name, property.GetValue(value));

            return expando as ExpandoObject;
        }
    }
}
