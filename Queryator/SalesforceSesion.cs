using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Queryator
{
    public class SalesforceSesion
    {
        /*Singleton */
        private static SalesforceSesion _instancia = new SalesforceSesion();
        private string _url;

        public static SalesforceSesion Instancia
        {
            get
            {
                return _instancia;
            }
        }
        /*constructor*/
        private SalesforceSesion()
        {
         
        }

        /*Propiedades*/
        public string token { get; set; }
        public string urlLogin
        {
            get
            {                
                if (sandBox)
                    return "https://test.salesforce.com/services/oauth2/token";
                else
                    return "https://login.salesforce.com/services/oauth2/token";
                
            }
            set {}
        }
        public string urlServer { get; set; }
        public string api { get; set; }
        public bool sandBox { get; set; }

        public string user { get; set; }
        public string password { get; set; }

    }
}
